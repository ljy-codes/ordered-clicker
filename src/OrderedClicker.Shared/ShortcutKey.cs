namespace OrderedClicker.Platform;

// Values preserve the Windows settings serialization without referencing WinForms.
public enum ShortcutKey
{
    None = 0, Back = 8, Tab = 9, Enter = 13,
    ShiftKey = 16, ControlKey = 17, Menu = 18, Escape = 27, Space = 32,
    PageUp = 33, PageDown = 34, End = 35, Home = 36,
    Left = 37, Up = 38, Right = 39, Down = 40, Insert = 45, Delete = 46,
    D0 = 48, D1, D2, D3, D4, D5, D6, D7, D8, D9,
    A = 65, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
    LWin = 91, RWin = 92,
    F1 = 112, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
    F13, F14, F15, F16, F17, F18, F19, F20,
    LShiftKey = 160, RShiftKey, LControlKey, RControlKey, LMenu, RMenu,
    KeyCode = 65535, Shift = 65536, Control = 131072, Alt = 262144
}
