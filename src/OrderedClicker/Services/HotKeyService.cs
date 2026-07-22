using System.ComponentModel;
using System.Runtime.InteropServices;
using OrderedClicker.Models;
using OrderedClicker.Native;

namespace OrderedClicker.Services;

[Flags]
public enum HotKeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Windows = 0x0008,
    NoRepeat = 0x4000
}

public sealed class HotKeyService : IHotKeyRegistrar
{
    private readonly IntPtr _windowHandle;
    private readonly HashSet<int> _registeredIds = [];
    private bool _disposed;

    public HotKeyService(IntPtr windowHandle)
    {
        _windowHandle = windowHandle;
    }

    public void Register(int id, Keys key, HotKeyModifiers modifiers = HotKeyModifiers.NoRepeat)
    {
        ThrowIfDisposed();
        if (!NativeMethods.RegisterHotKey(_windowHandle, id, (uint)modifiers, (uint)key))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                $"注册全局热键 {key} 失败，热键可能已被其他程序占用。");
        }

        _registeredIds.Add(id);
    }

    public void Register(HotKeyRegistration registration)
    {
        var modifiers = HotKeyModifiers.NoRepeat;
        if (registration.Binding.Modifiers.HasFlag(ShortcutModifiers.Control))
        {
            modifiers |= HotKeyModifiers.Control;
        }

        if (registration.Binding.Modifiers.HasFlag(ShortcutModifiers.Alt))
        {
            modifiers |= HotKeyModifiers.Alt;
        }

        if (registration.Binding.Modifiers.HasFlag(ShortcutModifiers.Shift))
        {
            modifiers |= HotKeyModifiers.Shift;
        }

        if (registration.Binding.Modifiers.HasFlag(ShortcutModifiers.Windows))
        {
            modifiers |= HotKeyModifiers.Windows;
        }

        Register(registration.Id, registration.Binding.Key, modifiers);
    }

    public void Unregister(int id)
    {
        ThrowIfDisposed();
        if (!_registeredIds.Contains(id))
        {
            return;
        }

        if (!NativeMethods.UnregisterHotKey(_windowHandle, id))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                $"注销全局热键 {id} 失败。");
        }

        _registeredIds.Remove(id);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var id in _registeredIds.ToList())
        {
            if (NativeMethods.UnregisterHotKey(_windowHandle, id))
            {
                _registeredIds.Remove(id);
            }
        }

        _registeredIds.Clear();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
