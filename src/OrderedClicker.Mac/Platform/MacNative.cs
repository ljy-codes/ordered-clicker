using System.Runtime.InteropServices;
using System.Text;
using OrderedClicker.Models;
using OrderedClicker.Services;
using OrderedClicker.Platform;

namespace OrderedClicker.Mac.Platform;

public static class MacNative
{
    private const string Library = "OrderedClickerNative";
    [DllImport(Library)] public static extern int oc_initialize();
    [DllImport(Library)] public static extern uint oc_poll_events();
    [DllImport(Library)] internal static extern int oc_dequeue_hotkey(out MacHotKeyEvent value);
    [DllImport(Library)] public static extern int oc_accessibility(int prompt);
    [DllImport(Library)] public static extern int oc_screen_permission(int prompt);
    [DllImport(Library)] internal static extern int oc_cursor(out int x, out int y);
    [DllImport(Library)] internal static extern int oc_move(int x, int y);
    [DllImport(Library)] internal static extern int oc_click_at(int x, int y, int releaseOnly);
    [DllImport(Library)] internal static extern int oc_own_ui_at(int x, int y);
    [DllImport(Library)] private static extern int oc_probe_window(int x, int y,
        [Out] byte[] application, int applicationCapacity, [Out] byte[] title, int titleCapacity,
        out int foregroundPid, out int windowPid, out int ownProcess);
    [DllImport(Library)] internal static extern int oc_displays([Out] double[] output, int capacity);
    [DllImport(Library)] internal static extern int oc_register_hotkey(int id, uint key, uint modifiers);
    [DllImport(Library)] internal static extern int oc_unregister_hotkey(int id);
    [DllImport(Library)] internal static extern void oc_activate_existing();
    [DllImport(Library)] internal static extern IntPtr oc_sampler_create(int x, int y, int width, int height);
    [DllImport(Library)] internal static extern int oc_sampler_read(IntPtr handle, [Out] byte[] output, int width, int height);
    [DllImport(Library)] internal static extern void oc_sampler_destroy(IntPtr handle);

    public static System.Drawing.Point Cursor()
    {
        if (oc_cursor(out var x, out var y) == 0) throw new IOException("无法读取鼠标位置。");
        return new(x, y);
    }

    public static IReadOnlyList<MonitorSnapshot> Displays()
    {
        var values = new double[32 * 6];
        var count = oc_displays(values, 32);
        if (count <= 0) throw new IOException("没有可用显示器。");
        return Enumerable.Range(0, count).Select(i => new MonitorSnapshot(
            $"mac:{values[i*6]:0}",
            new ScreenBounds((int)values[i*6+1], (int)values[i*6+2], (int)values[i*6+3], (int)values[i*6+4]),
            (uint)Math.Round(96 * values[i*6+5]))).ToArray();
    }

    public static ScreenBounds VirtualBounds(IReadOnlyList<MonitorSnapshot> displays)
    {
        var x = displays.Min(d => d.Bounds.X); var y = displays.Min(d => d.Bounds.Y);
        return new(x, y, displays.Max(d => d.Bounds.Right) - x, displays.Max(d => d.Bounds.Bottom) - y);
    }

    public static CapturedPoint Capture()
    {
        var p = Cursor();
        return CaptureAt(p.X,p.Y);
    }

    public static CapturedPoint CaptureAt(int x, int y)
    {
        var display = Displays().FirstOrDefault(d => d.Bounds.Contains(x, y))
            ?? throw new InvalidOperationException("鼠标不在可用显示器内。");
        return new(x, y, display.DeviceName, display.Bounds, display.Dpi);
    }

    public static IReadOnlyList<MacHotKeyEvent> DrainHotKeys()
    {
        var result=new List<MacHotKeyEvent>();
        while(oc_dequeue_hotkey(out var value)==1) result.Add(value);
        return result;
    }

    public static MacWindowSnapshot? ProbeWindow(int x,int y)
    {
        var application=new byte[4096]; var title=new byte[16384];
        if(oc_probe_window(x,y,application,application.Length,title,title.Length,
            out var foreground,out var hit,out var own)!=1)return null;
        static string Read(byte[] bytes)
        {
            var end=Array.IndexOf(bytes,(byte)0);
            return Encoding.UTF8.GetString(bytes,0,end<0?bytes.Length:end);
        }
        return new(Read(application),Read(title),foreground,hit,own!=0);
    }
}

[StructLayout(LayoutKind.Sequential)]
public readonly struct MacHotKeyEvent(ulong sequence,int id,int x,int y,int positionValid)
{
    public readonly ulong Sequence=sequence;
    public readonly int Id=id, X=x, Y=y, PositionValid=positionValid;
}

public sealed record MacWindowSnapshot(string ApplicationId,string WindowTitle,
    int ForegroundProcessId,int HitProcessId,bool IsOwnProcess);

