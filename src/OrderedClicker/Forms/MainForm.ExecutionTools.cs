using OrderedClicker.Core;
using OrderedClicker.Models;
using OrderedClicker.Services;

namespace OrderedClicker.Forms;

public sealed partial class MainForm
{
    private readonly Button _rehearsalButton = new();
    private readonly Button _stepButton = new();
    private readonly Button _bindTargetWindowButton = new();
    private readonly Button _clearTargetWindowButton = new();
    private readonly Label _targetWindowStatusLabel = new();
    private readonly Button _captureStabilityRegionButton = new();
    private readonly Button _previewStabilityRegionButton = new();
    private readonly Button _clearStabilityRegionButton = new();
    private readonly Label _stabilityRegionStatusLabel = new();
    private TargetWindowBinding? _targetWindow;
    private ScreenBounds? _stabilityRegion;
    private Point? _stabilityRegionTopLeft;
    private ScreenStabilitySettings _stabilitySettings = new();
    private CancellationTokenSource? _windowBindingCancellation;
    private ExecutionRunMode _activeRunMode;
    private int _executionEnvironmentChanged;

    private FlowLayoutPanel CreateModeOptionsRow() => new()
    {
        Dock = DockStyle.Fill,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        WrapContents = true,
        Margin = new Padding(6, 4, 6, 8),
        Padding = new Padding(0, 4, 0, 4),
        BackColor = _theme.Window
    };

    private void ConfigureExecutionTools(FlowLayoutPanel panel)
    {
        _rehearsalButton.Name = "RehearsalButton";
        _rehearsalButton.Text = "演练";
        ConfigureCommandButton(_rehearsalButton, 90);
        _stepButton.Name = "StepExecutionButton";
        _stepButton.Text = "单步执行";
        ConfigureCommandButton(_stepButton, 110);
        panel.Controls.Add(_rehearsalButton);
        panel.Controls.Add(_stepButton);
        _toolTip.SetToolTip(_rehearsalButton, "只移动鼠标，不点击，不改动正式执行断点。");
        _toolTip.SetToolTip(_stepButton, "完成一个点位的点击及等待后暂停，继续时执行下一个点位。");
        _rehearsalButton.Click += async (_, _) =>
            await StartExecutionAsync(ExecutionRunMode.Rehearsal);
        _stepButton.Click += async (_, _) =>
            await StartExecutionAsync(ExecutionRunMode.Step);
    }

    private void BuildSafetyOptions(TableLayoutPanel panel)
    {
        var region = CreateModeOptionsRow();
        _captureStabilityRegionButton.Name = "CaptureStabilityRegionButton";
        _captureStabilityRegionButton.Text = "选择检测区域";
        ConfigureCommandButton(_captureStabilityRegionButton, 132);
        _previewStabilityRegionButton.Name = "PreviewStabilityRegionButton";
        _previewStabilityRegionButton.Text = "预览";
        ConfigureCommandButton(_previewStabilityRegionButton, 68);
        _clearStabilityRegionButton.Name = "ClearStabilityRegionButton";
        _clearStabilityRegionButton.Text = "清除区域";
        ConfigureCommandButton(_clearStabilityRegionButton, 94);
        _stabilityRegionStatusLabel.Name = "StabilityRegionStatusLabel";
        _stabilityRegionStatusLabel.AutoSize = true;
        _stabilityRegionStatusLabel.MaximumSize = new Size(490, 0);
        _stabilityRegionStatusLabel.Margin = new Padding(4, 9, 0, 0);
        region.Controls.AddRange([
            _captureStabilityRegionButton, _previewStabilityRegionButton,
            _clearStabilityRegionButton, _stabilityRegionStatusLabel]);
        _captureStabilityRegionButton.Click += (_, _) => BeginStabilityRegionCapture();
        _previewStabilityRegionButton.Click += (_, _) =>
        {
            if (_stabilityRegion is { } bounds)
            {
                ShowCloudRegionPreview(new CloudDesktopRegion
                {
                    X = bounds.X, Y = bounds.Y, Width = bounds.Width, Height = bounds.Height
                });
            }
        };
        _clearStabilityRegionButton.Click += (_, _) =>
        {
            _stabilityRegion = null;
            MarkDraftDirty();
            UpdateSafetyOptionsControls(true);
        };
        var window = CreateModeOptionsRow();
        _bindTargetWindowButton.Name = "BindTargetWindowButton";
        _bindTargetWindowButton.Text = "绑定目标窗口";
        ConfigureCommandButton(_bindTargetWindowButton, 140);
        _clearTargetWindowButton.Name = "ClearTargetWindowButton";
        _clearTargetWindowButton.Text = "解除绑定";
        ConfigureCommandButton(_clearTargetWindowButton, 94);
        _targetWindowStatusLabel.Name = "TargetWindowStatusLabel";
        _targetWindowStatusLabel.AutoSize = true;
        _targetWindowStatusLabel.MaximumSize = new Size(490, 0);
        _targetWindowStatusLabel.Margin = new Padding(4, 9, 0, 0);
        window.Controls.AddRange([
            _bindTargetWindowButton, _clearTargetWindowButton, _targetWindowStatusLabel]);
        _bindTargetWindowButton.Click += async (_, _) => await BindTargetWindowAsync();
        _clearTargetWindowButton.Click += (_, _) =>
        {
            _windowBindingCancellation?.Cancel();
            _targetWindow = null;
            MarkDraftDirty();
            UpdateSafetyOptionsControls(true);
        };
        _toolTip.SetToolTip(_bindTargetWindowButton, "3 秒后记录前台窗口，请先切换到目标程序。");
        _toolTip.SetToolTip(_captureStabilityRegionButton, "使用采点热键依次记录检测范围的左上角和右下角。");
        panel.Controls.Add(region, 0, 2);
        panel.Controls.Add(window, 0, 3);
    }

