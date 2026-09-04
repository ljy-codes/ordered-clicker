using OrderedClicker.Models;
using OrderedClicker.Theming;

namespace OrderedClicker.Forms;

internal sealed class CloudRegionOverlayForm : Form
{
    private readonly Color _borderColor;

    public CloudRegionOverlayForm(CloudDesktopRegion region, AppTheme theme)
    {
        Name = "CloudRegionOverlayForm";
        Bounds = new Rectangle(region.X, region.Y, region.Width, region.Height);
        StartPosition = FormStartPosition.Manual;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;
        _borderColor = theme.CaptureAccent;
    }

    protected override bool ShowWithoutActivation => true;

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(_borderColor, 4);
        e.Graphics.DrawRectangle(pen, 2, 2, Width - 5, Height - 5);
    }
}