public static class MacTargetWindowGuard
{
    public static string? Check(TargetWindowBinding? binding,MacWindowSnapshot? window)
    {
        if(binding==null)return null;
        if(!string.Equals(binding.Platform,"macOS",StringComparison.OrdinalIgnoreCase))
            return "目标窗口绑定来自其他系统，请重新绑定或清除。";
        if(window==null||window.ForegroundProcessId<=0||window.HitProcessId<=0||
            string.IsNullOrWhiteSpace(window.ApplicationId)||string.IsNullOrWhiteSpace(window.WindowTitle))
            return "无法确认点击位置的实际窗口，请检查辅助功能权限和目标窗口。";
        if(window.IsOwnProcess)return "点击位置被有序连点器窗口遮挡，请隐藏主窗口或状态窗。";
        if(window.ForegroundProcessId!=window.HitProcessId)
            return "点击位置实际命中的窗口不属于前台应用。";
        if(!string.Equals(binding.ApplicationId,window.ApplicationId,StringComparison.Ordinal)||
            !string.Equals(binding.WindowTitle,window.WindowTitle,StringComparison.Ordinal))
            return "前台应用或点击位置实际命中的窗口与绑定不一致。";
        return null;
    }
}

public sealed class MacPlatformServices
{
    public Func<IReadOnlyList<MonitorSnapshot>> Displays { get; init; }=MacNative.Displays;
    public Func<System.Drawing.Point> Cursor { get; init; }=MacNative.Cursor;
    public Func<bool> Accessibility { get; init; }=()=>MacNative.oc_accessibility(0)==1;
    public Func<bool> ScreenPermission { get; init; }=()=>MacNative.oc_screen_permission(0)==1;
    public Func<int,int,int> OwnUiAt { get; init; }=MacNative.oc_own_ui_at;
    public Func<int,int,MacWindowSnapshot?> ProbeWindow { get; init; }=MacNative.ProbeWindow;
    public Func<IMouseController> CreateMouse { get; init; }=()=>new MacMouseController();
    public Func<TimeSpan,CancellationToken,Task> CountdownDelay { get; init; }=Task.Delay;
    public Func<uint> PollEvents { get; init; }=MacNative.oc_poll_events;
    public Func<IReadOnlyList<MacHotKeyEvent>> DrainHotKeys { get; init; }=MacNative.DrainHotKeys;
    public CapturedPoint CaptureAt(int x,int y)
    {
        var display=Displays().FirstOrDefault(d=>d.Bounds.Contains(x,y))
            ??throw new InvalidOperationException("采点位置不在可用显示器内。");
        return new(x,y,display.DeviceName,display.Bounds,display.Dpi);
    }
}

public sealed class MacMouseController : IMouseController
{
    private (int X, int Y, ScreenBounds Bounds)? _target;
    public void Reposition() { if (_target is { } t) MoveTo(t.X, t.Y, t.Bounds); }
    public void MoveTo(int x, int y) => MoveTo(x, y, MacNative.VirtualBounds(MacNative.Displays()));
    public void MoveTo(int x, int y, ScreenBounds virtualScreen)
    {
        if (!virtualScreen.Contains(x, y)) throw new InvalidOperationException("目标坐标超出桌面。");
        for (var i = 0; i < 3; i++)
        {
            if (MacNative.oc_move(x, y) == 0) throw new InvalidOperationException("请在系统设置中允许有序连点器使用辅助功能。");
            Thread.Sleep(40);
            var p = MacNative.Cursor();
            if (Math.Abs(p.X-x) <= 2 && Math.Abs(p.Y-y) <= 2) { _target = (x, y, virtualScreen); return; }
        }
        throw new InvalidOperationException("鼠标没有到达目标位置，执行已停止。");
    }
    public void LeftClick()
    {
        var target=_target??throw new InvalidOperationException("点击前必须先定位目标。");
        if (MacNative.oc_click_at(target.X,target.Y,0) == 0) throw new InvalidOperationException("点击发送失败，请检查辅助功能权限或窗口遮挡。");
    }
    public bool IsAtPosition(int x,int y)
    {
        var p=MacNative.Cursor();
        return Math.Abs((long)p.X-x)<=2&&Math.Abs((long)p.Y-y)<=2;
    }
    public void EnsureLeftButtonUp()
    {
        if(_target is {} target)MacNative.oc_click_at(target.X,target.Y,1);
    }
}

