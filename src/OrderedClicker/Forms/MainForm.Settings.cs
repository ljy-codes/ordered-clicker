using System.Drawing.Drawing2D;
using System.ComponentModel;
using OrderedClicker.Native;
using OrderedClicker.Theming;

namespace OrderedClicker.Forms;

public sealed partial class MainForm
{
    private void ShowThemeSettings()
    {
        var originalTheme = _theme;
        using var dialog = new ThemeSettingsDialog(_theme);
        dialog.ThemePreviewed += ApplyTheme;
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            ApplyTheme(originalTheme);
            return;
        }

        try
        {
            var selectedTheme = AppThemeCatalog.Get(dialog.SelectedThemeId);
            _settingsService.Save(new Models.AppSettings { Theme = selectedTheme.Id });
            ApplyTheme(selectedTheme);
            SetStatus($"主题已保存：{selectedTheme.DisplayName}");
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException)
        {
            ApplyTheme(originalTheme);
            ShowError($"保存主题设置失败：{exception.Message}");
        }
    }

    private sealed class ThemeSettingsDialog : Form
    {
        private readonly List<ThemeCard> _cards = [];
        private readonly TableLayoutPanel _root = new();
        private readonly Label _titleLabel = new();
        private readonly Label _subtitleLabel = new();
        private readonly FlowLayoutPanel _cardsPanel = new();
        private readonly FlowLayoutPanel _footerPanel = new();
        private readonly Button _saveButton = new();
        private readonly Button _cancelButton = new();

        public ThemeSettingsDialog(AppTheme currentTheme)
        {
            SelectedThemeId = currentTheme.Id;
            Name = "ThemeSettingsDialog";
            Text = "主题设置";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(620, 500);
            MinimumSize = new Size(620, 500);
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

        private void ConfigureLayout()
        {
            _root.Dock = DockStyle.Fill;
            _root.ColumnCount = 1;
            _root.RowCount = 3;
            _root.Padding = new Padding(20);
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
            _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));

            var header = new Panel { Dock = DockStyle.Fill };
            _titleLabel.AutoSize = true;
            _titleLabel.Location = new Point(0, 2);
            _titleLabel.Font = new Font("Segoe UI Semibold", 15F);
            _titleLabel.Text = "选择界面主题";
            _subtitleLabel.AutoSize = true;
            _subtitleLabel.Location = new Point(1, 37);
            _subtitleLabel.Text = "从预设色板中选择，保存后将在下次启动时自动恢复。";
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

            _footerPanel.Dock = DockStyle.Fill;
            _footerPanel.FlowDirection = FlowDirection.RightToLeft;
            _footerPanel.WrapContents = false;
            _footerPanel.Padding = new Padding(0, 13, 0, 0);
            ConfigureDialogButton(_saveButton, "保存", true);
            _saveButton.DialogResult = DialogResult.OK;
            ConfigureDialogButton(_cancelButton, "取消", false);
            _cancelButton.DialogResult = DialogResult.Cancel;
            _footerPanel.Controls.Add(_saveButton);
            _footerPanel.Controls.Add(_cancelButton);

            _root.Controls.Add(header, 0, 0);
            _root.Controls.Add(_cardsPanel, 0, 1);
            _root.Controls.Add(_footerPanel, 0, 2);
            Controls.Add(_root);
            AcceptButton = _saveButton;
            CancelButton = _cancelButton;
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
            _footerPanel.BackColor = theme.Window;
            _titleLabel.BackColor = theme.Window;
            _titleLabel.ForeColor = theme.Text;
            _subtitleLabel.BackColor = theme.Window;
            _subtitleLabel.ForeColor = theme.MutedText;
            if (_titleLabel.Parent is not null)
            {
                _titleLabel.Parent.BackColor = theme.Window;
            }

            ApplyDialogButtonTheme(_saveButton, theme, true);
            ApplyDialogButtonTheme(_cancelButton, theme, false);
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
