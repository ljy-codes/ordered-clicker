using OrderedClicker.Theming;

namespace OrderedClicker.Forms;

internal sealed class RunStatusForm : Form
{
    private readonly Label _stateLabel = new();
    private readonly Label _progressLabel = new();
    private readonly Button _pauseButton;

    public RunStatusForm(AppTheme theme)
    {
        Name = "RunStatusForm";
        Text = "有序连点器运行状态";
        TopMost = true;
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(410, 142);
        BackColor = theme.Window;
        ForeColor = theme.Text;

        _stateLabel.Name = "RunStatusStateLabel";
        _stateLabel.Dock = DockStyle.Top;
        _stateLabel.Height = 32;
        _stateLabel.Padding = new Padding(12, 9, 12, 0);
        _stateLabel.Font = new Font("Segoe UI Semibold", 10F);
        _stateLabel.Text = "准备执行";

        _progressLabel.Name = "RunStatusProgressLabel";
        _progressLabel.Dock = DockStyle.Top;
        _progressLabel.Height = 50;
        _progressLabel.Padding = new Padding(12, 5, 12, 0);
        _progressLabel.ForeColor = theme.MutedText;
        _progressLabel.Text = "等待倒计时";

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(8, 8, 8, 0),
            BackColor = theme.Window
        };
        var stopButton = CreateButton(
            "RunStatusStopButton",
            "停止",
            theme.DangerAccent,
            Color.White);
        stopButton.Click += (_, _) => StopRequested?.Invoke();
        _pauseButton = CreateButton(
            "RunStatusPauseButton",
            "暂停",
            theme.Surface,
            theme.Text);
        _pauseButton.Click += (_, _) => PauseRequested?.Invoke();
        actions.Controls.Add(stopButton);
        actions.Controls.Add(_pauseButton);

        Controls.Add(actions);
        Controls.Add(_progressLabel);
        Controls.Add(_stateLabel);
    }

    public event Action? PauseRequested;

    public event Action? StopRequested;

    public void UpdateState(string state, bool paused)
    {
        _stateLabel.Text = state;
        _pauseButton.Text = paused ? "继续" : "暂停";
        _pauseButton.Enabled = state is "执行中" or "已暂停";
    }

    public void UpdateProgress(string message)
    {
        _progressLabel.Text = message;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(area.Right - Width - 20, area.Bottom - Height - 20);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnFormClosing(e);
    }

    private static Button CreateButton(
        string name,
        string text,
        Color background,
        Color foreground)
    {
        return new Button
        {
            Name = name,
            Text = text,
            Width = 92,
            Height = 34,
            Margin = new Padding(8, 0, 0, 0),
            BackColor = background,
            ForeColor = foreground,
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false
        };
    }
}