public sealed class MacScreenSampler : IScreenSampler, IDisposable
{
    private IntPtr _handle;
    private ScreenBounds _region;
    public async Task<byte[]> SampleAsync(ScreenBounds region, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_handle != IntPtr.Zero && _region != region) Dispose();
        if (_handle == IntPtr.Zero)
        {
            _handle = await Task.Run(() => MacNative.oc_sampler_create(region.X, region.Y, region.Width, region.Height));
            _region = region;
            if (_handle == IntPtr.Zero) throw new InvalidOperationException("画面采集无法启动，请检查屏幕录制权限。");
        }
        var scale = Math.Min(1d, Math.Min(160d/region.Width, 90d/region.Height));
        var width = Math.Max(1, (int)Math.Round(region.Width*scale));
        var height = Math.Max(1, (int)Math.Round(region.Height*scale));
        var pixels = new byte[width*height*3];
        for (var i = 0; i < 80; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var status = MacNative.oc_sampler_read(_handle, pixels, width, height);
            if (status == 1) return pixels;
            if (status < 0) throw new InvalidOperationException("屏幕录制权限或采集流已失效，任务已停止。");
            await Task.Delay(100, cancellationToken);
        }
        throw new TimeoutException("未收到有效画面，不能确认画面稳定。");
    }
    public void Dispose()
    {
        if (_handle != IntPtr.Zero) MacNative.oc_sampler_destroy(_handle);
        _handle = IntPtr.Zero;
    }
}

public sealed class MacHotKeyRegistrar : IHotKeyRegistrar
{
    public static readonly IReadOnlyDictionary<ShortcutKey, uint> KeyCodes = new Dictionary<ShortcutKey,uint>
    {
        [ShortcutKey.A]=0,[ShortcutKey.S]=1,[ShortcutKey.D]=2,[ShortcutKey.F]=3,[ShortcutKey.H]=4,[ShortcutKey.G]=5,
        [ShortcutKey.Z]=6,[ShortcutKey.X]=7,[ShortcutKey.C]=8,[ShortcutKey.V]=9,[ShortcutKey.B]=11,
        [ShortcutKey.Q]=12,[ShortcutKey.W]=13,[ShortcutKey.E]=14,[ShortcutKey.R]=15,[ShortcutKey.Y]=16,[ShortcutKey.T]=17,
        [ShortcutKey.D1]=18,[ShortcutKey.D2]=19,[ShortcutKey.D3]=20,[ShortcutKey.D4]=21,[ShortcutKey.D6]=22,
        [ShortcutKey.D5]=23,[ShortcutKey.D9]=25,[ShortcutKey.D7]=26,[ShortcutKey.D8]=28,[ShortcutKey.D0]=29,
        [ShortcutKey.O]=31,[ShortcutKey.U]=32,[ShortcutKey.I]=34,[ShortcutKey.P]=35,[ShortcutKey.Enter]=36,
        [ShortcutKey.L]=37,[ShortcutKey.J]=38,[ShortcutKey.K]=40,[ShortcutKey.N]=45,[ShortcutKey.M]=46,
        [ShortcutKey.Tab]=48,[ShortcutKey.Space]=49,[ShortcutKey.Back]=51,[ShortcutKey.Escape]=53,
        [ShortcutKey.F1]=122,[ShortcutKey.F2]=120,[ShortcutKey.F3]=99,[ShortcutKey.F4]=118,[ShortcutKey.F5]=96,
        [ShortcutKey.F6]=97,[ShortcutKey.F7]=98,[ShortcutKey.F8]=100,[ShortcutKey.F9]=101,[ShortcutKey.F10]=109,
        [ShortcutKey.F11]=103,[ShortcutKey.F12]=111,[ShortcutKey.F13]=105,[ShortcutKey.F14]=107,[ShortcutKey.F15]=113,
        [ShortcutKey.F16]=106,[ShortcutKey.F17]=64,[ShortcutKey.F18]=79,[ShortcutKey.F19]=80,[ShortcutKey.F20]=90,
        [ShortcutKey.Home]=115,[ShortcutKey.End]=119,[ShortcutKey.PageUp]=116,[ShortcutKey.PageDown]=121,
        [ShortcutKey.Delete]=117,[ShortcutKey.Left]=123,[ShortcutKey.Right]=124,[ShortcutKey.Down]=125,[ShortcutKey.Up]=126
    };
    public void Register(HotKeyRegistration registration)
    {
        if (!KeyCodes.TryGetValue(registration.Binding.Key, out var code)) throw new ArgumentException("此主键不支持全局注册。");
        var modifiers = registration.Binding.Modifiers;
        uint flags = 0;
        if (modifiers.HasFlag(ShortcutModifiers.Control)) flags |= 4096;
        if (modifiers.HasFlag(ShortcutModifiers.Alt)) flags |= 2048;
        if (modifiers.HasFlag(ShortcutModifiers.Shift)) flags |= 512;
        if (modifiers.HasFlag(ShortcutModifiers.Windows)) flags |= 256;
        var status = MacNative.oc_register_hotkey(registration.Id, code, flags);
        if (status != 0) throw new InvalidOperationException($"快捷键已被占用或不可用（{status}），请在设置中改用组合键。");
    }
    public void Unregister(int id)
    {
        if (MacNative.oc_unregister_hotkey(id) != 0) throw new InvalidOperationException("注销快捷键失败。");
    }
    public void Dispose() { for (var i=1; i<=3; i++) MacNative.oc_unregister_hotkey(i); }
}
