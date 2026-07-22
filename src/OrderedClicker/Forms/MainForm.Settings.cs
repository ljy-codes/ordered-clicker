using System.Drawing.Drawing2D;
using System.ComponentModel;
using OrderedClicker.Core;
using OrderedClicker.Models;
using OrderedClicker.Native;
using OrderedClicker.Theming;

namespace OrderedClicker.Forms;

public sealed partial class MainForm
{
    private void ShowThemeSettings()
    {
        var originalSettings = CloneSettings(_settings);
        var originalTheme = _theme;
        using var dialog = new ThemeSettingsDialog(_settings, _theme);
        dialog.ThemePreviewed += ApplyTheme;
        DialogResult dialogResult;
        _suspendHotKeyActions = true;
        try
        {
            dialogResult = dialog.ShowDialog(this);
        }
        finally
        {
            _suspendHotKeyActions = false;
        }

        if (dialogResult != DialogResult.OK)
        {
            ApplyTheme(originalTheme);
            return;
        }

        var selectedSettings = new AppSettings
        {
            Theme = dialog.SelectedThemeId,
            CaptureHotKey = dialog.CaptureHotKey,
            StartPauseHotKey = dialog.StartPauseHotKey,
            StopHotKey = dialog.StopHotKey
        };
        var registrationChanged = false;
        try
        {
            var hotKeysChanged = HotKeysChanged(originalSettings, selectedSettings);
            if (hotKeysChanged && _enableGlobalHotKeys && _hotKeyCoordinator is not null)
            {
                var result = _hotKeyCoordinator.Replace(
                    CreateHotKeyRegistrations(selectedSettings));
                RefreshActiveHotKeyRegistrations();
                UpdateHotKeyText();
                if (!result.Success)
                {
                    ApplyTheme(originalTheme);
                    var recoveryMessage = DescribeRegistrationRecovery(originalSettings);
                    ShowError(
                        $"快捷键保存失败：{result.Message}{Environment.NewLine}"
                        + recoveryMessage);
                    return;
                }

                registrationChanged = true;
            }

            _settingsService.Save(selectedSettings);
            _settings = selectedSettings;
            var selectedTheme = AppThemeCatalog.Get(selectedSettings.Theme);
            ApplyTheme(selectedTheme);
            UpdateHotKeyText();
            SetStatus($"设置已保存：{selectedTheme.DisplayName}，快捷键已更新。");
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException)
        {
            if (registrationChanged && _hotKeyCoordinator is not null)
            {
                var rollback = _hotKeyCoordinator.Replace(
                    CreateHotKeyRegistrations(originalSettings));
                RefreshActiveHotKeyRegistrations();
                if (!rollback.Success)
                {
                    ShowError(
                        $"保存设置失败：{exception.Message}{Environment.NewLine}"
                        + $"恢复快捷键时发生错误：{rollback.Message}{Environment.NewLine}"
                        + DescribeRegistrationRecovery(originalSettings));
                    _settings = originalSettings;
                    ApplyTheme(originalTheme);
                    UpdateHotKeyText();
                    return;
                }
            }

            _settings = originalSettings;
            ApplyTheme(originalTheme);
            UpdateHotKeyText();
            ShowError($"保存设置失败：{exception.Message}");
        }
    }

    private string DescribeRegistrationRecovery(AppSettings expectedSettings)
    {
        if (_hotKeyCoordinator is null)
        {
            return "界面按钮仍可正常使用。";
        }

        RefreshActiveHotKeyRegistrations();
        UpdateHotKeyText();
        var expected = CreateHotKeyRegistrations(expectedSettings);
        var active = _hotKeyCoordinator.RegisteredBindings;
        if (expected.Count == active.Count
            && expected.All(active.Contains))
        {
            return "原快捷键已恢复，请更换组合键后重试。";
        }

        var unavailable = expected
            .Where(item => !active.Contains(item))
            .Select(item => $"{item.Name}（{HotKeyBindingService.Format(item.Binding)}）");
        SetStatus("部分快捷键不可用，请使用界面按钮并重新设置。");
        return "部分原快捷键恢复失败："
            + string.Join("、", unavailable)
            + "。界面按钮仍可正常使用，请关闭占用软件后重新设置或重启程序。";
    }

