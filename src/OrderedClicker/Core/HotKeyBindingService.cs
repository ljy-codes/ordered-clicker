using OrderedClicker.Models;

namespace OrderedClicker.Core;

public static class HotKeyBindingService
{
    private const ShortcutModifiers KnownModifiers =
        ShortcutModifiers.Control
        | ShortcutModifiers.Alt
        | ShortcutModifiers.Shift
        | ShortcutModifiers.Windows;

    public static HotKeyBinding DefaultCapture { get; } = new(
        Keys.F6,
        ShortcutModifiers.None);

    public static HotKeyBinding DefaultStartPause { get; } = new(
        Keys.F7,
        ShortcutModifiers.None);

    public static HotKeyBinding DefaultStop { get; } = new(
        Keys.F8,
        ShortcutModifiers.None);

    public static HotKeyBinding CompatibilityCapture { get; } = new(
        Keys.F8,
        ShortcutModifiers.Control | ShortcutModifiers.Alt);

    public static HotKeyBinding CompatibilityStartPause { get; } = new(
        Keys.F9,
        ShortcutModifiers.Control | ShortcutModifiers.Alt);

    public static HotKeyBinding CompatibilityStop { get; } = new(
        Keys.F10,
        ShortcutModifiers.Control | ShortcutModifiers.Alt);

    public static bool Validate(HotKeyBinding? binding, out string error)
    {
        if (binding is null || binding.Key == Keys.None)
        {
            error = "请选择快捷键主键。";
            return false;
        }

        if ((binding.Key & Keys.KeyCode) != binding.Key)
        {
            error = "快捷键主键无效，请重新按下组合键。";
            return false;
        }

        if (binding.Modifiers == ShortcutModifiers.None)
        {
            if (binding.Key is < Keys.F6 or > Keys.F12)
            {
                error = "无修饰键时仅允许使用 F6 到 F12。";
                return false;
            }
        }

        if ((binding.Modifiers & ~KnownModifiers) != 0)
        {
            error = "快捷键包含不支持的修饰键。";
            return false;
        }

        if (binding.Key is Keys.ControlKey
            or Keys.LControlKey
            or Keys.RControlKey
            or Keys.Menu
            or Keys.LMenu
            or Keys.RMenu
            or Keys.ShiftKey
            or Keys.LShiftKey
            or Keys.RShiftKey
            or Keys.LWin
            or Keys.RWin)
        {
            error = "请选择修饰键之外的主键。";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static bool ValidateSet(
        HotKeyBinding? capture,
        HotKeyBinding? startPause,
        HotKeyBinding? stop,
        out string error)
    {
        foreach (var (name, binding) in new[]
                 {
                     ("采点", capture),
                     ("开始/暂停", startPause),
                     ("停止", stop)
                 })
        {
            if (!Validate(binding, out var bindingError))
            {
                error = $"{name}快捷键无效：{bindingError}";
                return false;
            }
        }

        if (capture == startPause || capture == stop || startPause == stop)
        {
            error = "采点、开始/暂停和停止快捷键不能重复。";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static string Format(HotKeyBinding binding)
    {
        var parts = new List<string>(5);
        if (binding.Modifiers.HasFlag(ShortcutModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (binding.Modifiers.HasFlag(ShortcutModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (binding.Modifiers.HasFlag(ShortcutModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (binding.Modifiers.HasFlag(ShortcutModifiers.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(binding.Key.ToString());
        return string.Join("+", parts);
    }
}
