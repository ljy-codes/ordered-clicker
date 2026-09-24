using System.ComponentModel;
using System.Diagnostics;
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
    private readonly ProfileService _profileService;
    private readonly SettingsService _settingsService;
    private readonly DraftService? _draftService;
    private readonly DiagnosticLogService? _diagnosticLogService;
    private readonly ExecutionCheckpointService? _executionCheckpointService;
    private readonly LegacyProfileMigrationService _legacyMigrationService = new();
    private readonly System.Windows.Forms.Timer _draftTimer = new();
    private readonly System.Windows.Forms.Timer _executionProgressTimer = new();
    private readonly LatestExecutionProgress _latestExecutionProgress = new();
    private readonly ReplaceableDelay _captureDelay = new();
    private readonly ClickExecutionEngine _executionEngine = new(
        new WindowsMouseController(),
        stabilityDetector: new ScreenStabilityDetector(new WindowsScreenSampler()));
    private readonly AsyncPauseGate _pauseGate = new();
    private readonly ToolTip _toolTip = new();
    private readonly bool _enableGlobalHotKeys;

    private readonly ComboBox _profileSelector = new();
    private readonly Button _openProfilesDirectoryButton = new();
    private readonly NumericUpDown _totalLoopsInput = new();
    private readonly NumericUpDown _loopDelayInput = new();
    private readonly NumericUpDown _defaultClickIntervalInput = new();
    private readonly NumericUpDown _defaultAfterDelayInput = new();
    private readonly DataGridView _pointGrid = new();
    private readonly BindingSource _pointBindingSource = new();
    private readonly Button _captureButton = new();
    private readonly Button _captureNowButton = new();
    private readonly Button _undoButton = new();
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
    private readonly Button _openExecutionLogsButton = new();
    private readonly Button _openDiagnosticsButton = new();
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
    private readonly TabControl _workspacePages = new();
    private readonly Dictionary<WorkspaceStep, Button> _workspaceNavigation = [];
    private readonly Label _workspaceStateLabel = new();
    private readonly Label _workspaceStatusLabel = new();
    private readonly Label _captureEmptyStateLabel = new();
    private readonly Label _checkSummaryLabel = new();

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
    private string? _currentProfileFileHash;
    private bool _saveImportedProfileAsCopy;
    private string? _importedProfileSourcePath;
    private ClickProfile? _baselineProfile;
    private string? _profileSelectorTextBeforeSelection;
    private bool _suppressProfileSelection;
    private ExecutionPlan? _pendingExecutionPlan;
    private ExecutionPlan? _activeExecutionPlan;
    private ExecutionCheckpoint _executionCheckpoint = ExecutionCheckpoint.Start;
    private DateTimeOffset _lastCheckpointSaveAt = DateTimeOffset.MinValue;
    private bool _suspendHotKeyActions;
    private bool _activeHotKeysKnown;
    private Guid _profileId = Guid.NewGuid();
    private DateTime _profileCreatedAtUtc = DateTime.UtcNow;
    private DateTime _profileUpdatedAtUtc = DateTime.UtcNow;
    private ClickProfile? _lastDraftSnapshot;
    private string? _settingsLoadWarning;
    private CaptureHudForm? _captureHud;
    private RunStatusForm? _runStatusForm;
    private readonly Stack<List<ClickPoint>> _pointUndoStack = new();
    private const int PointUndoHistoryLimit = 10;
    private bool _executionStartInProgress;
    private bool _draftDirty;
    private int _captureDelayVersion;
    private Task? _activeExecutionTask;
    private bool _allowClose;
    private bool _closeInProgress;

    public MainForm(
        bool enableGlobalHotKeys = true,
        SettingsService? settingsService = null,
        AppDataPaths? appDataPaths = null)
    {
        _enableGlobalHotKeys = enableGlobalHotKeys;
        _profileService = appDataPaths is null
            ? new ProfileService()
            : new ProfileService(appDataPaths);
        _settingsService = settingsService
                           ?? (appDataPaths is null
                               ? new SettingsService()
                               : new SettingsService(appDataPaths));
        _draftService = appDataPaths is null ? null : new DraftService(appDataPaths);
        _diagnosticLogService = appDataPaths is null
            ? null
            : new DiagnosticLogService(appDataPaths);
        _executionCheckpointService = appDataPaths is null
            ? null
            : new ExecutionCheckpointService(appDataPaths);
        _executionLogService = appDataPaths is null
            ? new ExecutionLogService()
            : new ExecutionLogService(appDataPaths);
        var settingsResult = _settingsService.LoadWithResult();
        _settings = settingsResult.Settings;
        _settingsLoadWarning = settingsResult.Warning;
        _theme = AppThemeCatalog.Get(_settings.Theme);
        InitializeWindow();
        BuildLayout();
        ConfigurePointGrid();
        WireEvents();
        ApplyProfile(new ClickProfile());
        UpdateProfileBaseline();
        _lastDraftSnapshot = ProfileService.CloneProfile(_baselineProfile!);
        _draftDirty = false;
        UpdateCaptureButton();
        SetExecutionState(ExecutionState.Idle);
        UpdateHotKeyText();
        ApplyTheme(_theme);
        ConfigureDraftTimer();
        ConfigureExecutionProgressTimer();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        RecoverDraftIfAvailable();
        RecoverExecutionCheckpointIfAvailable();
        if (!string.IsNullOrWhiteSpace(_settingsLoadWarning))
        {
            MessageBox.Show(
                this,
                _settingsLoadWarning,
                "设置已恢复",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            _settingsLoadWarning = null;
        }

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
        if (!_allowClose
            && _activeExecutionTask is { IsCompleted: false })
        {
            e.Cancel = true;
            if (!_closeInProgress)
            {
                _closeInProgress = true;
                _ = CloseAfterExecutionAsync();
            }

            return;
        }

        FlushDraft();
        _draftTimer.Stop();
        _draftTimer.Dispose();
        _executionProgressTimer.Stop();
        _executionProgressTimer.Dispose();
        CancelDelayedCapture();
        _executionCancellation?.Cancel();
        _pauseGate.Resume();
        _hotKeyCoordinator?.Dispose();
        _captureDelay.Dispose();
        _toolTip.Dispose();
        _captureHud?.Close();
        _runStatusForm?.Dispose();
        base.OnFormClosing(e);
    }

    public void ActivateExistingInstance()
    {
        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }

        Show();
        Activate();
        TopMost = true;
        TopMost = false;
    }

    private async Task CloseAfterExecutionAsync()
    {
        SetStatus("正在安全停止并保存执行断点…");
        StopExecution();
        try
        {
            if (_activeExecutionTask is not null)
            {
                await _activeExecutionTask;
            }
        }
        catch (Exception exception)
        {
            RecordDiagnostic("execution.close", exception);
        }
        finally
        {
            _allowClose = true;
            Close();
        }
    }

    private void InitializeWindow()
    {
        Text = "有序连点器";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(980, 640);
        ClientSize = new Size(1180, 720);
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
            ColumnCount = 3,
            RowCount = 2,
            Padding = new Padding(8),
            BackColor = _theme.Window
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 148));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 218));

        root.Controls.Add(BuildWorkspaceNavigation(), 0, 0);
        root.Controls.Add(BuildWorkspacePages(), 1, 0);
        root.Controls.Add(BuildWorkspaceStatusPanel(), 2, 0);
        root.Controls.Add(BuildStatusStrip(), 0, 1);
        root.SetColumnSpan(_statusStrip, 3);
        Controls.Add(root);
        ShowWorkspaceStep(WorkspaceStep.Plan);
    }

    private Control BuildWorkspaceNavigation()
    {
        var panel = new FlowLayoutPanel
        {
            Name = "WorkspaceNavigation",
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(0, 8, 8, 0),
            BackColor = _theme.Window
        };
        AddWorkspaceNavigationButton(panel, WorkspaceStep.Mode, "1  模式");
        AddWorkspaceNavigationButton(panel, WorkspaceStep.Plan, "2  方案");
        AddWorkspaceNavigationButton(panel, WorkspaceStep.Capture, "3  采点");
        AddWorkspaceNavigationButton(panel, WorkspaceStep.Check, "4  检查");
        AddWorkspaceNavigationButton(panel, WorkspaceStep.Run, "5  运行 / 日志");
        return panel;
    }

    private void AddWorkspaceNavigationButton(
        Control panel,
        WorkspaceStep step,
        string text)
    {
        var button = new Button
        {
            Name = step switch
            {
                WorkspaceStep.Mode => "WorkspaceModeButton",
                WorkspaceStep.Plan => "WorkspacePlanButton",
                WorkspaceStep.Capture => "WorkspaceCaptureButton",
                WorkspaceStep.Check => "WorkspaceCheckButton",
                _ => "WorkspaceRunButton"
            },
            Text = text,
            Width = 132,
            Height = 42,
            Margin = new Padding(0, 0, 0, 8),
            TextAlign = ContentAlignment.MiddleLeft
        };
        ConfigureCommandButton(button, 132);
        button.Click += (_, _) => ShowWorkspaceStep(step);
        _workspaceNavigation[step] = button;
        panel.Controls.Add(button);
    }

    private Control BuildWorkspacePages()
    {
        _workspacePages.Name = "WorkspacePageHost";
        _workspacePages.Dock = DockStyle.Fill;
        _workspacePages.Appearance = TabAppearance.FlatButtons;
        _workspacePages.ItemSize = new Size(0, 1);
        _workspacePages.SizeMode = TabSizeMode.Fixed;
        _workspacePages.Multiline = true;
        _workspacePages.Padding = new Point(0, 0);

        _workspacePages.TabPages.Add(CreateModePage());
        _workspacePages.TabPages.Add(CreatePlanPage());
        _workspacePages.TabPages.Add(CreateCapturePage());
        _workspacePages.TabPages.Add(CreateCheckPage());
        _workspacePages.TabPages.Add(CreateRunPage());
        return _workspacePages;
    }

    private TabPage CreateModePage()
    {
        var page = CreateWorkspacePage("ModePage");
        var content = BuildCloudDesktopPanel();
        content.Dock = DockStyle.Top;
        content.Height = 96;
        page.Controls.Add(content);
        return page;
    }

    private TabPage CreatePlanPage()
    {
        var page = CreateWorkspacePage("PlanPage");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(8)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        layout.Controls.Add(BuildProfilePanel(), 0, 0);
        layout.Controls.Add(BuildTimingPanel(), 0, 1);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage CreateCapturePage()
    {
        var page = CreateWorkspacePage("CapturePage");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(8)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        _captureEmptyStateLabel.Name = "CaptureEmptyStateLabel";
        _captureEmptyStateLabel.Dock = DockStyle.Fill;
        _captureEmptyStateLabel.Text = "尚未添加点位";
        _captureEmptyStateLabel.TextAlign = ContentAlignment.MiddleLeft;
        _captureEmptyStateLabel.Font = new Font(Font, FontStyle.Bold);
        layout.Controls.Add(_captureEmptyStateLabel, 0, 0);
        layout.Controls.Add(_pointGrid, 0, 1);
        layout.Controls.Add(BuildPointToolbar(), 0, 2);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage CreateCheckPage()
    {
        var page = CreateWorkspacePage("CheckPage");
        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(16)
        };
        _checkSummaryLabel.Name = "CheckSummaryLabel";
        _checkSummaryLabel.AutoSize = false;
        _checkSummaryLabel.Width = 620;
        _checkSummaryLabel.Height = 150;
        _checkSummaryLabel.Font = new Font("Segoe UI", 10F);
        var checkButton = new Button
        {
            Name = "CheckPlanButton",
            Text = "生成并确认执行计划",
            Width = 190,
            Height = 38
        };
        ConfigurePrimaryButton(checkButton, 190);
        checkButton.Click += async (_, _) => await StartExecutionAsync();
        layout.Controls.Add(_checkSummaryLabel);
        layout.Controls.Add(checkButton);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage CreateRunPage()
    {
        var page = CreateWorkspacePage("RunPage");
        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(8)
        };
        var panel = BuildExecutionPanel();
        panel.Width = 700;
        panel.Height = 110;
        var supportActions = new FlowLayoutPanel
        {
            Width = 700,
            Height = 48,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        _openExecutionLogsButton.Name = "OpenExecutionLogsButton";
        _openExecutionLogsButton.Text = "打开执行日志";
        ConfigureCommandButton(_openExecutionLogsButton, 118);
        _openDiagnosticsButton.Name = "OpenDiagnosticsButton";
        _openDiagnosticsButton.Text = "打开诊断目录";
        ConfigureCommandButton(_openDiagnosticsButton, 118);
        supportActions.Controls.Add(_openExecutionLogsButton);
        supportActions.Controls.Add(_openDiagnosticsButton);
        layout.Controls.Add(panel);
        layout.Controls.Add(supportActions);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage CreateWorkspacePage(string name)
    {
        return new TabPage
        {
            Name = name,
            Text = name,
            BackColor = _theme.Window,
            ForeColor = _theme.Text,
            AutoScroll = true,
            Padding = new Padding(0)
        };
    }

    private Control BuildWorkspaceStatusPanel()
    {
        var panel = new TableLayoutPanel
        {
            Name = "WorkspaceStatusPanel",
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(12, 14, 4, 8),
            BackColor = _theme.Window
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        var title = new Label
        {
            Text = "工作区状态",
            Dock = DockStyle.Fill,
            Font = new Font(Font, FontStyle.Bold)
        };
        _workspaceStateLabel.Dock = DockStyle.Fill;
        _workspaceStateLabel.Text = "就绪";
        _workspaceStateLabel.Font = new Font("Segoe UI Semibold", 12F);
        _workspaceStatusLabel.Dock = DockStyle.Fill;
        _workspaceStatusLabel.Text = "等待操作";
        _workspaceStatusLabel.AutoEllipsis = true;
        var safety = new Label
        {
            Dock = DockStyle.Fill,
            Text = "紧急停止\n全局停止键 + 安全角",
            ForeColor = _theme.WarningAccent
        };
        panel.Controls.Add(title, 0, 0);
        panel.Controls.Add(_workspaceStateLabel, 0, 1);
        panel.Controls.Add(_workspaceStatusLabel, 0, 2);
        panel.Controls.Add(safety, 0, 3);
        _themeSettingsButton.Dock = DockStyle.Fill;
        _themeSettingsButton.Margin = new Padding(0, 4, 0, 4);
        _usageHelpButton.Dock = DockStyle.Fill;
        _usageHelpButton.Margin = new Padding(0, 4, 0, 4);
        panel.Controls.Add(_themeSettingsButton, 0, 4);
        panel.Controls.Add(_usageHelpButton, 0, 5);
        return panel;
    }

    private void ShowWorkspaceStep(WorkspaceStep step)
    {
        _workspacePages.SelectedIndex = (int)step;
        foreach (var item in _workspaceNavigation)
        {
            item.Value.Font = new Font(
                item.Value.Font,
                item.Key == step ? FontStyle.Bold : FontStyle.Regular);
        }

        if (step == WorkspaceStep.Check)
        {
            UpdateCheckSummary();
        }
    }

    private void UpdateCheckSummary()
    {
        var enabled = _points.Count(point => point.Enabled);
        var clicks = _points
            .Where(point => point.Enabled)
            .Sum(point => (long)point.ClickCount * decimal.ToInt32(_totalLoopsInput.Value));
        _checkSummaryLabel.Text =
            $"方案：{_profileSelector.Text}\r\n"
            + $"点位：{_points.Count} 个，启用 {enabled} 个\r\n"
            + $"循环：{_totalLoopsInput.Value} 次\r\n"
            + $"计划点击：{clicks} 次\r\n"
            + $"停止保护：{StopHotKeyText} + "
            + (_settings.SafetyCornerEnabled ? "安全角已开启" : "安全角未开启");
    }

    private void UpdateCaptureEmptyState()
    {
        _captureEmptyStateLabel.Text = _points.Count == 0
            ? "尚未添加点位，请使用采点按钮或记录当前位置"
            : $"已添加 {_points.Count} 个点位";
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
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));

        var fields = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(0, 5, 0, 5),
            BackColor = _theme.Window
        };
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(0, 3, 0, 5),
            BackColor = _theme.Window
        };

        _profileSelector.Name = "ProfileSelector";
        _profileSelector.Width = 200;
        _profileSelector.Text = "默认方案";
        _profileSelector.DropDownStyle = ComboBoxStyle.DropDown;
        _profileSelector.IntegralHeight = false;
        _profileSelector.DropDownHeight = 280;
        ConfigureInput(_profileSelector);
        _openProfilesDirectoryButton.Name = "OpenProfilesDirectoryButton";
        _openProfilesDirectoryButton.Text = "📁";
        ConfigureCommandButton(_openProfilesDirectoryButton, 38);
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
        _importProfileButton.Text = "迁移旧方案";
        _exportProfileButton.Name = "ExportProfileButton";
        _exportProfileButton.Text = "打开方案";
        ConfigureCommandButton(_saveButton, 68);
        ConfigureCommandButton(_saveAsButton, 78);
        ConfigureCommandButton(_importProfileButton, 104);
        ConfigureCommandButton(_exportProfileButton, 88);

        fields.Controls.Add(CreateFieldLabel("方案名称"));
        fields.Controls.Add(_profileSelector);
        fields.Controls.Add(_openProfilesDirectoryButton);
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
            WrapContents = true,
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
        panel.Controls.Add(CreateFieldLabel("同点连击间隔(ms)"));
        panel.Controls.Add(_defaultClickIntervalInput);
        panel.Controls.Add(_applyClickIntervalButton);
        panel.Controls.Add(CreateSpacer(22));
        panel.Controls.Add(CreateFieldLabel("步骤完成后等待(ms)"));
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
            WrapContents = true,
            Padding = new Padding(0, 7, 0, 5),
            BackColor = _theme.Window
        };

        ConfigureCommandButton(_captureButton, 190);
        _captureNowButton.Name = "CaptureNowButton";
        _captureNowButton.Text = "2 秒后记录";
        _undoButton.Name = "UndoPointChangeButton";
        _undoButton.Text = "撤销";
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
        ConfigureCommandButton(_captureNowButton, 104);
        ConfigureCommandButton(_undoButton, 82);
        ConfigureCommandButton(_themeSettingsButton, 126);
        ConfigureCommandButton(_usageHelpButton, 150);

        panel.Controls.Add(_captureButton);
        panel.Controls.Add(_captureNowButton);
        panel.Controls.Add(_moveUpButton);
        panel.Controls.Add(_moveDownButton);
        panel.Controls.Add(_deleteButton);
        panel.Controls.Add(_clearButton);
        panel.Controls.Add(_undoButton);
        return panel;
    }

    private Control BuildExecutionPanel()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
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
            _profileSelector,
            "输入方案名称，或从下拉列表打开默认方案目录中的已保存方案。");
        _toolTip.SetToolTip(
            _openProfilesDirectoryButton,
            "打开默认方案目录。");
        _toolTip.SetToolTip(
            _importProfileButton,
            "读取 v1-v3 旧 JSON 方案并转换为新的 v4 方案，不修改来源文件。");
        _toolTip.SetToolTip(
            _exportProfileButton,
            "打开任意位置的 v4 .oclick 方案，并继续保存到原路径。");
        _toolTip.SetToolTip(_calibrateRegionButton, "依次记录云桌面画面的左上角和右下角。");
        _captureButton.Click += (_, _) => ToggleCaptureMode();
        _captureNowButton.Click += async (_, _) => await RecordCurrentPositionWithDelayAsync();
        _undoButton.Click += (_, _) => UndoPointChange();
        _moveUpButton.Click += (_, _) => MoveSelectedPoint(-1);
        _moveDownButton.Click += (_, _) => MoveSelectedPoint(1);
        _deleteButton.Click += (_, _) => DeleteSelectedPoint();
        _clearButton.Click += (_, _) => ClearPoints();
        _saveButton.Click += (_, _) => SaveProfile();
        _saveAsButton.Click += (_, _) => SaveProfileAs();
        _profileSelector.DropDown += (_, _) =>
        {
            _profileSelectorTextBeforeSelection = _profileSelector.Text;
            RefreshProfileDirectory();
        };
        _profileSelector.KeyDown += (_, eventArgs) =>
            CaptureProfileSelectorTextBeforeKeyboardSelection(eventArgs);
        _profileSelector.MouseWheel += (_, _) =>
            _profileSelectorTextBeforeSelection = _profileSelector.Text;
        _profileSelector.SelectionChangeCommitted += (_, _) => SelectLocalProfile();
        _openProfilesDirectoryButton.Click += (_, _) => OpenProfilesDirectory();
        _importProfileButton.Click += (_, _) => ImportProfile();
        _exportProfileButton.Click += (_, _) => OpenExternalProfile();
        _themeSettingsButton.Click += (_, _) => ShowThemeSettings();
        _usageHelpButton.Click += (_, _) => ShowUsageHelp();
        _applyClickIntervalButton.Click += (_, _) => ApplyClickIntervalToAll();
        _applyAfterDelayButton.Click += (_, _) => ApplyAfterDelayToAll();
        _startPauseButton.Click += async (_, _) => await HandleStartPauseAsync();
        _stopButton.Click += (_, _) => StopExecution();
        _openExecutionLogsButton.Click += (_, _) => OpenSupportDirectory(
            _executionLogService.LogDirectory,
            "execution.logs.directory");
        _openDiagnosticsButton.Click += (_, _) => OpenSupportDirectory(
            _diagnosticLogService?.DiagnosticsDirectory
            ?? Path.Combine(
                Directory.GetParent(_executionLogService.LogDirectory)?.FullName
                ?? _executionLogService.LogDirectory,
                "diagnostics"),
            "diagnostics.directory");
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
        _pointGrid.CellValueChanged += (_, _) => MarkDraftDirty();
        _profileSelector.TextChanged += (_, _) => MarkDraftDirty();
        _totalLoopsInput.ValueChanged += (_, _) => MarkDraftDirty();
        _loopDelayInput.ValueChanged += (_, _) => MarkDraftDirty();
        _defaultClickIntervalInput.ValueChanged += (_, _) => MarkDraftDirty();
        _defaultAfterDelayInput.ValueChanged += (_, _) => MarkDraftDirty();
        _cloudDesktopEnabledCheckBox.CheckedChanged += (_, _) => MarkDraftDirty();
        _waitForStableScreenCheckBox.CheckedChanged += (_, _) => MarkDraftDirty();
        _stabilityTimeoutInput.ValueChanged += (_, _) => MarkDraftDirty();
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

    private void ConfigureDraftTimer()
    {
        if (_draftService is null)
        {
            return;
        }

        _draftTimer.Interval = 800;
        _draftTimer.Tick += (_, _) => SaveDraftIfChanged();
        _draftTimer.Start();
    }

    private void ConfigureExecutionProgressTimer()
    {
        _executionProgressTimer.Interval = 100;
        _executionProgressTimer.Tick += (_, _) =>
        {
            if (_latestExecutionProgress.TryConsume(out var progress))
            {
                UpdateExecutionProgress(progress!);
            }
        };
        _executionProgressTimer.Start();
    }

    private void SaveDraftIfChanged()
    {
        if (_draftService is null
            || !_draftDirty
            || _executionState != ExecutionState.Idle
            || _pointGrid.IsCurrentCellInEditMode)
        {
            return;
        }

        try
        {
            var snapshot = CreateProfileSnapshot();
            _draftService.Save(new DraftEnvelope(
                snapshot,
                _currentProfilePath,
                GetSourceUpdatedAtUtc(),
                DateTime.UtcNow,
                _currentProfileFileHash));
            _lastDraftSnapshot = ProfileService.CloneProfile(snapshot);
            _draftDirty = false;
        }
        catch (Exception exception)
        {
            RecordDiagnostic("draft.save", exception);
            SetStatus("自动草稿保存失败，详情已写入诊断日志。");
        }
    }

    private void MarkDraftDirty()
    {
        if (!_suppressProfileSelection)
        {
            _draftDirty = true;
        }
    }

    private void FlushDraft()
    {
        if (_draftService is null)
        {
            return;
        }

        try
        {
            if (HasUnsavedProfileChanges())
            {
                var snapshot = CreateProfileSnapshot();
                _draftService.Save(new DraftEnvelope(
                    snapshot,
                    _currentProfilePath,
                    GetSourceUpdatedAtUtc(),
                    DateTime.UtcNow,
                    _currentProfileFileHash));
            }
            else
            {
                _draftService.Discard();
            }
        }
        catch (Exception exception)
        {
            RecordDiagnostic("draft.flush", exception);
        }
    }

    private DateTime? GetSourceUpdatedAtUtc()
    {
        return _currentProfilePath is not null && File.Exists(_currentProfilePath)
            ? File.GetLastWriteTimeUtc(_currentProfilePath)
            : null;
    }

    private void RecoverDraftIfAvailable()
    {
        if (_draftService?.Exists != true)
        {
            return;
        }

        try
        {
            var draft = _draftService.Load();
            if (draft is null)
            {
                return;
            }

            var sourceChanged = DraftService.HasSourceChanged(draft);
            var sourceWarning = sourceChanged
                ? "\n\n原方案已被修改或删除。恢复后将要求另存，避免覆盖较新的文件。"
                : string.Empty;
            var choice = MessageBox.Show(
                this,
                $"发现 {draft.DraftUpdatedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss} 保存的未完成工作，是否恢复？"
                + sourceWarning,
                "恢复自动草稿",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button1);
            if (choice != DialogResult.Yes)
            {
                _draftService.Discard();
                return;
            }

            ApplyProfile(draft.Profile);
            _currentProfilePath = sourceChanged ? null : draft.SourcePath;
            _currentProfileFileHash =
                !sourceChanged && draft.SourcePath is not null
                    ? draft.SourceSha256
                    : null;
            _saveImportedProfileAsCopy = sourceChanged;
            _importedProfileSourcePath = sourceChanged ? draft.SourcePath : null;
            _baselineProfile = null;
            _lastDraftSnapshot = ProfileService.CloneProfile(draft.Profile);
            ClearExecutionCheckpoint();
            SetStatus(
                sourceChanged
                    ? "已恢复自动草稿；原方案已变化，请另存为新方案。"
                    : "已恢复自动草稿，请确认后保存。");
        }
        catch (Exception exception)
        {
            RecordDiagnostic("draft.recover", exception);
            string? brokenPath = null;
            try
            {
                brokenPath = _draftService?.QuarantineBrokenDraft();
            }
            catch (Exception quarantineException)
            {
                RecordDiagnostic("draft.quarantine", quarantineException);
            }

            ShowError(
                string.IsNullOrWhiteSpace(brokenPath)
                    ? "自动草稿无法恢复，详情已写入诊断日志。"
                    : $"自动草稿无法恢复，已隔离到：{brokenPath}");
        }
    }

    private void RecordDiagnostic(string operation, Exception exception)
    {
        try
        {
            _diagnosticLogService?.Write(operation, exception);
        }
        catch
        {
            // Diagnostics must never replace the original user-facing error.
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
                case ComboBox:
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
        ApplyNeutralButtonTheme(_openProfilesDirectoryButton);
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
        ApplyAccentButtonTheme(_openProfilesDirectoryButton, theme.CaptureAccent);
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

        CancelDelayedCapture();
        if (_captureMode is CaptureMode.CloudRegionTopLeft
            or CaptureMode.CloudRegionBottomRight)
        {
            _captureMode = CaptureMode.Idle;
            _cloudRegionTopLeft = null;
            CloseCaptureHud();
        }
        else if (_captureMode == CaptureMode.PointCapture)
        {
            var added = _points.Count - _captureSessionStartCount;
            _captureMode = CaptureMode.Idle;
            CloseCaptureHud();
            SetStatus($"采点已结束，本次新增 {added} 个，共 {_points.Count} 个点位。");
        }
        else
        {
            _captureSessionStartCount = _points.Count;
            _captureMode = CaptureMode.PointCapture;
            ShowCaptureHud($"移动鼠标后按 {CaptureHotKeyText} 或点击“记录当前位置”。");
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
            var nearbyIndex = CaptureWorkflowService.FindNearbyPoint(
                _points,
                captured.X,
                captured.Y);
            if (nearbyIndex >= 0
                && MessageBox.Show(
                    this,
                    $"当前位置接近点位 {nearbyIndex + 1}，仍要添加吗？",
                    "可能重复的点位",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

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
            PushPointUndo();
            _points.Add(point);
            UpdateCaptureEmptyState();
            _captureHud?.UpdateStatus(
                $"最近记录：({point.X}, {point.Y})",
                _points.Count);
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
        PushPointUndo();
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

        PushPointUndo();
        _points.RemoveAt(index);
        _pointGrid.Refresh();
        UpdateCaptureEmptyState();
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
            PushPointUndo();
            _points.Clear();
            _pointGrid.Refresh();
            UpdateCaptureEmptyState();
            SetStatus("已清空全部点位。");
        }
    }

    private async Task RecordCurrentPositionWithDelayAsync()
    {
        if (_executionState != ExecutionState.Idle)
        {
            return;
        }

        if (_captureMode == CaptureMode.Idle)
        {
            _captureSessionStartCount = _points.Count;
            _captureMode = CaptureMode.PointCapture;
            UpdateCaptureButton();
            ShowCaptureHud("2 秒后记录，请移动鼠标到目标位置。");
        }

        var version = Interlocked.Increment(ref _captureDelayVersion);
        _captureNowButton.Enabled = false;
        _captureNowButton.Text = "等待记录…";
        SetStatus("2 秒后记录当前位置，请移动鼠标到目标位置。");
        await _captureDelay.RunAsync(
            TimeSpan.FromSeconds(2),
            _ =>
            {
                CaptureCurrentPoint();
                return Task.CompletedTask;
            });
        if (version == _captureDelayVersion && !IsDisposed)
        {
            _captureNowButton.Text = "2 秒后记录";
            _captureNowButton.Enabled = _executionState == ExecutionState.Idle;
        }
    }

    private void CancelDelayedCapture()
    {
        Interlocked.Increment(ref _captureDelayVersion);
        _captureDelay.Cancel();
        if (!IsDisposed)
        {
            _captureNowButton.Text = "2 秒后记录";
            _captureNowButton.Enabled = _executionState == ExecutionState.Idle;
        }
    }

    private void PushPointUndo()
    {
        _pointUndoStack.Push(_points.Select(ClonePoint).ToList());
        if (_pointUndoStack.Count > PointUndoHistoryLimit)
        {
            var retained = _pointUndoStack
                .Take(PointUndoHistoryLimit)
                .Reverse()
                .ToArray();
            _pointUndoStack.Clear();
            foreach (var snapshot in retained)
            {
                _pointUndoStack.Push(snapshot);
            }
        }

        _undoButton.Enabled = true;
    }

    private void UndoPointChange()
    {
        if (_pointUndoStack.Count == 0)
        {
            return;
        }

        _points = new BindingList<ClickPoint>(
            _pointUndoStack.Pop().Select(ClonePoint).ToList());
        SubscribePointChanges();
        _pointBindingSource.DataSource = _points;
        _pointGrid.Refresh();
        _undoButton.Enabled = _pointUndoStack.Count > 0;
        UpdateCaptureEmptyState();
        SetStatus("已撤销上一次删除或清空。");
    }

    private void ShowCaptureHud(string message)
    {
        _captureHud ??= new CaptureHudForm(_theme);
        _captureHud.RecordRequested -= RequestDelayedCapture;
        _captureHud.RecordRequested += RequestDelayedCapture;
        _captureHud.FinishRequested -= ToggleCaptureMode;
        _captureHud.FinishRequested += ToggleCaptureMode;
        _captureHud.UpdateStatus(message, _points.Count);
        if (!_captureHud.Visible)
        {
            _captureHud.Show(this);
        }
    }

    private void RequestDelayedCapture()
    {
        _ = RecordCurrentPositionWithDelayAsync();
    }

    private void CloseCaptureHud()
    {
        _captureHud?.Close();
        _captureHud = null;
    }

    private void ApplyClickIntervalToAll()
    {
        CommitGridChanges();
        var value = decimal.ToInt32(_defaultClickIntervalInput.Value);
        PointTimingService.ApplyClickInterval(_points, value);
        _pointGrid.Refresh();
        MarkDraftDirty();
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
        MarkDraftDirty();
        SetStatus(
            _points.Count == 0
                ? $"新采集点的点后等待已设为 {value} ms。"
                : $"已将 {_points.Count} 个点位的点后等待设为 {value} ms。");
    }

    private bool SaveProfile()
    {
        if (_saveImportedProfileAsCopy)
        {
            return SaveProfileAs();
        }

        try
        {
            CommitGridChanges();
            var profile = CreateProfileSnapshot();
            var path = _currentProfilePath is null
                ? _profileService.Save(profile)
                : _profileService.Save(
                    profile,
                    _currentProfilePath,
                    new ProfileSaveOptions(
                        CreateBackup: true,
                        AllowOverwrite: true,
                        ExpectedExistingSha256: _currentProfileFileHash));
            _currentProfilePath = path;
            _currentProfileFileHash = ProfileService.ComputeFileSha256(path);
            TrackProfileIdentity(profile);
            _saveImportedProfileAsCopy = false;
            _importedProfileSourcePath = null;
            UpdateProfileBaseline(profile);
            _lastDraftSnapshot = ProfileService.CloneProfile(profile);
            _draftService?.Discard();
            RefreshProfileDirectory();
            SetStatus($"配置已保存：{path}");
            return true;
        }
        catch (ProfileConflictException exception)
        {
            RecordDiagnostic("profile.save-conflict", exception);
            MessageBox.Show(
                this,
                exception.Message,
                "方案保存冲突",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return SaveProfileAs();
        }
        catch (Exception exception)
        {
            RecordDiagnostic("profile.save", exception);
            ShowError($"保存配置失败：{exception.Message}");
            return false;
        }
    }

    private bool SaveProfileAs()
    {
        var profileName = string.IsNullOrWhiteSpace(_profileSelector.Text)
            ? "默认方案"
            : _profileSelector.Text.Trim();
        var importCopyPath =
            _saveImportedProfileAsCopy && _importedProfileSourcePath is not null
                ? _profileService.GetAvailableImportCopyPath(
                    profileName,
                    _importedProfileSourcePath)
                : null;
        using var dialog = new SaveFileDialog
        {
            Title = "另存连点器配置",
            Filter = "有序连点器方案 (*.oclick)|*.oclick",
            InitialDirectory = importCopyPath is not null
                ? Path.GetDirectoryName(importCopyPath)
                : _currentProfilePath is null
                    ? _profileService.ProfilesDirectory
                    : Path.GetDirectoryName(_currentProfilePath),
            FileName = importCopyPath is not null
                ? Path.GetFileName(importCopyPath)
                : $"{profileName}.oclick",
            AddExtension = true,
            DefaultExt = "oclick",
            OverwritePrompt = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return false;
        }

        if (_importedProfileSourcePath is not null
            && ProfileService.PathsEqual(dialog.FileName, _importedProfileSourcePath))
        {
            ShowError("导入副本不能覆盖来源文件，请选择其他文件名。");
            return false;
        }

        try
        {
            CommitGridChanges();
            var profile = CreateProfileSnapshot();
            var path = _profileService.Save(
                profile,
                dialog.FileName,
                new ProfileSaveOptions(
                    CreateBackup: File.Exists(dialog.FileName),
                    AllowOverwrite: true,
                    RenewIdentity: true));
            _currentProfilePath = path;
            _currentProfileFileHash = ProfileService.ComputeFileSha256(path);
            TrackProfileIdentity(profile);
            _saveImportedProfileAsCopy = false;
            _importedProfileSourcePath = null;
            UpdateProfileBaseline(profile);
            _lastDraftSnapshot = ProfileService.CloneProfile(profile);
            _draftService?.Discard();
            RefreshProfileDirectory();
            SetStatus($"配置已另存为：{path}");
            return true;
        }
        catch (Exception exception)
        {
            RecordDiagnostic("profile.save-as", exception);
            ShowError($"另存配置失败：{exception.Message}");
            return false;
        }
    }

    private void ImportProfile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "导入连点器方案副本",
            Filter = "有序连点器方案 (*.oclick)|*.oclick|所有文件 (*.*)|*.*",
            InitialDirectory = _profileService.ProfilesDirectory,
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var result = _legacyMigrationService.Migrate(dialog.FileName);
        if (!result.Success)
        {
            ShowError(result.Error ?? "旧方案迁移失败。");
            return;
        }

        var profile = result.Profile!;
        var enabledPointCount = profile.Points.Count(point => point.Enabled);
        var warningText = result.Warnings.Count == 0
            ? "未发现需要人工处理的迁移项。"
            : string.Join(Environment.NewLine, result.Warnings.Select(item => $"• {item}"));
        var confirmation = MessageBox.Show(
            this,
            $"""
            方案名称：{profile.Name}
            点位总数：{profile.Points.Count}
            启用点位：{enabledPointCount}
            总循环次数：{profile.TotalLoops}

            {warningText}

            迁移结果将作为未保存的新方案，不会修改来源文件。
            是否继续？
            """,
            "确认迁移旧方案",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button1);
        if (confirmation != DialogResult.Yes)
        {
            SetStatus("已取消迁移旧方案。");
            return;
        }

        if (!ConfirmSaveBeforeProfileSwitch())
        {
            SetStatus("已取消迁移旧方案。");
            return;
        }

        ApplyImportedProfile(profile, dialog.FileName);
        ClearExecutionCheckpoint();
        SetStatus($"旧方案已迁移，请另存为 .oclick：{dialog.FileName}");
    }

    private void OpenExternalProfile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "打开有序连点器方案",
            Filter = "有序连点器方案 (*.oclick)|*.oclick",
            InitialDirectory = _currentProfilePath is null
                ? _profileService.ProfilesDirectory
                : Path.GetDirectoryName(_currentProfilePath),
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        if (!ConfirmSaveBeforeProfileSwitch())
        {
            SetStatus("已取消打开方案。");
            return;
        }

        var result = _profileService.Import(dialog.FileName);
        if (!result.Success)
        {
            ShowError(result.ErrorMessage ?? "方案打开失败。");
            return;
        }

        ApplyLocalProfile(result.Profile!, dialog.FileName);
        _lastDraftSnapshot = ProfileService.CloneProfile(result.Profile!);
        _draftService?.Discard();
        ClearExecutionCheckpoint();
        SetStatus($"已打开方案：{dialog.FileName}");
    }

    private void ApplyImportedProfile(ClickProfile profile, string sourcePath)
    {
        ApplyProfile(profile);
        _currentProfilePath = null;
        _currentProfileFileHash = null;
        _saveImportedProfileAsCopy = true;
        _importedProfileSourcePath = Path.GetFullPath(sourcePath);
        UpdateProfileBaseline();
    }

    private void ExportProfile()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "导出连点器方案",
            Filter = "有序连点器方案 (*.oclick)|*.oclick",
            InitialDirectory = _currentProfilePath is null
                ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                : Path.GetDirectoryName(_currentProfilePath),
            FileName = $"{_profileSelector.Text.Trim()}.oclick",
            AddExtension = true,
            DefaultExt = "oclick",
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
            RecordDiagnostic("profile.export", exception);
            ShowError($"导出方案失败：{exception.Message}");
        }
    }

    private ClickProfile CreateProfileSnapshot()
    {
        CommitGridChanges();
        return new ClickProfile
        {
            FormatVersion = 4,
            ProfileId = _profileId,
            CreatedAtUtc = _profileCreatedAtUtc,
            UpdatedAtUtc = _profileUpdatedAtUtc,
            Name = string.IsNullOrWhiteSpace(_profileSelector.Text)
                ? "默认方案"
                : _profileSelector.Text.Trim(),
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
        TrackProfileIdentity(profile);
        _profileSelector.Text = profile.Name;
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
        SubscribePointChanges();
        _pointBindingSource.DataSource = _points;
        ApplyCloudDesktopProfile(profile);
        _pointGrid.Refresh();
        UpdateCaptureEmptyState();
        _draftDirty = false;
    }

    private void SubscribePointChanges()
    {
        _points.ListChanged -= PointsOnListChanged;
        _points.ListChanged += PointsOnListChanged;
    }

    private void PointsOnListChanged(object? sender, ListChangedEventArgs eventArgs)
    {
        MarkDraftDirty();
    }

    private void TrackProfileIdentity(ClickProfile profile)
    {
        _profileId = profile.ProfileId == Guid.Empty
            ? Guid.NewGuid()
            : profile.ProfileId;
        _profileCreatedAtUtc = profile.CreatedAtUtc == default
            ? DateTime.UtcNow
            : profile.CreatedAtUtc;
        _profileUpdatedAtUtc = profile.UpdatedAtUtc == default
            ? _profileCreatedAtUtc
            : profile.UpdatedAtUtc;
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
        _workspaceStatusLabel.Text = message;
        _runStatusForm?.UpdateProgress(message);
    }

    private void OpenSupportDirectory(string path, string operation)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            RecordDiagnostic(operation, exception);
            ShowError($"打开目录失败：{exception.Message}");
        }
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