    private static bool HotKeysChanged(AppSettings original, AppSettings selected)
    {
        return original.CaptureHotKey != selected.CaptureHotKey
            || original.StartPauseHotKey != selected.StartPauseHotKey
            || original.StopHotKey != selected.StopHotKey;
    }

    private static AppSettings CloneSettings(AppSettings settings)
    {
        return new AppSettings
        {
            Theme = settings.Theme,
            CaptureHotKey = settings.CaptureHotKey,
            StartPauseHotKey = settings.StartPauseHotKey,
            StopHotKey = settings.StopHotKey
        };
    }

    private sealed class ThemeSettingsDialog : Form
    {
        private readonly List<ThemeCard> _cards = [];
        private readonly TableLayoutPanel _root = new();
        private readonly Label _titleLabel = new();
        private readonly Label _subtitleLabel = new();
        private readonly FlowLayoutPanel _cardsPanel = new();
        private readonly TableLayoutPanel _hotKeyPanel = new();
        private readonly Label _hotKeyTitleLabel = new();
        private readonly Label _hotKeySubtitleLabel = new();
        private readonly HotKeyInput _captureHotKeyInput;
        private readonly HotKeyInput _startPauseHotKeyInput;
        private readonly HotKeyInput _stopHotKeyInput;
        private readonly Button _restoreDefaultHotKeysButton = new();
        private readonly FlowLayoutPanel _footerPanel = new();
        private readonly Button _saveButton = new();
        private readonly Button _cancelButton = new();

        public ThemeSettingsDialog(AppSettings settings, AppTheme currentTheme)
        {
            SelectedThemeId = settings.Theme;
            _captureHotKeyInput = new HotKeyInput(
                "CaptureHotKeyInput",
                settings.CaptureHotKey);
            _startPauseHotKeyInput = new HotKeyInput(
                "StartPauseHotKeyInput",
                settings.StartPauseHotKey);
            _stopHotKeyInput = new HotKeyInput(
                "StopHotKeyInput",
                settings.StopHotKey);
            Name = "ThemeSettingsDialog";
            Text = "设置";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(700, 680);
            MinimumSize = new Size(680, 640);
            MaximizeBox = false;
            MinimizeBox = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9F);
            BackColor = currentTheme.Window;
            ForeColor = currentTheme.Text;
            KeyPreview = true;

            ConfigureLayout();
            ApplyDialogTheme(currentTheme);
        }

        public event Action<AppTheme>? ThemePreviewed;

        public AppThemeId SelectedThemeId { get; private set; }

        public HotKeyBinding CaptureHotKey => _captureHotKeyInput.Binding;

        public HotKeyBinding StartPauseHotKey => _startPauseHotKeyInput.Binding;

        public HotKeyBinding StopHotKey => _stopHotKeyInput.Binding;

