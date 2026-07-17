using System.ComponentModel;
using System.Runtime.InteropServices;
using OrderedClicker.Native;

namespace OrderedClicker.Services;

public sealed class WindowsMouseController : IMouseController
{
    public void MoveTo(int x, int y)
    {
        if (!NativeMethods.SetCursorPos(x, y))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "移动鼠标失败。");
        }
    }

    public void LeftClick()
    {
        SendMouseEvent(NativeMethods.MouseEventLeftDown);
        SendMouseEvent(NativeMethods.MouseEventLeftUp);
    }

    public void EnsureLeftButtonUp()
    {
        SendMouseEvent(NativeMethods.MouseEventLeftUp, throwOnFailure: false);
    }

    private static void SendMouseEvent(uint flags, bool throwOnFailure = true)
    {
        var inputs = new[]
        {
            new NativeMethods.Input
            {
                Type = NativeMethods.InputMouse,
                Data = new NativeMethods.InputUnion
                {
                    Mouse = new NativeMethods.MouseInput
                    {
                        Flags = flags
                    }
                }
            }
        };

        var sent = NativeMethods.SendInput(
            (uint)inputs.Length,
            inputs,
            Marshal.SizeOf<NativeMethods.Input>());

        if (sent == 0 && throwOnFailure)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "发送鼠标点击失败。");
        }
    }
}
