using System.ComponentModel;
using OrderedClicker.Core;
using OrderedClicker.Models;
using OrderedClicker.Native;

namespace OrderedClicker.Forms;

internal sealed class HotKeyInput : TextBox
{
    private HotKeyBinding _binding;

    public HotKeyInput(string name, HotKeyBinding binding)
    {
        Name = name;
        ReadOnly = true;
        ShortcutsEnabled = false;
        Width = 220;
        _binding = binding;
        UpdateText();
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public HotKeyBinding Binding
    {
        get => _binding;
        set
        {
            _binding = value;
            UpdateText();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var modifiers = ShortcutModifiers.None;
        if (e.Control)
        {
            modifiers |= ShortcutModifiers.Control;
        }

        if (e.Alt)
        {
            modifiers |= ShortcutModifiers.Alt;
        }

        if (e.Shift)
        {
            modifiers |= ShortcutModifiers.Shift;
        }

        if (IsKeyPressed(Keys.LWin) || IsKeyPressed(Keys.RWin))
        {
            modifiers |= ShortcutModifiers.Windows;
        }

        if (e.KeyCode == Keys.Escape && modifiers == ShortcutModifiers.None)
        {
            base.OnKeyDown(e);
            return;
        }

        if (e.KeyCode is not (
                Keys.ControlKey
                or Keys.LControlKey
                or Keys.RControlKey
                or Keys.Menu
                or Keys.LMenu
                or Keys.RMenu
                or Keys.ShiftKey
                or Keys.LShiftKey
                or Keys.RShiftKey
                or Keys.LWin
                or Keys.RWin))
        {
            Binding = new HotKeyBinding(e.KeyCode, modifiers);
        }

        e.Handled = true;
        e.SuppressKeyPress = true;
        base.OnKeyDown(e);
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        e.Handled = true;
        base.OnKeyPress(e);
    }

    private static bool IsKeyPressed(Keys key)
    {
        return (NativeMethods.GetKeyState((int)key) & 0x8000) != 0;
    }

    private void UpdateText()
    {
        Text = HotKeyBindingService.Format(_binding);
        SelectionStart = TextLength;
    }
}