        private void ConfigureLayout()
        {
            _root.Dock = DockStyle.Fill;
            _root.ColumnCount = 1;
            _root.RowCount = 4;
            _root.Padding = new Padding(20);
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
            _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 206));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));

            var header = new Panel { Dock = DockStyle.Fill };
            _titleLabel.AutoSize = true;
            _titleLabel.Location = new Point(0, 2);
            _titleLabel.Font = new Font("Segoe UI Semibold", 15F);
            _titleLabel.Text = "应用设置";
            _subtitleLabel.AutoSize = true;
            _subtitleLabel.Location = new Point(1, 37);
            _subtitleLabel.Text = "主题和全局快捷键会在下次启动时自动恢复。";
            header.Controls.Add(_titleLabel);
            header.Controls.Add(_subtitleLabel);

            _cardsPanel.Dock = DockStyle.Fill;
            _cardsPanel.AutoScroll = true;
            _cardsPanel.FlowDirection = FlowDirection.LeftToRight;
            _cardsPanel.WrapContents = true;
            _cardsPanel.Padding = new Padding(0, 4, 0, 4);
            foreach (var theme in AppThemeCatalog.All)
            {
                var card = new ThemeCard(theme, theme.Id == SelectedThemeId);
                card.Click += (_, _) => SelectTheme(theme.Id);
                _cards.Add(card);
                _cardsPanel.Controls.Add(card);
            }

            ConfigureHotKeyPanel();

            _footerPanel.Dock = DockStyle.Fill;
            _footerPanel.FlowDirection = FlowDirection.RightToLeft;
            _footerPanel.WrapContents = false;
            _footerPanel.Padding = new Padding(0, 13, 0, 0);
            ConfigureDialogButton(_saveButton, "保存", true);
            _saveButton.Click += (_, _) => SaveSettings();
            ConfigureDialogButton(_cancelButton, "取消", false);
            _cancelButton.DialogResult = DialogResult.Cancel;
            _footerPanel.Controls.Add(_saveButton);
            _footerPanel.Controls.Add(_cancelButton);

            _root.Controls.Add(header, 0, 0);
            _root.Controls.Add(_cardsPanel, 0, 1);
            _root.Controls.Add(_hotKeyPanel, 0, 2);
            _root.Controls.Add(_footerPanel, 0, 3);
            Controls.Add(_root);
            AcceptButton = _saveButton;
            CancelButton = _cancelButton;
        }

        private void ConfigureHotKeyPanel()
        {
            _hotKeyPanel.Name = "HotKeySettingsPanel";
            _hotKeyPanel.Dock = DockStyle.Fill;
            _hotKeyPanel.ColumnCount = 3;
            _hotKeyPanel.RowCount = 5;
            _hotKeyPanel.Padding = new Padding(0, 8, 0, 0);
            _hotKeyPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126));
            _hotKeyPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 238));
            _hotKeyPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _hotKeyPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            _hotKeyPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            _hotKeyPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            _hotKeyPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            _hotKeyPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

            _hotKeyTitleLabel.AutoSize = true;
            _hotKeyTitleLabel.Font = new Font("Segoe UI Semibold", 11F);
            _hotKeyTitleLabel.Text = "全局快捷键";
            _hotKeyTitleLabel.Margin = new Padding(0, 5, 0, 0);
            _hotKeySubtitleLabel.AutoSize = true;
            _hotKeySubtitleLabel.Text = "选中输入框后直接按新组合键，至少包含一个修饰键。";
            _hotKeySubtitleLabel.Margin = new Padding(0, 8, 0, 0);

            ConfigureHotKeyInput(_captureHotKeyInput);
            ConfigureHotKeyInput(_startPauseHotKeyInput);
            ConfigureHotKeyInput(_stopHotKeyInput);
            _restoreDefaultHotKeysButton.Name = "RestoreDefaultHotKeysButton";
            ConfigureDialogButton(_restoreDefaultHotKeysButton, "恢复默认快捷键", false);
            _restoreDefaultHotKeysButton.Width = 138;
            _restoreDefaultHotKeysButton.Margin = new Padding(0, 5, 0, 0);
            _restoreDefaultHotKeysButton.Click += (_, _) => RestoreDefaultHotKeys();

            _hotKeyPanel.Controls.Add(_hotKeyTitleLabel, 0, 0);
            _hotKeyPanel.SetColumnSpan(_hotKeyTitleLabel, 1);
            _hotKeyPanel.Controls.Add(_hotKeySubtitleLabel, 1, 0);
            _hotKeyPanel.SetColumnSpan(_hotKeySubtitleLabel, 2);
            AddHotKeyRow(1, "采点", _captureHotKeyInput);
            AddHotKeyRow(2, "开始 / 暂停", _startPauseHotKeyInput);
            AddHotKeyRow(3, "停止", _stopHotKeyInput);
            _hotKeyPanel.Controls.Add(_restoreDefaultHotKeysButton, 1, 4);
        }

        private void AddHotKeyRow(int row, string labelText, HotKeyInput input)
        {
            var label = new Label
            {
                AutoSize = true,
                Text = labelText,
                Margin = new Padding(0, 9, 8, 0)
            };
            _hotKeyPanel.Controls.Add(label, 0, row);
            _hotKeyPanel.Controls.Add(input, 1, row);
        }

        private static void ConfigureHotKeyInput(HotKeyInput input)
        {
            input.Height = 28;
            input.Margin = new Padding(0, 3, 0, 3);
            input.TabStop = true;
        }

        private void SaveSettings()
        {
            if (!HotKeyBindingService.ValidateSet(
                    CaptureHotKey,
                    StartPauseHotKey,
                    StopHotKey,
                    out var error))
            {
                MessageBox.Show(
                    this,
                    error,
                    "快捷键设置无效",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        private void RestoreDefaultHotKeys()
        {
            _captureHotKeyInput.Binding = HotKeyBindingService.DefaultCapture;
            _startPauseHotKeyInput.Binding = HotKeyBindingService.DefaultStartPause;
            _stopHotKeyInput.Binding = HotKeyBindingService.DefaultStop;
        }

        private static void ConfigureDialogButton(Button button, string text, bool primary)
        {
            button.Text = text;
            button.Width = 96;
            button.Height = 34;
            button.Margin = new Padding(8, 0, 0, 0);
            button.FlatStyle = FlatStyle.Flat;
            button.UseVisualStyleBackColor = false;
            button.Cursor = Cursors.Hand;
        }

        private void SelectTheme(AppThemeId themeId)
        {
            SelectedThemeId = themeId;
            foreach (var card in _cards)
            {
                card.Selected = card.PreviewTheme.Id == themeId;
            }

            var selectedTheme = AppThemeCatalog.Get(themeId);
            ApplyDialogTheme(selectedTheme);
            ThemePreviewed?.Invoke(selectedTheme);
        }

        private void ApplyDialogTheme(AppTheme theme)
        {
            BackColor = theme.Window;
            ForeColor = theme.Text;
            _root.BackColor = theme.Window;
            _cardsPanel.BackColor = theme.Window;
            _hotKeyPanel.BackColor = theme.Window;
            _footerPanel.BackColor = theme.Window;
            _titleLabel.BackColor = theme.Window;
            _titleLabel.ForeColor = theme.Text;
            _subtitleLabel.BackColor = theme.Window;
            _subtitleLabel.ForeColor = theme.MutedText;
            _hotKeyTitleLabel.BackColor = theme.Window;
            _hotKeyTitleLabel.ForeColor = theme.Text;
            _hotKeySubtitleLabel.BackColor = theme.Window;
            _hotKeySubtitleLabel.ForeColor = theme.MutedText;
            if (_titleLabel.Parent is not null)
            {
                _titleLabel.Parent.BackColor = theme.Window;
            }

            foreach (Control control in _hotKeyPanel.Controls)
            {
                switch (control)
                {
                    case HotKeyInput input:
                        input.BackColor = theme.Input;
                        input.ForeColor = theme.Text;
                        break;
                    case Label label:
                        label.BackColor = theme.Window;
                        label.ForeColor = theme.MutedText;
                        break;
                }
            }

            ApplyDialogButtonTheme(_saveButton, theme, true);
            ApplyDialogButtonTheme(_cancelButton, theme, false);
            ApplyDialogButtonTheme(_restoreDefaultHotKeysButton, theme, false);
            ApplyTitleBarTheme(theme);
            Invalidate(true);
        }

        private static void ApplyDialogButtonTheme(Button button, AppTheme theme, bool primary)
        {
            button.ForeColor = primary ? Color.White : theme.Text;
            button.BackColor = primary ? theme.Primary : theme.Surface;
            button.FlatAppearance.BorderColor = primary ? theme.Primary : theme.Border;
            button.FlatAppearance.MouseOverBackColor =
                primary ? theme.PrimaryHover : theme.SurfaceHover;
            button.FlatAppearance.MouseDownBackColor =
                primary ? theme.PrimaryPressed : theme.SurfacePressed;
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

    private sealed class ThemeCard : Control
    {
        private readonly AppTheme _previewTheme;
        private bool _selected;

        public ThemeCard(AppTheme previewTheme, bool selected)
        {
            _previewTheme = previewTheme;
            _selected = selected;
            Name = $"ThemeCard_{previewTheme.Id}";
            Text = previewTheme.DisplayName;
            Size = new Size(266, 86);
            Margin = new Padding(0, 0, 10, 10);
            Cursor = Cursors.Hand;
            TabStop = true;
            AccessibleRole = AccessibleRole.RadioButton;
            AccessibleName = previewTheme.DisplayName;
            DoubleBuffered = true;
        }

        public AppTheme PreviewTheme => _previewTheme;

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Selected
        {
            get => _selected;
            set
            {
                if (_selected == value)
                {
                    return;
                }

                _selected = value;
                AccessibleDefaultActionDescription = value ? "当前已选择" : "选择此主题";
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = CreateRoundedRectangle(
                new Rectangle(1, 1, Width - 3, Height - 3),
                6);
            using var background = new SolidBrush(_previewTheme.Surface);
            using var border = new Pen(
                _selected ? _previewTheme.Primary : _previewTheme.Border,
                _selected ? 2F : 1F);
            e.Graphics.FillPath(background, path);
            e.Graphics.DrawPath(border, path);

            using var nameFont = new Font(Font, FontStyle.Bold);
            TextRenderer.DrawText(
                e.Graphics,
                _previewTheme.DisplayName,
                nameFont,
                new Rectangle(16, 13, Width - 48, 24),
                _previewTheme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(
                e.Graphics,
                _previewTheme.IsDark ? "深色基调" : "明亮基调",
                Font,
                new Rectangle(16, 37, Width - 32, 20),
                _previewTheme.MutedText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

            var swatches = new[]
            {
                _previewTheme.Primary,
                _previewTheme.CaptureAccent,
                _previewTheme.WarningAccent,
                _previewTheme.SettingsAccent,
                _previewTheme.HelpAccent
            };
            for (var index = 0; index < swatches.Length; index++)
            {
                using var brush = new SolidBrush(swatches[index]);
                e.Graphics.FillRectangle(brush, 16 + index * 25, 64, 18, 8);
            }

            if (_selected)
            {
                using var checkBrush = new SolidBrush(_previewTheme.Primary);
                using var checkFont = new Font(Font.FontFamily, 8F, FontStyle.Bold);
                e.Graphics.FillEllipse(checkBrush, Width - 31, 13, 16, 16);
                TextRenderer.DrawText(
                    e.Graphics,
                    "✓",
                    checkFont,
                    new Rectangle(Width - 31, 13, 16, 16),
                    Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            if (Focused)
            {
                using var focusPath = CreateRoundedRectangle(
                    new Rectangle(5, 5, Width - 11, Height - 11),
                    4);
                using var focusPen = new Pen(_previewTheme.HelpAccent);
                e.Graphics.DrawPath(focusPen, focusPath);
            }
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode is Keys.Enter or Keys.Space)
            {
                OnClick(EventArgs.Empty);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
        {
            var diameter = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(
                bounds.Right - diameter,
                bounds.Bottom - diameter,
                diameter,
                diameter,
                0,
                90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
