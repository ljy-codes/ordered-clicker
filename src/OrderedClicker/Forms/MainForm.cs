using System.ComponentModel;
using OrderedClicker.Core;
using OrderedClicker.Models;
using OrderedClicker.Native;
using OrderedClicker.Services;
using OrderedClicker.Theming;

namespace OrderedClicker.Forms;

public sealed partial class MainForm : Form
{
    private const int CaptureHotKeyId = 1001;
    private const int StartPauseHotKeyId = 1002;
    private const int StopHotKeyId = 1003;

    private sealed class ThemedToolStripColorTable(AppTheme theme) : ProfessionalColorTable
    {
        public override Color StatusStripGradientBegin => theme.StatusBar;
        public override Color StatusStripGradientEnd => theme.StatusBar;
        public override Color ToolStripBorder => theme.Border;
        public override Color SeparatorDark => theme.Border;
        public override Color SeparatorLight => theme.GridLine;
    }

    private readonly MonitorService _monitorService = new();
    private readonly ProfileService _profileService = new();
    private readonly SettingsService _settingsService;
    private readonly ClickExecutionEngine _executionEngine = new(
        new WindowsMouseController(),
        stabilityDetector: new ScreenStabilityDetector(new WindowsScreenSampler()));
    private readonly AsyncPauseGate _pauseGate = new();
    private readonly ToolTip _toolTip = new();
    private readonly bool _enableGlobalHotKeys;

    private readonly TextBox _profileNameTextBox = new();
    private readonly NumericUpDown _totalLoopsInput = new();
    private readonly NumericUpDown _loopDelayInput = new();
    private readonly NumericUpDown _defaultClickIntervalInput = new();
    private readonly NumericUpDown _defaultAfterDelayInput = new();
    private readonly DataGridView _pointGrid = new();
    private readonly BindingSource _pointBindingSource = new();
    private readonly Button _captureButton = new();
    private readonly Button _moveUpButton = new();
    private readonly Button _moveDownButton = new();
    private readonly Button _deleteButton = new();
    private readonly Button _clearButton = new();
    private readonly Button _themeSettingsButton = new();
    private readonly Button _usageHelpButton = new();
    private readonly Button _saveButton = new();
    private readonly Button _saveAsButton = new();
    private readonly Button _importProfileButton = new();
    private readonly Button _exportProfileButton = new();
    private readonly Button _applyClickIntervalButton = new();
    private readonly Button _applyAfterDelayButton = new();
    private readonly Button _startPauseButton = new();
    private readonly Button _stopButton = new();
    private readonly Button _restartButton = new();
    private readonly CheckBox _cloudDesktopEnabledCheckBox = new();
    private readonly Button _calibrateRegionButton = new();
    private readonly Label _cloudRegionStatusLabel = new();
    private readonly CheckBox _waitForStableScreenCheckBox = new();
    private readonly NumericUpDown _stabilityTimeoutInput = new();
    private readonly Label _executionHintLabel = new();
    private readonly ToolStripStatusLabel _stateStatusLabel = new();
    private readonly ToolStripStatusLabel _progressStatusLabel = new();
    private readonly ToolStripStatusLabel _hotKeyStatusLabel = new();
    private readonly StatusStrip _statusStrip = new();

    private BindingList<ClickPoint> _points = [];
    private readonly Dictionary<int, HotKeyRegistration> _activeHotKeyRegistrations = [];
    private AppSettings _settings;
    private AppTheme _theme;
    private HotKeyRegistrationCoordinator? _hotKeyCoordinator;
    private CancellationTokenSource? _executionCancellation;
    private ExecutionState _executionState = ExecutionState.Idle;
    private CaptureMode _captureMode;
    private Point? _cloudRegionTopLeft;
    private CloudDesktopRegion? _cloudDesktopRegion;
    private int _captureSessionStartCount;
    private string? _currentProfilePath;
    private bool _saveImportedProfileAsCopy;
    private string? _importedProfileSourcePath;
    private ExecutionPlan? _pendingExecutionPlan;
    private ExecutionCheckpoint _executionCheckpoint = ExecutionCheckpoint.Start;
    private bool _suspendHotKeyActions;
    private bool _activeHotKeysKnown;

    public MainForm(
        bool enableGlobalHotKeys = true,
        SettingsService? settingsService = null)
    {
        _enableGlobalHotKeys = enableGlobalHotKeys;
        _settingsService = settingsService ?? new SettingsService();
        _settings = _settingsService.Load();
        _theme = AppThemeCatalog.Get(_settings.Theme);
        InitializeWindow();
        BuildLayout();
        ConfigurePointGrid();
        WireEvents();
        ApplyProfile(new ClickProfile());
        UpdateCaptureButton();
        SetExecutionState(ExecutionState.Idle);
        UpdateHotKeyText();
        ApplyTheme(_theme);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_enableGlobalHotKeys)
        {
            RegisterGlobalHotKeys();
        }

        if (_settings.RequiresSaveAfterLoad
            && (!_enableGlobalHotKeys || _activeHotKeyRegistrations.Count == 3))
        {
            _settingsService.Save(_settings);
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyDarkTitleBar();
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == NativeMethods.WmHotKey)
        {
            if (_suspendHotKeyActions)
            {
                return;
            }

            switch (message.WParam.ToInt32())
            {
                case CaptureHotKeyId:
                    CaptureCurrentPoint();
                    return;
                case StartPauseHotKeyId:
                    _ = HandleStartPauseAsync();
                    return;
                case StopHotKeyId:
                    StopExecution();
                    return;
            }
        }

