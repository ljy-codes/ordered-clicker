param(
    [Parameter(Mandatory = $true)]
    [int]$ProcessId,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class WindowCaptureNative
{
    public delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr window, out Rect rect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr window, StringBuilder text, int maximumCount);

    public static IntPtr FindVisibleWindow(int processId)
    {
        IntPtr result = IntPtr.Zero;
        EnumWindows((window, parameter) =>
        {
            GetWindowThreadProcessId(window, out uint ownerProcessId);
            if (ownerProcessId == processId && IsWindowVisible(window))
            {
                result = window;
                return false;
            }

            return true;
        }, IntPtr.Zero);
        return result;
    }

    public static string DescribeWindows(int processId)
    {
        var windows = new List<string>();
        EnumWindows((window, parameter) =>
        {
            GetWindowThreadProcessId(window, out uint ownerProcessId);
            if (ownerProcessId == processId)
            {
                var title = new StringBuilder(512);
                GetWindowText(window, title, title.Capacity);
                GetWindowRect(window, out Rect rect);
                windows.Add(
                    $"Handle={window}; Visible={IsWindowVisible(window)}; " +
                    $"Title={title}; Rect={rect.Left},{rect.Top},{rect.Right},{rect.Bottom}");
            }

            return true;
        }, IntPtr.Zero);
        return windows.Count == 0 ? "没有顶层窗口句柄。" : string.Join(Environment.NewLine, windows);
    }
}
"@

$window = [WindowCaptureNative]::FindVisibleWindow($ProcessId)
if ($window -eq [IntPtr]::Zero) {
    $details = [WindowCaptureNative]::DescribeWindows($ProcessId)
    throw "进程 $ProcessId 没有可见顶层窗口。`n$details"
}

$rect = New-Object WindowCaptureNative+Rect
if (-not [WindowCaptureNative]::GetWindowRect($window, [ref]$rect)) {
    throw "无法读取窗口范围。"
}

$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
if ($width -le 0 -or $height -le 0) {
    throw "窗口范围无效：${width}x${height}"
}

$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $resolvedOutput
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null

$bitmap = New-Object System.Drawing.Bitmap $width, $height
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
try {
    $graphics.CopyFromScreen(
        (New-Object System.Drawing.Point $rect.Left, $rect.Top),
        [System.Drawing.Point]::Empty,
        (New-Object System.Drawing.Size $width, $height))
    $bitmap.Save($resolvedOutput, [System.Drawing.Imaging.ImageFormat]::Png)
}
finally {
    $graphics.Dispose()
    $bitmap.Dispose()
}

Write-Host "窗口截图：$resolvedOutput (${width}x${height})"
