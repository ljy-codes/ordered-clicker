using OrderedClicker.Models;
using OrderedClicker.Theming;

namespace OrderedClicker.Forms;

public sealed class ExecutionPlanDialog : Form
{
    public ExecutionPlanDialog(ExecutionPlan plan, AppTheme theme)
    {
        Name = "ExecutionPlanDialog";
        Text = "确认执行计划";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(520, 390);
        MinimizeBox = false;
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9F);
        BackColor = theme.Window;
        ForeColor = theme.Text;

        var disabledRows = plan.DisabledSourceRowNumbers.Count == 0
            ? "无"
            : string.Join("、", plan.DisabledSourceRowNumbers);
        var body = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = theme.Input,
            ForeColor = theme.Text,
            Font = new Font("Segoe UI", 10F),
            Text =
                $"总行数：{plan.TotalSourceRows}{Environment.NewLine}"
                + $"启用行：{plan.Points.Count}{Environment.NewLine}"
                + $"禁用行：{disabledRows}{Environment.NewLine}"
                + $"总循环：{plan.TotalLoops}{Environment.NewLine}"
                + $"计划点次：{plan.PlannedPointExecutionCount}{Environment.NewLine}"
                + $"计划点击：{plan.PlannedClickCount}{Environment.NewLine}"
                + $"预计耗时：{FormatDuration(plan.EstimatedBaseDuration)}"
                + $" 至 {FormatDuration(plan.EstimatedMaximumDuration)}"
                + $"{Environment.NewLine}"
                + $"坐标范围：{plan.StabilityRegion.Width} × {plan.StabilityRegion.Height}"
                + $"{Environment.NewLine}"
                + $"画面稳定等待：{(plan.ScreenStability.Enabled ? "开启" : "关闭")}"
                + $"{Environment.NewLine}{Environment.NewLine}"
                + "请确认启用行数量和计划点击数正确。只有实际完成数与计划完全一致，"
                + "程序才会显示全部完成。"
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 58,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 12, 12, 0),
            BackColor = theme.Window
        };
        var start = CreateButton("开始执行", theme.Primary, Color.White);
        start.DialogResult = DialogResult.OK;
        var cancel = CreateButton("返回检查", theme.Surface, theme.Text);
        cancel.DialogResult = DialogResult.Cancel;
        buttons.Controls.Add(start);
        buttons.Controls.Add(cancel);

        Controls.Add(body);
        Controls.Add(buttons);
        AcceptButton = start;
        CancelButton = cancel;
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours}小时 {duration.Minutes}分 {duration.Seconds}秒";
        }

        if (duration.TotalMinutes >= 1)
        {
            return $"{duration.Minutes}分 {duration.Seconds}秒";
        }

        return $"{Math.Max(1, (int)Math.Ceiling(duration.TotalSeconds))}秒";
    }

    private static Button CreateButton(string text, Color background, Color foreground)
    {
        return new Button
        {
            Text = text,
            Width = 104,
            Height = 34,
            Margin = new Padding(8, 0, 0, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = background,
            ForeColor = foreground,
            UseVisualStyleBackColor = false
        };
    }
}
