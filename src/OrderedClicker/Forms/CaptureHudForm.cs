using OrderedClicker.Theming;

namespace OrderedClicker.Forms;

internal sealed class CaptureHudForm : Form
{
    private readonly Label _message = new();
    private readonly Label _count = new();

    public CaptureHudForm(AppTheme theme)
    {
        Name = "CaptureHudForm";
        Text = "采点";
        TopMost = true;
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(360, 118);
        BackColor = theme.Window;
        ForeColor = theme.Text;
        _message.Dock = DockStyle.Top;
        _message.Height = 40;
        _message.Padding = new Padding(10, 8, 10, 0);
        _count.Dock = DockStyle.Top;
        _count.Height = 24;
        _count.Padding = new Padding(10, 0, 10, 0);
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8, 7, 8, 0),
            BackColor = theme.Window
        };
        var finish = CreateButton("完成", theme.Surface, theme.Text);
        finish.Click += (_, _) => FinishRequested?.Invoke();
        var record = CreateButton("记录当前位置", theme.Primary, Color.White);
        record.Width = 118;
        record.Click += (_, _) => RecordRequested?.Invoke();
        actions.Controls.Add(finish);
        actions.Controls.Add(record);
        Controls.Add(actions);
        Controls.Add(_count);
        Controls.Add(_message);
    }

    public event Action? RecordRequested;
    public event Action? FinishRequested;

    public void UpdateStatus(string message, int count)
    {
        _message.Text = message;
        _count.Text = $"已记录 {count} 个点位";
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(area.Right - Width - 20, area.Top + 20);
    }

    private static Button CreateButton(string text, Color background, Color foreground)
    {
        return new Button
        {
            Text = text,
            Width = 82,
            Height = 32,
            BackColor = background,
            ForeColor = foreground,
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false
        };
    }
}
