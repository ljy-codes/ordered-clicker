namespace OrderedClicker.Models;

[Flags]
public enum ShortcutModifiers
{
    None = 0,
    Control = 1,
    Alt = 2,
    Shift = 4,
    Windows = 8
}

public sealed record HotKeyBinding(Keys Key, ShortcutModifiers Modifiers);
