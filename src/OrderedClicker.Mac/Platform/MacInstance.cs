using System.ComponentModel;
using System.Runtime.InteropServices;
namespace OrderedClicker.Mac.Platform;

public sealed class MacInstance : IDisposable
{
    private readonly int _fd;
    [DllImport("libc", SetLastError=true)] private static extern int open(string path,int flags,int mode);
    [DllImport("libc", SetLastError=true)] private static extern int flock(int fd,int operation);
    [DllImport("libc")] private static extern int close(int fd);
    public bool IsOwner { get; }
    public MacInstance(string root)
    {
        Directory.CreateDirectory(root);
        _fd=open(Path.Combine(root,".instance.lock"),2|0x200|0x1000000,0x180); // Darwin O_RDWR | O_CREAT, 0600.
        if(_fd<0)throw new Win32Exception(Marshal.GetLastWin32Error(),"无法创建单实例锁。");
        IsOwner=flock(_fd,2|4)==0;
        if(!IsOwner&&Marshal.GetLastWin32Error()!=35){var error=Marshal.GetLastWin32Error();close(_fd);throw new Win32Exception(error,"无法获取单实例锁。");}
    }
    public void Dispose(){if(IsOwner)flock(_fd,8);close(_fd);}
}
