using OrderedClicker.Native;
using OrderedClicker.Theming;

namespace OrderedClicker.Forms;

public sealed partial class MainForm
{
    private void ShowUsageHelp()
    {
        using var dialog = new UsageHelpDialog(_theme);
        dialog.ShowDialog(this);
    }

    private sealed class UsageHelpDialog : Form
    {
        private const string HelpText =
            """
            一、配置执行方案
            设置方案名称、总循环次数和轮间等待时间。总循环次数表示全部启用点位按顺序执行多少轮；轮间等待只发生在两轮之间。

            二、采集点击位置
            点击“采点模式”后，将鼠标移动到目标位置并按 F8。每按一次 F8 会记录一个点位，点位按采集顺序执行。采点完成后再次点击“结束采点”。

            三、调整点位顺序
            选中表格中的点位，使用“上移”或“下移”调整执行顺序。“启用”未勾选的点位会被跳过，但不会从方案中删除。

            四、设置每个点的动作
            点击次数：当前点位连续点击多少次。
            点击间隔：同一点位多次点击之间的等待时间，单位为毫秒。
            点后等待：当前点位完成全部点击后，进入下一个点位前的等待时间。

            五、开始、暂停和停止
            F9：开始任务；运行中按 F9 暂停，再按一次继续。
            F10：随时停止倒计时、等待或点击任务。
            开始前有 3 秒倒计时，可利用这段时间切换到目标窗口。

            六、保存与加载
            “保存”会将当前方案写入 JSON 文件，包括点位顺序、点击参数和显示器信息。“加载”可恢复之前保存的方案。

            七、显示器与屏幕缩放
            程序使用 Per-Monitor V2 DPI 和屏幕物理坐标处理点击位置。如果显示器分辨率、排列位置、主副屏关系或缩放比例发生变化，原坐标可能不再准确。出现相关提示时，请在当前显示环境下重新采点。

            八、使用建议
            先用较少循环次数验证点位和等待时间，确认无误后再提高循环次数。执行过程中可随时按 F10 停止。
            """;

        public UsageHelpDialog(AppTheme theme)
        {
            Name = "UsageHelpDialog";
            Text = "使用说明";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(700, 590);
            MinimumSize = new Size(600, 500);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9F);
            BackColor = theme.Window;
            ForeColor = theme.Text;
            KeyPreview = true;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(20),
                BackColor = theme.Window
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            root.Controls.Add(CreateHeader(theme), 0, 0);
            root.Controls.Add(CreateBody(theme), 0, 1);
            root.Controls.Add(CreateFooter(theme), 0, 2);
            Controls.Add(root);
            ApplyTitleBarTheme(theme);
        }

        private static Control CreateHeader(AppTheme theme)
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = theme.Window
            };
            panel.Controls.Add(new Label
            {
                AutoSize = true,
                Location = new Point(0, 2),
                Font = new Font("Segoe UI Semibold", 16F),
                ForeColor = theme.Text,
                BackColor = theme.Window,
                Text = "有序连点器快速上手"
            });
            panel.Controls.Add(new Label
            {
                AutoSize = true,
                Location = new Point(1, 39),
                ForeColor = theme.MutedText,
                BackColor = theme.Window,
                Text = "按顺序配置点位，为每个点设置独立点击参数，再按循环执行。"
            });

            var shortcuts = new FlowLayoutPanel
            {
                AutoSize = true,
                Location = new Point(0, 64),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = theme.Window
            };
            shortcuts.Controls.Add(CreateShortcutLabel("F8 采点", theme.CaptureAccent, theme));
            shortcuts.Controls.Add(CreateShortcutLabel("F9 开始 / 暂停", theme.Primary, theme));
            shortcuts.Controls.Add(CreateShortcutLabel("F10 停止", theme.DangerAccent, theme));
            panel.Controls.Add(shortcuts);
            return panel;
        }

        private static Label CreateShortcutLabel(string text, Color accent, AppTheme theme)
        {
            return new Label
            {
                AutoSize = true,
                Text = text,
                Padding = new Padding(8, 3, 8, 3),
                Margin = new Padding(0, 0, 8, 0),
                BackColor = theme.AccentSurface(accent),
                ForeColor = theme.AccentText(accent)
            };
        }

        private static Control CreateBody(AppTheme theme)
        {
            var frame = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(1),
                BackColor = theme.Border
            };
            var body = new RichTextBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                ReadOnly = true,
                DetectUrls = false,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                BackColor = theme.Surface,
                ForeColor = theme.Text,
                Font = new Font("Segoe UI", 10F),
                Text = HelpText
            };
            body.HandleCreated += (_, _) =>
                NativeMethods.SetWindowTheme(
                    body.Handle,
                    theme.IsDark ? "DarkMode_Explorer" : "Explorer",
                    null);
            body.SelectAll();
            body.SelectionColor = theme.Text;
            body.SelectionBackColor = theme.Surface;
            using var headingFont = new Font(body.Font, FontStyle.Bold);
            foreach (var heading in new[]
                     {
                         "一、配置执行方案",
                         "二、采集点击位置",
                         "三、调整点位顺序",
                         "四、设置每个点的动作",
                         "五、开始、暂停和停止",
                         "六、保存与加载",
                         "七、显示器与屏幕缩放",
                         "八、使用建议"
                     })
            {
                var start = body.Text.IndexOf(heading, StringComparison.Ordinal);
                if (start < 0)
                {
                    continue;
                }

                body.Select(start, heading.Length);
                body.SelectionColor = theme.HelpAccent;
                body.SelectionFont = headingFont;
            }

            body.SelectionLength = 0;
            body.SelectionStart = 0;
            frame.Controls.Add(body);
            return frame;
        }

        private Control CreateFooter(AppTheme theme)
        {
            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(0, 13, 0, 0),
                BackColor = theme.Window
            };
            var closeButton = new Button
            {
                Text = "关闭",
                Width = 104,
                Height = 34,
                DialogResult = DialogResult.Cancel,
                FlatStyle = FlatStyle.Flat,
                UseVisualStyleBackColor = false,
                Cursor = Cursors.Hand,
                BackColor = theme.AccentSurface(theme.HelpAccent),
                ForeColor = theme.AccentText(theme.HelpAccent)
            };
            closeButton.FlatAppearance.BorderColor = theme.HelpAccent;
            closeButton.FlatAppearance.MouseOverBackColor = theme.AccentHover(theme.HelpAccent);
            closeButton.FlatAppearance.MouseDownBackColor =
                theme.AccentPressed(theme.HelpAccent);
            panel.Controls.Add(closeButton);
            CancelButton = closeButton;
            return panel;
        }

        private void ApplyTitleBarTheme(AppTheme theme)
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
            {
                return;
            }

            var enabled = theme.IsDark ? 1 : 0;
            var result = NativeMethods.DwmSetWindowAttribute(
                Handle,
                NativeMethods.DwmwaUseImmersiveDarkMode,
                ref enabled,
                sizeof(int));
            if (result != 0)
            {
                NativeMethods.DwmSetWindowAttribute(
                    Handle,
                    NativeMethods.DwmwaUseImmersiveDarkModeBefore20H1,
                    ref enabled,
                    sizeof(int));
            }
        }
    }
}
