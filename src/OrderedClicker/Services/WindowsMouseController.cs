using System.ComponentModel;
using System.Runtime.InteropServices;
using OrderedClicker.Core;
using OrderedClicker.Models;
using OrderedClicker.Native;

namespace OrderedClicker.Services;

public sealed class WindowsMouseController : IMouseController
{
    public void MoveTo(int x, int y)
    {
        var bounds = SystemInformation.VirtualScreen;
        MoveTo(x, y, new ScreenBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height));
    }

    public void MoveTo(int x, int y, ScreenBounds virtualScreen)
    {
        var normalized = VirtualScreenCoordinateService.Normalize(x, y, virtualScreen);
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            SendMouseEvent(
                NativeMethods.MouseEventMove
                | NativeMethods.MouseEventAbsolute
                | NativeMethods.MouseEventVirtualDesk,
                normalized.X,
                normalized.Y);

            Thread.Sleep(40);
            if (NativeMethods.GetCursorPos(out var actual)
                && Math.Abs(actual.X - x) <= 2
                && Math.Abs(actual.Y - y) <= 2)
            {
                return;
            }
        }

        NativeMethods.GetCursorPos(out var finalActual);
        throw new Win32Exception(
            Marshal.GetLastWin32Error(),
            $"鼠标未能移动到目标位置 ({x}, {y})，实际位置为 ({finalActual.X}, {finalActual.Y})。");
    }

    public void LeftClick()
    {
        var sent = SendMouseEvents(
            NativeMethods.MouseEventLeftDown,
            NativeMethods.MouseEventLeftUp);
        if (sent == 2)
        {
            return;
        }

        if (sent == 1)
        {
            if (SendMouseEvent(NativeMethods.MouseEventLeftUp, throwOnFailure: false))
            {
                return;
            }

            throw new IndeterminateClickException(
                "鼠标按下已发送，但抬起状态无法确认；该点击不会自动重试。");
        }

        throw new Win32Exception(
            Marshal.GetLastWin32Error(),
            "发送鼠标点击失败。");
    }

    public void EnsureLeftButtonUp()
    {
        SendMouseEvent(NativeMethods.MouseEventLeftUp, throwOnFailure: false);
    }

    private static bool SendMouseEvent(
        uint flags,
        int dx = 0,
        int dy = 0,
        bool throwOnFailure = true)
    {
        var sent = SendMouseEvents([flags], dx, dy);

        if (sent == 0 && throwOnFailure)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "发送鼠标事件失败。");
        }

        return sent > 0;
    }

    private static uint SendMouseEvents(params uint[] flags)
    {
        return SendMouseEvents(flags, 0, 0);
    }

    private static uint SendMouseEvents(
        IReadOnlyList<uint> flags,
        int dx,
        int dy)
    {
        var inputs = flags.Select(flag =>
            new NativeMethods.Input
            {
                Type = NativeMethods.InputMouse,
                Data = new NativeMethods.InputUnion
                {
                    Mouse = new NativeMethods.MouseInput
                    {
                        Dx = dx,
                        Dy = dy,
                        Flags = flag
                    }
                }
            }).ToArray();

        return NativeMethods.SendInput(
            (uint)inputs.Length,
            inputs,
            Marshal.SizeOf<NativeMethods.Input>());
    }
}