    private async Task BindTargetWindowAsync()
    {
        if (_windowBindingCancellation is not null)
        {
            _windowBindingCancellation.Cancel();
            return;
        }
        if (_executionState != ExecutionState.Idle)
        {
            return;
        }

        CancelDelayedCapture();
        _captureMode = CaptureMode.Idle;
        CloseCaptureHud();
        UpdateCaptureButton();
        using var cancellation = new CancellationTokenSource();
        _windowBindingCancellation = cancellation;
        try
        {
            for (var seconds = 3; seconds > 0; seconds--)
            {
                _bindTargetWindowButton.Text = $"取消绑定 ({seconds})";
                SetStatus($"{seconds} 秒后记录目标窗口，请切换到需要操作的程序。");
                await Task.Delay(1000, cancellation.Token);
            }
            _targetWindow = WindowsExecutionEnvironment.CaptureTarget();
            MarkDraftDirty();
            SetStatus($"已绑定窗口：{_targetWindow.WindowTitle}");
        }
        catch (OperationCanceledException)
        {
            SetStatus("已取消绑定窗口。");
        }
        catch (Exception exception)
        {
            RecordDiagnostic("execution.window.capture", exception);
            SetStatus(exception.Message);
        }
        finally
        {
            _windowBindingCancellation = null;
            if (!IsDisposed)
            {
                _bindTargetWindowButton.Text = "绑定目标窗口";
                UpdateSafetyOptionsControls(_executionState == ExecutionState.Idle);
            }
        }
    }

    private void BeginStabilityRegionCapture()
    {
        if (_executionState != ExecutionState.Idle)
        {
            return;
        }
        CancelDelayedCapture();
        _captureMode = CaptureMode.StabilityRegionTopLeft;
        _stabilityRegionTopLeft = null;
        UpdateCaptureButton();
        ShowCaptureHud($"将鼠标移到检测区域左上角，按 {CaptureHotKeyText} 记录。");
        SetStatus("记录检测区域左上角后，再记录右下角。");
    }

