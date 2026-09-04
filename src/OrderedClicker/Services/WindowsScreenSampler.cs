using System.ComponentModel;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using OrderedClicker.Models;
using OrderedClicker.Native;

namespace OrderedClicker.Services;

public sealed class WindowsScreenSampler : IScreenSampler, IDisposable
{
    private const int MaximumWidth = 160;
    private const int MaximumHeight = 90;
    private readonly object _sync = new();
    private Bitmap? _buffer;

    public Task<byte[]> SampleAsync(
        ScreenBounds region,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (region.Width <= 0 || region.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(region));
        }

        lock (_sync)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var size = CalculateSampleSize(region);
            EnsureBuffer(size.Width, size.Height);
            CaptureScaled(region, _buffer!);
            return Task.FromResult(CopyRgbBytes(_buffer!));
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _buffer?.Dispose();
            _buffer = null;
        }
    }

    private static Size CalculateSampleSize(ScreenBounds region)
    {
        var scale = Math.Min(
            1d,
            Math.Min(
                (double)MaximumWidth / region.Width,
                (double)MaximumHeight / region.Height));
        return new Size(
            Math.Max(1, (int)Math.Round(region.Width * scale)),
            Math.Max(1, (int)Math.Round(region.Height * scale)));
    }

    private void EnsureBuffer(int width, int height)
    {
        if (_buffer?.Width == width && _buffer.Height == height)
        {
            return;
        }

        _buffer?.Dispose();
        _buffer = new Bitmap(width, height, PixelFormat.Format24bppRgb);
    }

    private static void CaptureScaled(ScreenBounds region, Bitmap target)
    {
        using var graphics = Graphics.FromImage(target);
        var destinationDc = graphics.GetHdc();
        var sourceDc = NativeMethods.GetDC(IntPtr.Zero);
        try
        {
            NativeMethods.SetStretchBltMode(
                destinationDc,
                NativeMethods.StretchModeHalftone);
            if (!NativeMethods.StretchBlt(
                    destinationDc,
                    0,
                    0,
                    target.Width,
                    target.Height,
                    sourceDc,
                    region.X,
                    region.Y,
                    region.Width,
                    region.Height,
                    NativeMethods.RasterOperationSourceCopy))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "采集画面稳定检测区域失败。");
            }
        }
        finally
        {
            if (sourceDc != IntPtr.Zero)
            {
                NativeMethods.ReleaseDC(IntPtr.Zero, sourceDc);
            }

            graphics.ReleaseHdc(destinationDc);
        }
    }

    private static byte[] CopyRgbBytes(Bitmap bitmap)
    {
        var rectangle = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(
            rectangle,
            ImageLockMode.ReadOnly,
            PixelFormat.Format24bppRgb);
        try
        {
            var source = new byte[Math.Abs(data.Stride) * bitmap.Height];
            Marshal.Copy(data.Scan0, source, 0, source.Length);
            var rgb = new byte[bitmap.Width * bitmap.Height * 3];
            for (var y = 0; y < bitmap.Height; y++)
            {
                Buffer.BlockCopy(
                    source,
                    y * Math.Abs(data.Stride),
                    rgb,
                    y * bitmap.Width * 3,
                    bitmap.Width * 3);
            }

            return rgb;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
