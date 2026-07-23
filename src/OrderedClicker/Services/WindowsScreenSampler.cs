using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using OrderedClicker.Models;

namespace OrderedClicker.Services;

public sealed class WindowsScreenSampler : IScreenSampler
{
    private const int MaximumWidth = 160;
    private const int MaximumHeight = 90;

    public Task<byte[]> SampleAsync(
        ScreenBounds region,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (region.Width <= 0 || region.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(region));
        }

        using var source = new Bitmap(
            region.Width,
            region.Height,
            PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(source))
        {
            graphics.CopyFromScreen(
                region.X,
                region.Y,
                0,
                0,
                source.Size,
                CopyPixelOperation.SourceCopy);
        }

        var scale = Math.Min(
            1d,
            Math.Min(
                (double)MaximumWidth / region.Width,
                (double)MaximumHeight / region.Height));
        var width = Math.Max(1, (int)Math.Round(region.Width * scale));
        var height = Math.Max(1, (int)Math.Round(region.Height * scale));

        using var reduced = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(reduced))
        {
            graphics.InterpolationMode = InterpolationMode.Low;
            graphics.PixelOffsetMode = PixelOffsetMode.HighSpeed;
            graphics.DrawImage(source, new Rectangle(0, 0, width, height));
        }

        return Task.FromResult(ToGrayscaleBytes(reduced));
    }

    private static byte[] ToGrayscaleBytes(Bitmap bitmap)
    {
        var rectangle = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(
            rectangle,
            ImageLockMode.ReadOnly,
            PixelFormat.Format24bppRgb);

        try
        {
            var rowBytes = bitmap.Width * 3;
            var buffer = new byte[Math.Abs(data.Stride) * bitmap.Height];
            Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);
            var grayscale = new byte[bitmap.Width * bitmap.Height];

            for (var y = 0; y < bitmap.Height; y++)
            {
                var sourceRow = y * Math.Abs(data.Stride);
                var targetRow = y * bitmap.Width;
                for (var x = 0; x < bitmap.Width; x++)
                {
                    var offset = sourceRow + x * 3;
                    var blue = buffer[offset];
                    var green = buffer[offset + 1];
                    var red = buffer[offset + 2];
                    grayscale[targetRow + x] = (byte)(
                        (red * 30 + green * 59 + blue * 11) / 100);
                }
            }

            return grayscale;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