    private void CaptureStabilityRegionCorner()
    {
        var captured = _monitorService.CaptureCursor();
        if (_captureMode == CaptureMode.StabilityRegionTopLeft
            || _stabilityRegionTopLeft is null)
        {
            _stabilityRegionTopLeft = new Point(captured.X, captured.Y);
            _captureMode = CaptureMode.StabilityRegionBottomRight;
            _captureHud?.UpdateStatus("请记录检测区域右下角。", _points.Count);
            UpdateCaptureButton();
            return;
        }

        var topLeft = _stabilityRegionTopLeft.Value;
        var width = (long)captured.X - topLeft.X;
        var height = (long)captured.Y - topLeft.Y;
        var screen = _monitorService.GetVirtualScreenBounds();
        if (width < 8 || height < 8 || width > int.MaxValue || height > int.MaxValue
            || !screen.Contains(topLeft.X, topLeft.Y)
            || !screen.Contains(captured.X - 1, captured.Y - 1))
        {
            SetStatus("检测区域无效，请重新记录右下角；宽高至少 8 像素且位于当前桌面内。");
            return;
        }

        _stabilityRegion = new ScreenBounds(topLeft.X, topLeft.Y, (int)width, (int)height);
        _waitForStableScreenCheckBox.Checked = true;
        _captureMode = CaptureMode.Idle;
        _stabilityRegionTopLeft = null;
        CloseCaptureHud();
        UpdateCaptureButton();
        UpdateSafetyOptionsControls(true);
        MarkDraftDirty();
        _previewStabilityRegionButton.PerformClick();
        SetStatus($"检测区域已设置：{width} × {height}");
    }

    private void UpdateSafetyOptionsControls(bool enabled)
    {
        _bindTargetWindowButton.Enabled = enabled;
        _clearTargetWindowButton.Enabled = enabled && _targetWindow is not null;
        _captureStabilityRegionButton.Enabled = enabled;
        _previewStabilityRegionButton.Enabled = enabled && _stabilityRegion is not null;
        _clearStabilityRegionButton.Enabled = enabled && _stabilityRegion is not null;
        _targetWindowStatusLabel.Text = _targetWindow is null
            ? "未绑定"
            : $"{_targetWindow.Platform}：{_targetWindow.WindowTitle}";
        _stabilityRegionStatusLabel.Text = _stabilityRegion is { } bounds
            ? $"({bounds.X}, {bounds.Y}) {bounds.Width} × {bounds.Height}"
            : "默认检测范围";
    }

    private void ApplyExecutionToolsProfile(ClickProfile profile)
    {
        _targetWindow = profile.TargetWindow is null ? null : new TargetWindowBinding
        {
            Platform = profile.TargetWindow.Platform,
            ApplicationId = profile.TargetWindow.ApplicationId,
            WindowTitle = profile.TargetWindow.WindowTitle
        };
        _stabilitySettings = profile.ScreenStability;
        _stabilityRegion = profile.ScreenStability.Region;
        UpdateSafetyOptionsControls(true);
    }

    private void ApplyExecutionToolsTheme()
    {
        foreach (var button in new[]
        {
            _rehearsalButton, _stepButton, _bindTargetWindowButton,
            _clearTargetWindowButton, _captureStabilityRegionButton,
            _previewStabilityRegionButton, _clearStabilityRegionButton
        })
        {
            ApplyNeutralButtonTheme(button);
        }
    }

    private string? CheckExecutionSafety(
        ExecutionPlan plan, ExecutionPlanPoint point, string displaySignature)
    {
        if (Volatile.Read(ref _executionEnvironmentChanged) != 0
            || plan.VirtualScreen != _monitorService.GetVirtualScreenBounds()
            || !string.Equals(displaySignature,
                WindowsExecutionEnvironment.CaptureDisplaySignature(_monitorService),
                StringComparison.Ordinal))
        {
            return "显示器环境已变化，请停止并重新检查点位";
        }
        if (!WindowsExecutionEnvironment.IsInputDesktopAvailable())
        {
            return "当前桌面不可操作，请解锁并确认目标窗口";
        }
        return WindowsExecutionEnvironment.CheckTarget(plan.TargetWindow, point);
    }

    private void ReconcileExecutionCheckpoint()
    {
        if (_pendingExecutionPlan is not null
            && !string.Equals(_pendingExecutionPlan.ProfileFingerprint,
                ExecutionPlanFingerprintService.Create(CreateProfileSnapshot()),
                StringComparison.Ordinal))
        {
            ClearExecutionCheckpoint();
        }
    }
}