        base.WndProc(ref message);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _executionCancellation?.Cancel();
        _pauseGate.Resume();
        _hotKeyCoordinator?.Dispose();
        _toolTip.Dispose();
        base.OnFormClosing(e);
    }

    private void InitializeWindow()
    {
        Text = "有序连点器";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(980, 620);
        ClientSize = new Size(1080, 700);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9F);
        BackColor = _theme.Window;
        ForeColor = _theme.Text;
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            Padding = new Padding(12),
            BackColor = _theme.Window
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));

        root.Controls.Add(BuildProfilePanel(), 0, 0);
        root.Controls.Add(BuildTimingPanel(), 0, 1);
        root.Controls.Add(BuildCloudDesktopPanel(), 0, 2);
        root.Controls.Add(_pointGrid, 0, 3);
        root.Controls.Add(BuildPointToolbar(), 0, 4);
        root.Controls.Add(BuildExecutionPanel(), 0, 5);
        root.Controls.Add(BuildStatusStrip(), 0, 6);
        Controls.Add(root);
    }

    private Control BuildProfilePanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = _theme.Window
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        var fields = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 5, 0, 5),
            BackColor = _theme.Window
        };
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 3, 0, 5),
            BackColor = _theme.Window
        };

        _profileNameTextBox.Width = 200;
        _profileNameTextBox.Text = "默认方案";
        ConfigureInput(_profileNameTextBox);
        _totalLoopsInput.Minimum = 1;
        _totalLoopsInput.Maximum = 100000;
        _totalLoopsInput.Value = 1;
        _totalLoopsInput.Width = 78;
        ConfigureInput(_totalLoopsInput);
        _loopDelayInput.Minimum = 0;
        _loopDelayInput.Maximum = 600000;
        _loopDelayInput.Increment = 100;
        _loopDelayInput.Value = 1000;
        _loopDelayInput.Width = 96;
        ConfigureInput(_loopDelayInput);

        _saveButton.Name = "SaveButton";
        _saveButton.Text = "保存";
        _saveAsButton.Name = "SaveAsButton";
        _saveAsButton.Text = "另存为";
        _importProfileButton.Name = "ImportProfileButton";
        _importProfileButton.Text = "导入方案";
        _exportProfileButton.Name = "ExportProfileButton";
        _exportProfileButton.Text = "导出方案";
        ConfigureCommandButton(_saveButton, 68);
        ConfigureCommandButton(_saveAsButton, 78);
        ConfigureCommandButton(_importProfileButton, 78);
        ConfigureCommandButton(_exportProfileButton, 78);

        fields.Controls.Add(CreateFieldLabel("方案名称"));
        fields.Controls.Add(_profileNameTextBox);
        fields.Controls.Add(CreateSpacer(12));
        fields.Controls.Add(CreateFieldLabel("总循环次数"));
        fields.Controls.Add(_totalLoopsInput);
        fields.Controls.Add(CreateSpacer(12));
        fields.Controls.Add(CreateFieldLabel("轮间等待(ms)"));
        fields.Controls.Add(_loopDelayInput);

        actions.Controls.Add(_saveButton);
        actions.Controls.Add(_saveAsButton);
        actions.Controls.Add(_importProfileButton);
        actions.Controls.Add(_exportProfileButton);
        panel.Controls.Add(fields, 0, 0);
        panel.Controls.Add(actions, 0, 1);
        return panel;
    }

    private Control BuildTimingPanel()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 7, 0, 7),
            BackColor = _theme.Window
        };

        _defaultClickIntervalInput.Name = "DefaultClickIntervalInput";
        _defaultClickIntervalInput.Minimum = 10;
        _defaultClickIntervalInput.Maximum = 600000;
        _defaultClickIntervalInput.Increment = 100;
        _defaultClickIntervalInput.Value = 100;
        _defaultClickIntervalInput.Width = 110;
        ConfigureInput(_defaultClickIntervalInput);

        _defaultAfterDelayInput.Name = "DefaultAfterDelayInput";
        _defaultAfterDelayInput.Minimum = 0;
        _defaultAfterDelayInput.Maximum = 600000;
        _defaultAfterDelayInput.Increment = 100;
        _defaultAfterDelayInput.Value = 500;
        _defaultAfterDelayInput.Width = 110;
        ConfigureInput(_defaultAfterDelayInput);

        _applyClickIntervalButton.Name = "ApplyClickIntervalButton";
        _applyClickIntervalButton.Text = "应用全部";
        _applyAfterDelayButton.Name = "ApplyAfterDelayButton";
        _applyAfterDelayButton.Text = "应用全部";
        ConfigureCommandButton(_applyClickIntervalButton, 94);
        ConfigureCommandButton(_applyAfterDelayButton, 94);

        var sectionLabel = CreateFieldLabel("点位时间");
        sectionLabel.Font = new Font(Font, FontStyle.Bold);
        sectionLabel.ForeColor = _theme.Text;

        panel.Controls.Add(sectionLabel);
        panel.Controls.Add(CreateSpacer(10));
        panel.Controls.Add(CreateFieldLabel("点击间隔(ms)"));
        panel.Controls.Add(_defaultClickIntervalInput);
        panel.Controls.Add(_applyClickIntervalButton);
        panel.Controls.Add(CreateSpacer(22));
        panel.Controls.Add(CreateFieldLabel("点后等待(ms)"));
        panel.Controls.Add(_defaultAfterDelayInput);
        panel.Controls.Add(_applyAfterDelayButton);
        return panel;
    }

    private Control BuildPointToolbar()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 7, 0, 5),
            BackColor = _theme.Window
        };

        ConfigureCommandButton(_captureButton, 190);
        _moveUpButton.Text = "↑ 上移";
        _moveDownButton.Text = "↓ 下移";
        _deleteButton.Text = "删除";
        _clearButton.Text = "清空";
        _themeSettingsButton.Name = "ThemeSettingsButton";
        _themeSettingsButton.Text = "⚙ 设置";
        _usageHelpButton.Name = "UsageHelpButton";
        _usageHelpButton.Text = "? 使用说明";
        ConfigureCommandButton(_moveUpButton, 86);
        ConfigureCommandButton(_moveDownButton, 86);
        ConfigureCommandButton(_deleteButton, 82);
        ConfigureCommandButton(_clearButton, 82);
        ConfigureCommandButton(_themeSettingsButton, 126);
        ConfigureCommandButton(_usageHelpButton, 150);

        panel.Controls.Add(_captureButton);
        panel.Controls.Add(_moveUpButton);
        panel.Controls.Add(_moveDownButton);
        panel.Controls.Add(_deleteButton);
        panel.Controls.Add(_clearButton);
        panel.Controls.Add(CreateSpacer(28));
        panel.Controls.Add(_themeSettingsButton);
        panel.Controls.Add(_usageHelpButton);
        return panel;
    }

    private Control BuildExecutionPanel()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 10, 0, 8),
            BackColor = _theme.Window
        };

        ConfigurePrimaryButton(_startPauseButton, 190);
        ConfigureCommandButton(_stopButton, 186);
        _restartButton.Name = "RestartExecutionButton";
        _restartButton.Text = "重新开始";
        _restartButton.Visible = false;
        ConfigureCommandButton(_restartButton, 110);

        _executionHintLabel.AutoSize = true;
        _executionHintLabel.Margin = new Padding(18, 10, 0, 0);
        _executionHintLabel.ForeColor = _theme.MutedText;
        _executionHintLabel.BackColor = _theme.Window;

        panel.Controls.Add(_startPauseButton);
        panel.Controls.Add(_stopButton);
        panel.Controls.Add(_restartButton);
        panel.Controls.Add(_executionHintLabel);
        return panel;
    }

    private StatusStrip BuildStatusStrip()
    {
        _statusStrip.Dock = DockStyle.Fill;
        _statusStrip.SizingGrip = false;
        _statusStrip.BackColor = _theme.StatusBar;
        _statusStrip.ForeColor = _theme.Text;
        _statusStrip.Renderer =
            new ToolStripProfessionalRenderer(new ThemedToolStripColorTable(_theme));
        _statusStrip.Padding = new Padding(4, 0, 4, 0);
        _stateStatusLabel.Text = "就绪";
        _stateStatusLabel.ForeColor = _theme.Text;
        _progressStatusLabel.Spring = true;
        _progressStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _progressStatusLabel.ForeColor = _theme.MutedText;
        _hotKeyStatusLabel.ForeColor = _theme.Text;
        _statusStrip.Items.Add(_stateStatusLabel);
        _statusStrip.Items.Add(new ToolStripSeparator());
        _statusStrip.Items.Add(_progressStatusLabel);
        _statusStrip.Items.Add(_hotKeyStatusLabel);
        return _statusStrip;
    }

    private void ConfigurePointGrid()
    {
        _pointGrid.Dock = DockStyle.Fill;
        _pointGrid.AutoGenerateColumns = false;
        _pointGrid.AllowUserToAddRows = false;
        _pointGrid.AllowUserToDeleteRows = false;
        _pointGrid.AllowUserToResizeRows = false;
        _pointGrid.MultiSelect = false;
        _pointGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _pointGrid.RowHeadersVisible = false;
        _pointGrid.BackgroundColor = _theme.Input;
        _pointGrid.BorderStyle = BorderStyle.FixedSingle;
        _pointGrid.GridColor = _theme.GridLine;
        _pointGrid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        _pointGrid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        _pointGrid.ColumnHeadersHeight = 34;
        _pointGrid.RowTemplate.Height = 32;
        _pointGrid.EnableHeadersVisualStyles = false;
        _pointGrid.ColumnHeadersDefaultCellStyle.BackColor = _theme.Header;
        _pointGrid.ColumnHeadersDefaultCellStyle.ForeColor = _theme.Text;
        _pointGrid.ColumnHeadersDefaultCellStyle.SelectionBackColor = _theme.Header;
        _pointGrid.ColumnHeadersDefaultCellStyle.SelectionForeColor = _theme.Text;
        _pointGrid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
        _pointGrid.ColumnHeadersDefaultCellStyle.Font = new Font(Font, FontStyle.Bold);
        _pointGrid.DefaultCellStyle.BackColor = _theme.Surface;
        _pointGrid.DefaultCellStyle.ForeColor = _theme.Text;
        _pointGrid.DefaultCellStyle.SelectionBackColor = _theme.Selection;
        _pointGrid.DefaultCellStyle.SelectionForeColor = Color.White;
        _pointGrid.AlternatingRowsDefaultCellStyle.BackColor = _theme.SurfaceAlternate;

        _pointGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            DataPropertyName = nameof(ClickPoint.Enabled),
            HeaderText = "启用",
            Width = 54
        });
        _pointGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Order",
            HeaderText = "顺序",
            ReadOnly = true,
            Width = 58
        });
        _pointGrid.Columns.Add(CreateTextColumn(nameof(ClickPoint.X), "X", 82, true));
        _pointGrid.Columns.Add(CreateTextColumn(nameof(ClickPoint.Y), "Y", 82, true));
        _pointGrid.Columns.Add(CreateTextColumn(nameof(ClickPoint.ClickCount), "点击次数", 96));
        _pointGrid.Columns.Add(CreateTextColumn(nameof(ClickPoint.ClickIntervalMs), "点击间隔(ms)", 118));
        _pointGrid.Columns.Add(CreateTextColumn(nameof(ClickPoint.AfterDelayMs), "点后等待(ms)", 118));
        _pointGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(ClickPoint.MonitorDeviceName),
            HeaderText = "显示器",
            ReadOnly = true,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = 150
        });
        _pointGrid.DataSource = _pointBindingSource;
    }

    private void WireEvents()
    {
        _toolTip.SetToolTip(_moveUpButton, "将选中点位提前一个执行顺序。");
        _toolTip.SetToolTip(_moveDownButton, "将选中点位后移一个执行顺序。");
        _toolTip.SetToolTip(_deleteButton, "删除当前选中的点位。");
        _toolTip.SetToolTip(_themeSettingsButton, "选择界面主题并配置全局快捷键。");
        _toolTip.SetToolTip(_usageHelpButton, "查看连点器操作说明。");
        _toolTip.SetToolTip(
            _defaultClickIntervalInput,
            "后续新采集点默认使用的点击间隔。");
        _toolTip.SetToolTip(
            _applyClickIntervalButton,
            "将当前点击间隔应用到全部已有点位。");
        _toolTip.SetToolTip(
            _defaultAfterDelayInput,
            "后续新采集点默认使用的点后等待。");
        _toolTip.SetToolTip(
            _applyAfterDelayButton,
            "将当前点后等待应用到全部已有点位。");
        _toolTip.SetToolTip(_startPauseButton, "开始、暂停或继续执行。");
        _toolTip.SetToolTip(_stopButton, "立即停止倒计时、等待或点击任务。");
        _toolTip.SetToolTip(_saveAsButton, "将当前方案保存到指定位置。");
        _toolTip.SetToolTip(
            _importProfileButton,
            "从 JSON 文件导入方案副本，不会覆盖来源文件。");
        _toolTip.SetToolTip(
            _exportProfileButton,
            "将当前方案导出为 JSON 文件，不改变当前保存位置。");
        _toolTip.SetToolTip(_calibrateRegionButton, "依次记录云桌面画面的左上角和右下角。");
        _captureButton.Click += (_, _) => ToggleCaptureMode();
        _moveUpButton.Click += (_, _) => MoveSelectedPoint(-1);
        _moveDownButton.Click += (_, _) => MoveSelectedPoint(1);
        _deleteButton.Click += (_, _) => DeleteSelectedPoint();
        _clearButton.Click += (_, _) => ClearPoints();
        _saveButton.Click += (_, _) => SaveProfile();
        _saveAsButton.Click += (_, _) => SaveProfileAs();
        _importProfileButton.Click += (_, _) => ImportProfile();
        _exportProfileButton.Click += (_, _) => ExportProfile();
        _themeSettingsButton.Click += (_, _) => ShowThemeSettings();
        _usageHelpButton.Click += (_, _) => ShowUsageHelp();
        _applyClickIntervalButton.Click += (_, _) => ApplyClickIntervalToAll();
        _applyAfterDelayButton.Click += (_, _) => ApplyAfterDelayToAll();
        _startPauseButton.Click += async (_, _) => await HandleStartPauseAsync();
        _stopButton.Click += (_, _) => StopExecution();
        _restartButton.Click += async (_, _) =>
        {
            ClearExecutionCheckpoint();
            await StartExecutionAsync();
        };
        _pointGrid.CellFormatting += PointGridOnCellFormatting;
        _pointGrid.EditingControlShowing += (_, eventArgs) =>
        {
            eventArgs.Control.BackColor = _theme.Input;
            eventArgs.Control.ForeColor = _theme.Text;
        };
        _pointGrid.DataError += (_, eventArgs) =>
        {
            eventArgs.ThrowException = false;
            SetStatus("请输入有效的整数。");
        };
    }

    private void RegisterGlobalHotKeys()
    {
        _hotKeyCoordinator = new HotKeyRegistrationCoordinator(new HotKeyService(Handle));
        var result = _hotKeyCoordinator.RegisterInitial(CreateHotKeyRegistrations(_settings));
        RefreshActiveHotKeyRegistrations();
        UpdateHotKeyText();
        if (!result.Success)
        {
            MessageBox.Show(
                $"{result.Message}{Environment.NewLine}{Environment.NewLine}"
                + "冲突项可在“设置”中修改，界面按钮仍可正常使用。",
                "全局热键不可用",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void ApplyDarkTitleBar()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            return;
        }

        var enabled = _theme.IsDark ? 1 : 0;
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

    private void ApplyTheme(AppTheme theme)
    {
        _theme = theme;
        BackColor = theme.Window;
        ForeColor = theme.Text;

        foreach (var control in EnumerateControls(this))
        {
            switch (control)
            {
                case TextBox:
                case NumericUpDown:
                    control.BackColor = theme.Input;
                    control.ForeColor = theme.Text;
                    break;
                case Label:
                    control.BackColor = theme.Window;
                    control.ForeColor = theme.MutedText;
                    break;
                case FlowLayoutPanel:
                case TableLayoutPanel:
                case Panel:
                    control.BackColor = theme.Window;
                    control.ForeColor = theme.Text;
                    break;
            }
        }

        ApplyPointGridTheme();
        ApplyStatusStripTheme();

        ApplyNeutralButtonTheme(_saveButton);
        ApplyNeutralButtonTheme(_saveAsButton);
        ApplyNeutralButtonTheme(_importProfileButton);
        ApplyNeutralButtonTheme(_exportProfileButton);
        ApplyNeutralButtonTheme(_moveUpButton);
        ApplyNeutralButtonTheme(_moveDownButton);
        ApplyNeutralButtonTheme(_deleteButton);
        ApplyNeutralButtonTheme(_clearButton);
        ApplyNeutralButtonTheme(_themeSettingsButton);
        ApplyNeutralButtonTheme(_usageHelpButton);
        ApplyNeutralButtonTheme(_applyClickIntervalButton);
        ApplyNeutralButtonTheme(_applyAfterDelayButton);
        ApplyNeutralButtonTheme(_stopButton);
        ApplyNeutralButtonTheme(_restartButton);
        ApplyNeutralButtonTheme(_calibrateRegionButton);

        ApplyAccentButtonTheme(_saveButton, theme.Primary);
        ApplyAccentButtonTheme(_saveAsButton, theme.SettingsAccent);
        ApplyAccentButtonTheme(_importProfileButton, theme.HelpAccent);
        ApplyAccentButtonTheme(_exportProfileButton, theme.CaptureAccent);
        ApplyAccentButtonTheme(_moveUpButton, theme.Primary);
        ApplyAccentButtonTheme(_moveDownButton, theme.Primary);
        ApplyAccentButtonTheme(_deleteButton, theme.DangerAccent);
        ApplyAccentButtonTheme(_clearButton, theme.WarningAccent);
        ApplyAccentButtonTheme(_themeSettingsButton, theme.SettingsAccent);
        ApplyAccentButtonTheme(_usageHelpButton, theme.HelpAccent);
        ApplyAccentButtonTheme(_applyClickIntervalButton, theme.Primary);
        ApplyAccentButtonTheme(_applyAfterDelayButton, theme.HelpAccent);
        ApplyAccentButtonTheme(_stopButton, theme.DangerAccent);
        ApplyAccentButtonTheme(_calibrateRegionButton, theme.CaptureAccent);
        ApplyPrimaryButtonTheme(_startPauseButton);
        UpdateCaptureButton();

        if (IsHandleCreated)
        {
            ApplyDarkTitleBar();
        }

        Invalidate(true);
    }

    private void ApplyPointGridTheme()
    {
        _pointGrid.BackgroundColor = _theme.Input;
        _pointGrid.GridColor = _theme.GridLine;
        _pointGrid.ColumnHeadersDefaultCellStyle.BackColor = _theme.Header;
        _pointGrid.ColumnHeadersDefaultCellStyle.ForeColor = _theme.Text;
        _pointGrid.ColumnHeadersDefaultCellStyle.SelectionBackColor = _theme.Header;
        _pointGrid.ColumnHeadersDefaultCellStyle.SelectionForeColor = _theme.Text;
        _pointGrid.DefaultCellStyle.BackColor = _theme.Surface;
        _pointGrid.DefaultCellStyle.ForeColor = _theme.Text;
        _pointGrid.DefaultCellStyle.SelectionBackColor = _theme.Selection;
        _pointGrid.DefaultCellStyle.SelectionForeColor =
            _theme.IsDark ? Color.White : _theme.Text;
        _pointGrid.AlternatingRowsDefaultCellStyle.BackColor = _theme.SurfaceAlternate;
    }

    private void ApplyStatusStripTheme()
    {
        _statusStrip.BackColor = _theme.StatusBar;
        _statusStrip.ForeColor = _theme.Text;
        _statusStrip.Renderer =
            new ToolStripProfessionalRenderer(new ThemedToolStripColorTable(_theme));
        _stateStatusLabel.ForeColor = _theme.Text;
        _progressStatusLabel.ForeColor = _theme.MutedText;
        _hotKeyStatusLabel.ForeColor = _theme.Text;

        foreach (ToolStripItem item in _statusStrip.Items)
        {
            item.BackColor = _theme.StatusBar;
        }
    }

    private static IEnumerable<Control> EnumerateControls(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;

            foreach (var descendant in EnumerateControls(child))
            {
                yield return descendant;
            }
        }
    }

    private void ToggleCaptureMode()
    {
        if (_executionState != ExecutionState.Idle)
        {
            return;
        }

        if (_captureMode is CaptureMode.CloudRegionTopLeft
            or CaptureMode.CloudRegionBottomRight)
        {
            _captureMode = CaptureMode.Idle;
            _cloudRegionTopLeft = null;
        }
        else if (_captureMode == CaptureMode.PointCapture)
        {
            var added = _points.Count - _captureSessionStartCount;
            _captureMode = CaptureMode.Idle;
            SetStatus($"采点已结束，本次新增 {added} 个，共 {_points.Count} 个点位。");
        }
        else
        {
            _captureSessionStartCount = _points.Count;
            _captureMode = CaptureMode.PointCapture;
        }

        UpdateCaptureButton();
        if (_captureMode == CaptureMode.PointCapture)
        {
            SetStatus($"采点模式已开启，将鼠标移到目标位置后按 {CaptureHotKeyText}。");
        }
    }

    private void CaptureCurrentPoint()
    {
        if (_executionState != ExecutionState.Idle)
        {
            return;
        }

        try
        {
            if (_captureMode is CaptureMode.CloudRegionTopLeft
                or CaptureMode.CloudRegionBottomRight)
            {
                CaptureCloudRegionCorner();
                return;
            }

            if (_captureMode != CaptureMode.PointCapture)
            {
                return;
            }

            var captured = _monitorService.CaptureCursor();
            var point = new ClickPoint
            {
                X = captured.X,
                Y = captured.Y,
                MonitorDeviceName = captured.MonitorDeviceName,
                MonitorBounds = captured.MonitorBounds,
                CapturedDpi = captured.Dpi
            };
            if (_cloudDesktopEnabledCheckBox.Checked)
            {
                if (_cloudDesktopRegion is null)
                {
                    ShowError("请先校准云桌面区域，再进行采点。");
                    return;
                }

                var relative = CloudDesktopCoordinateService.ToRelative(
                    _cloudDesktopRegion,
                    captured.X,
                    captured.Y);
                point.RelativeX = relative.X;
                point.RelativeY = relative.Y;
            }

            PointTimingService.ApplyDefaults(
                point,
                decimal.ToInt32(_defaultClickIntervalInput.Value),
                decimal.ToInt32(_defaultAfterDelayInput.Value));
            _points.Add(point);
            SelectPoint(_points.Count - 1);
            SetStatus($"已记录点位 {_points.Count}：({point.X}, {point.Y})。");
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
    }

    private void MoveSelectedPoint(int offset)
    {
        var index = GetSelectedPointIndex();
        var targetIndex = index + offset;
        if (index < 0 || targetIndex < 0 || targetIndex >= _points.Count)
        {
            return;
        }

        var point = _points[index];
        _points.RemoveAt(index);
        _points.Insert(targetIndex, point);
        _pointGrid.Refresh();
        SelectPoint(targetIndex);
    }

    private void DeleteSelectedPoint()
    {
        var index = GetSelectedPointIndex();
        if (index < 0)
        {
            return;
        }

        _points.RemoveAt(index);
        _pointGrid.Refresh();
        if (_points.Count > 0)
        {
            SelectPoint(Math.Min(index, _points.Count - 1));
        }
    }

    private void ClearPoints()
    {
        if (_points.Count == 0)
        {
            return;
        }

        if (MessageBox.Show(
                "确定清空全部点位吗？",
                "清空点位",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) == DialogResult.Yes)
        {
            _points.Clear();
            _pointGrid.Refresh();
            SetStatus("已清空全部点位。");
        }
    }

    private void ApplyClickIntervalToAll()
    {
        CommitGridChanges();
        var value = decimal.ToInt32(_defaultClickIntervalInput.Value);
        PointTimingService.ApplyClickInterval(_points, value);
        _pointGrid.Refresh();
        SetStatus(
            _points.Count == 0
                ? $"新采集点的点击间隔已设为 {value} ms。"
                : $"已将 {_points.Count} 个点位的点击间隔设为 {value} ms。");
    }

    private void ApplyAfterDelayToAll()
    {
        CommitGridChanges();
        var value = decimal.ToInt32(_defaultAfterDelayInput.Value);
        PointTimingService.ApplyAfterDelay(_points, value);
        _pointGrid.Refresh();
        SetStatus(
            _points.Count == 0
                ? $"新采集点的点后等待已设为 {value} ms。"
                : $"已将 {_points.Count} 个点位的点后等待设为 {value} ms。");
    }

    private void SaveProfile()
    {
        if (_saveImportedProfileAsCopy)
        {
            SaveProfileAs();
            return;
        }

        try
        {
            CommitGridChanges();
            var path = _currentProfilePath is null
                ? _profileService.Save(CreateProfileSnapshot())
                : _profileService.Save(CreateProfileSnapshot(), _currentProfilePath);
            _currentProfilePath = path;
            _saveImportedProfileAsCopy = false;
            _importedProfileSourcePath = null;
            SetStatus($"配置已保存：{path}");
        }
        catch (Exception exception)
        {
            ShowError($"保存配置失败：{exception.Message}");
        }
    }

    private void SaveProfileAs()
    {
        var profileName = string.IsNullOrWhiteSpace(_profileNameTextBox.Text)
            ? "默认方案"
            : _profileNameTextBox.Text.Trim();
        var importCopyPath =
            _saveImportedProfileAsCopy && _importedProfileSourcePath is not null
                ? _profileService.GetAvailableImportCopyPath(
                    profileName,
                    _importedProfileSourcePath)
                : null;
        using var dialog = new SaveFileDialog
        {
            Title = "另存连点器配置",
            Filter = "连点器配置 (*.json)|*.json",
            InitialDirectory = importCopyPath is not null
                ? Path.GetDirectoryName(importCopyPath)
                : _currentProfilePath is null
                    ? _profileService.ProfilesDirectory
                    : Path.GetDirectoryName(_currentProfilePath),
            FileName = importCopyPath is not null
                ? Path.GetFileName(importCopyPath)
                : $"{profileName}.json",
            AddExtension = true,
            DefaultExt = "json",
            OverwritePrompt = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        if (_importedProfileSourcePath is not null
            && ProfileService.PathsEqual(dialog.FileName, _importedProfileSourcePath))
        {
            ShowError("导入副本不能覆盖来源文件，请选择其他文件名。");
            return;
        }

        try
        {
            CommitGridChanges();
            var path = _profileService.Save(CreateProfileSnapshot(), dialog.FileName);
            _currentProfilePath = path;
            _saveImportedProfileAsCopy = false;
            _importedProfileSourcePath = null;
            SetStatus($"配置已另存为：{path}");
        }
        catch (Exception exception)
        {
            ShowError($"另存配置失败：{exception.Message}");
        }
    }

    private void ImportProfile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "导入连点器方案副本",
            Filter = "连点器配置 (*.json)|*.json|所有文件 (*.*)|*.*",
            InitialDirectory = _profileService.ProfilesDirectory,
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var result = _profileService.Import(dialog.FileName);
        if (!result.Success)
        {
            ShowError(result.ErrorMessage ?? "方案导入失败。");
            return;
        }

        var profile = result.Profile!;
        var enabledPointCount = profile.Points.Count(point => point.Enabled);
        var confirmation = MessageBox.Show(
            this,
            $"""
            方案名称：{profile.Name}
            点位总数：{profile.Points.Count}
            启用点位：{enabledPointCount}
            总循环次数：{profile.TotalLoops}

            导入后将作为本机副本，不会覆盖来源文件。
            是否继续导入？
            """,
            "确认导入方案",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button1);
        if (confirmation != DialogResult.Yes)
        {
            SetStatus("已取消导入方案。");
            return;
        }

        ApplyImportedProfile(profile, dialog.FileName);
        ClearExecutionCheckpoint();
        SetStatus($"已导入方案副本：{dialog.FileName}");
    }

    private void ApplyImportedProfile(ClickProfile profile, string sourcePath)
    {
        ApplyProfile(profile);
        _currentProfilePath = null;
        _saveImportedProfileAsCopy = true;
        _importedProfileSourcePath = Path.GetFullPath(sourcePath);
    }

    private void ExportProfile()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "导出连点器方案",
            Filter = "连点器配置 (*.json)|*.json",
            InitialDirectory = _currentProfilePath is null
                ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                : Path.GetDirectoryName(_currentProfilePath),
            FileName = $"{_profileNameTextBox.Text.Trim()}.json",
            AddExtension = true,
            DefaultExt = "json",
            OverwritePrompt = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            CommitGridChanges();
            var path = _profileService.Export(CreateProfileSnapshot(), dialog.FileName);
            SetStatus($"方案已导出：{path}");
        }
        catch (Exception exception)
        {
            ShowError($"导出方案失败：{exception.Message}");
        }
    }

    private ClickProfile CreateProfileSnapshot()
    {
        CommitGridChanges();
        return new ClickProfile
        {
            Version = 3,
            Name = string.IsNullOrWhiteSpace(_profileNameTextBox.Text)
                ? "默认方案"
                : _profileNameTextBox.Text.Trim(),
            TotalLoops = decimal.ToInt32(_totalLoopsInput.Value),
            LoopDelayMs = decimal.ToInt32(_loopDelayInput.Value),
            DefaultClickIntervalMs = decimal.ToInt32(_defaultClickIntervalInput.Value),
            DefaultAfterDelayMs = decimal.ToInt32(_defaultAfterDelayInput.Value),
            CoordinateMode = _cloudDesktopEnabledCheckBox.Checked
                ? CoordinateMode.CloudDesktopRegion
                : CoordinateMode.AbsoluteScreen,
            CloudDesktopRegion = _cloudDesktopRegion,
            ScreenStability = new ScreenStabilitySettings
            {
                Enabled = _cloudDesktopEnabledCheckBox.Checked
                    && _waitForStableScreenCheckBox.Checked,
                TimeoutMs = decimal.ToInt32(_stabilityTimeoutInput.Value) * 1000
            },
            Points = _points.Select(ClonePoint).ToList()
        };
    }

    private void ApplyProfile(ClickProfile profile)
    {
        _profileNameTextBox.Text = profile.Name;
        _totalLoopsInput.Value = Math.Clamp(profile.TotalLoops, 1, 100000);
        _loopDelayInput.Value = Math.Clamp(profile.LoopDelayMs, 0, 600000);
        var defaults = PointTimingService.ResolveDefaults(profile);
        _defaultClickIntervalInput.Value = Math.Clamp(
            defaults.ClickIntervalMs,
            decimal.ToInt32(_defaultClickIntervalInput.Minimum),
            decimal.ToInt32(_defaultClickIntervalInput.Maximum));
        _defaultAfterDelayInput.Value = Math.Clamp(
            defaults.AfterDelayMs,
            decimal.ToInt32(_defaultAfterDelayInput.Minimum),
            decimal.ToInt32(_defaultAfterDelayInput.Maximum));
        _points = new BindingList<ClickPoint>(profile.Points.Select(ClonePoint).ToList());
        _pointBindingSource.DataSource = _points;
        ApplyCloudDesktopProfile(profile);
        _pointGrid.Refresh();
    }

    private IReadOnlyList<string> GetMonitorWarnings(ClickProfile profile)
    {
        var warnings = new List<string>();
        for (var index = 0; index < profile.Points.Count; index++)
        {
            var point = profile.Points[index];
            if (!point.Enabled || string.IsNullOrWhiteSpace(point.MonitorDeviceName))
            {
                continue;
            }

            var current = _monitorService.FindMonitor(point.MonitorDeviceName);
            if (current is null)
            {
                warnings.Add($"点位 {index + 1} 原显示器 {point.MonitorDeviceName} 当前不存在。");
                continue;
            }

            if (current.Bounds != point.MonitorBounds || current.Dpi != point.CapturedDpi)
            {
                warnings.Add(
                    $"点位 {index + 1} 的显示器分辨率、位置或缩放比例已变化，建议重新采点。");
            }
        }

        return warnings;
    }

    private void UpdateCaptureButton()
    {
        _captureButton.Text =
            _captureMode switch
            {
                CaptureMode.PointCapture => "● 结束采点",
                CaptureMode.CloudRegionTopLeft => $"记录左上角 ({CaptureHotKeyText})",
                CaptureMode.CloudRegionBottomRight => $"记录右下角 ({CaptureHotKeyText})",
                _ => $"＋ 采点 ({CaptureHotKeyText})"
            };
        var active = _captureMode != CaptureMode.Idle;
        _captureButton.BackColor = active
            ? _theme.AccentPressed(_theme.CaptureAccent)
            : _theme.AccentSurface(_theme.CaptureAccent);
        _captureButton.FlatAppearance.BorderColor = active
            ? _theme.CaptureAccent
            : _theme.CaptureAccent;
        _captureButton.FlatAppearance.MouseOverBackColor = active
            ? _theme.AccentPressed(_theme.CaptureAccent)
            : _theme.AccentHover(_theme.CaptureAccent);
        _captureButton.FlatAppearance.MouseDownBackColor =
            _theme.AccentPressed(_theme.CaptureAccent);
        _captureButton.ForeColor = _theme.AccentText(_theme.CaptureAccent);
    }

    private string CaptureHotKeyText =>
        GetActiveHotKeyText(CaptureHotKeyId, _settings.CaptureHotKey);

    private string StartPauseHotKeyText =>
        GetActiveHotKeyText(StartPauseHotKeyId, _settings.StartPauseHotKey);

    private string StopHotKeyText =>
        GetActiveHotKeyText(StopHotKeyId, _settings.StopHotKey);

    private IReadOnlyList<HotKeyRegistration> CreateHotKeyRegistrations(AppSettings settings)
    {
        return
        [
            new HotKeyRegistration(CaptureHotKeyId, "采点", settings.CaptureHotKey),
            new HotKeyRegistration(StartPauseHotKeyId, "开始/暂停", settings.StartPauseHotKey),
            new HotKeyRegistration(StopHotKeyId, "停止", settings.StopHotKey)
        ];
    }

    private void UpdateHotKeyText()
    {
        UpdateCaptureButton();
        SetExecutionState(_executionState);
        _stopButton.Text = $"停止 ({StopHotKeyText})";
        _executionHintLabel.Text =
            $"运行前有 3 秒倒计时；{StopHotKeyText} 可随时停止";
        _hotKeyStatusLabel.Text =
            $"{CaptureHotKeyText} 采点 | "
            + $"{StartPauseHotKeyText} 开始/暂停 | "
            + $"{StopHotKeyText} 停止";
        _toolTip.SetToolTip(
            _captureButton,
            $"开启后，将鼠标移动到目标位置并按 {CaptureHotKeyText} 记录坐标。");
    }

    private string GetActiveHotKeyText(int id, HotKeyBinding configuredBinding)
    {
        if (!_enableGlobalHotKeys || !_activeHotKeysKnown)
        {
            return HotKeyBindingService.Format(configuredBinding);
        }

        return _activeHotKeyRegistrations.TryGetValue(id, out var registration)
            ? HotKeyBindingService.Format(registration.Binding)
            : "未注册";
    }

    private void RefreshActiveHotKeyRegistrations()
    {
        _activeHotKeyRegistrations.Clear();
        if (_hotKeyCoordinator is not null)
        {
            foreach (var registration in _hotKeyCoordinator.RegisteredBindings)
            {
                _activeHotKeyRegistrations[registration.Id] = registration;
            }
        }

        _activeHotKeysKnown = true;
    }

    private void SetStatus(string message)
    {
        _progressStatusLabel.Text = message;
    }

    private void ShowError(string message)
    {
        MessageBox.Show(this, message, "连点器", MessageBoxButtons.OK, MessageBoxIcon.Error);
        SetStatus(message);
    }

    private void CommitGridChanges()
    {
        _pointGrid.EndEdit();
        _pointBindingSource.EndEdit();
    }

    private int GetSelectedPointIndex()
    {
        return _pointGrid.CurrentRow?.Index ?? -1;
    }

    private void SelectPoint(int index)
    {
        if (index < 0 || index >= _pointGrid.Rows.Count)
        {
            return;
        }

        _pointGrid.ClearSelection();
        _pointGrid.Rows[index].Selected = true;
        _pointGrid.CurrentCell = _pointGrid.Rows[index].Cells[0];
        _pointGrid.FirstDisplayedScrollingRowIndex = index;
    }

    private void PointGridOnCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex >= 0 && _pointGrid.Columns[e.ColumnIndex].Name == "Order")
        {
            e.Value = e.RowIndex + 1;
            e.FormattingApplied = true;
        }
    }

    private static ClickPoint ClonePoint(ClickPoint point)
    {
        return new ClickPoint
        {
            Id = point.Id,
            Enabled = point.Enabled,
            X = point.X,
            Y = point.Y,
            RelativeX = point.RelativeX,
            RelativeY = point.RelativeY,
            ClickCount = point.ClickCount,
            ClickIntervalMs = point.ClickIntervalMs,
            AfterDelayMs = point.AfterDelayMs,
            MonitorDeviceName = point.MonitorDeviceName,
            MonitorBounds = point.MonitorBounds,
            CapturedDpi = point.CapturedDpi
        };
    }

    private Label CreateFieldLabel(string text)
    {
        return new Label
        {
            AutoSize = true,
            Text = text,
            Margin = new Padding(0, 9, 7, 0),
            ForeColor = _theme.MutedText,
            BackColor = _theme.Window
        };
    }

    private static Control CreateSpacer(int width)
    {
        return new Panel { Width = width, Height = 1 };
    }

    private static DataGridViewTextBoxColumn CreateTextColumn(
        string propertyName,
        string headerText,
        int width,
        bool readOnly = false)
    {
        return new DataGridViewTextBoxColumn
        {
            DataPropertyName = propertyName,
            HeaderText = headerText,
            Width = width,
            ReadOnly = readOnly,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
    }

    private void ConfigureCommandButton(Button button, int width)
    {
        button.Width = width;
        button.Height = 32;
        button.Margin = new Padding(0, 0, 8, 0);
        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.Cursor = Cursors.Hand;
        ApplyNeutralButtonTheme(button);
    }

    private void ConfigurePrimaryButton(Button button, int width)
    {
        ConfigureCommandButton(button, width);
        ApplyPrimaryButtonTheme(button);
    }

    private void ApplyNeutralButtonTheme(Button button)
    {
        button.BackColor = _theme.Surface;
        button.ForeColor = _theme.Text;
        button.FlatAppearance.BorderColor = _theme.Border;
        button.FlatAppearance.MouseOverBackColor = _theme.SurfaceHover;
        button.FlatAppearance.MouseDownBackColor = _theme.SurfacePressed;
    }

    private void ApplyAccentButtonTheme(Button button, Color accent)
    {
        button.BackColor = _theme.AccentSurface(accent);
        button.ForeColor = _theme.AccentText(accent);
        button.FlatAppearance.BorderColor = accent;
        button.FlatAppearance.MouseOverBackColor = _theme.AccentHover(accent);
        button.FlatAppearance.MouseDownBackColor = _theme.AccentPressed(accent);
    }

    private void ApplyPrimaryButtonTheme(Button button)
    {
        button.BackColor = _theme.Primary;
        button.ForeColor = Color.White;
        button.FlatAppearance.BorderColor = _theme.Primary;
        button.FlatAppearance.MouseOverBackColor = _theme.PrimaryHover;
        button.FlatAppearance.MouseDownBackColor = _theme.PrimaryPressed;
    }

    private void ConfigureInput(Control control)
    {
        control.BackColor = _theme.Input;
        control.ForeColor = _theme.Text;
    }
}
