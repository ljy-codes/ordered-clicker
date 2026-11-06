using OrderedClicker.Core;
using OrderedClicker.Models;

namespace OrderedClicker.Forms;

public sealed partial class MainForm
{
    private Control BuildCloudDesktopPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0, 7, 0, 7),
            BackColor = _theme.Window
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var cloud = CreateModeOptionsRow();
        var stability = CreateModeOptionsRow();

        _cloudDesktopEnabledCheckBox.Name = "CloudDesktopEnabledCheckBox";
        _cloudDesktopEnabledCheckBox.Text = "云桌面增强";
        _cloudDesktopEnabledCheckBox.AutoSize = true;
        _cloudDesktopEnabledCheckBox.Margin = new Padding(0, 8, 10, 0);

        _calibrateRegionButton.Name = "CalibrateRegionButton";
        _calibrateRegionButton.Text = "校准云桌面区域";
        ConfigureCommandButton(_calibrateRegionButton, 142);

        _cloudRegionStatusLabel.Name = "CloudRegionStatusLabel";
        _cloudRegionStatusLabel.AutoSize = true;
        _cloudRegionStatusLabel.Text = "未校准";
        _cloudRegionStatusLabel.Margin = new Padding(4, 9, 18, 0);

        _waitForStableScreenCheckBox.Name = "WaitForStableScreenCheckBox";
        _waitForStableScreenCheckBox.Text = "等待画面稳定";
        _waitForStableScreenCheckBox.AutoSize = true;
        _waitForStableScreenCheckBox.Margin = new Padding(0, 8, 8, 0);

        _stabilityTimeoutInput.Name = "StabilityTimeoutInput";
        _stabilityTimeoutInput.Minimum = 3;
        _stabilityTimeoutInput.Maximum = 60;
        _stabilityTimeoutInput.DecimalPlaces = 3;
        _stabilityTimeoutInput.Increment = 0.25m;
        _stabilityTimeoutInput.Value = 15;
        _stabilityTimeoutInput.Width = 66;
        ConfigureInput(_stabilityTimeoutInput);

        cloud.Controls.Add(_cloudDesktopEnabledCheckBox);
        cloud.Controls.Add(_calibrateRegionButton);
        cloud.Controls.Add(_cloudRegionStatusLabel);
        stability.Controls.Add(_waitForStableScreenCheckBox);
        stability.Controls.Add(CreateFieldLabel("超时(秒)"));
        stability.Controls.Add(_stabilityTimeoutInput);
        panel.Controls.Add(cloud, 0, 0);
        panel.Controls.Add(stability, 0, 1);
        BuildSafetyOptions(panel);

        _cloudDesktopEnabledCheckBox.CheckedChanged += (_, _) =>
        {
            UpdateCloudDesktopControls();
        };
        _calibrateRegionButton.Click += (_, _) => BeginCloudRegionCalibration();
        _waitForStableScreenCheckBox.CheckedChanged += (_, _) =>
        {
            UpdateCloudDesktopControls();
        };

        UpdateCloudDesktopControls();
        return panel;
    }

    private void BeginCloudRegionCalibration()
    {
        if (_executionState != ExecutionState.Idle)
        {
            return;
        }

        CancelDelayedCapture();
        _cloudDesktopEnabledCheckBox.Checked = true;
        _captureMode = CaptureMode.CloudRegionTopLeft;
        _cloudRegionTopLeft = null;
        UpdateCaptureButton();
        ShowCaptureHud(
            $"将鼠标移到云桌面左上角，按 {CaptureHotKeyText} 或点“记录当前位置”。");
        SetStatus(
            $"将鼠标移到云桌面画面的左上角，按 {CaptureHotKeyText}；"
            + "然后再记录右下角。");
    }

    private void CaptureCloudRegionCorner()
    {
        var captured = _monitorService.CaptureCursor();
        if (_captureMode == CaptureMode.CloudRegionTopLeft)
        {
            _cloudRegionTopLeft = new Point(captured.X, captured.Y);
            _captureMode = CaptureMode.CloudRegionBottomRight;
            UpdateCaptureButton();
            _captureHud?.UpdateStatus(
                "左上角已记录，请记录右下角。",
                _points.Count);
            SetStatus(
                $"左上角已记录 ({captured.X}, {captured.Y})，"
                + $"请移动到右下角并按 {CaptureHotKeyText}。");
            return;
        }

        if (_cloudRegionTopLeft is null)
        {
            _captureMode = CaptureMode.CloudRegionTopLeft;
            return;
        }

        var topLeft = _cloudRegionTopLeft.Value;
        var width = captured.X - topLeft.X;
        var height = captured.Y - topLeft.Y;
        var region = new CloudDesktopRegion
        {
            X = topLeft.X,
            Y = topLeft.Y,
            Width = width,
            Height = height,
            MonitorDeviceName = captured.MonitorDeviceName,
            CapturedDpi = captured.Dpi
        };

        try
        {
            CloudDesktopCoordinateService.ValidateRegion(region);
        }
        catch (ArgumentOutOfRangeException)
        {
            _captureMode = CaptureMode.CloudRegionTopLeft;
            _cloudRegionTopLeft = null;
            UpdateCaptureButton();
            ShowError("校准区域无效。请先记录左上角，再记录右下角，宽高至少 100 像素。");
            return;
        }

        _cloudDesktopRegion = region;
        var filledPointCount =
            CloudDesktopCoordinateService.FillMissingRelativeCoordinates(_points, region);
        MarkDraftDirty();
        _captureMode = CaptureMode.Idle;
        _cloudRegionTopLeft = null;
        CloseCaptureHud();
        UpdateCloudDesktopControls();
        UpdateCaptureButton();
        ClearExecutionCheckpoint();
        SetStatus(
            $"云桌面区域已校准：{region.Width} × {region.Height}，"
            + $"补充了 {filledPointCount} 个缺失相对坐标，已有相对坐标保持不变。");
        ShowCloudRegionPreview(region);
    }

    private void ShowCloudRegionPreview(CloudDesktopRegion region)
    {
        var overlay = new CloudRegionOverlayForm(region, _theme);
        var timer = new System.Windows.Forms.Timer { Interval = 1200 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            timer.Dispose();
            overlay.Close();
            overlay.Dispose();
        };
        overlay.Show(this);
        timer.Start();
    }

    private void UpdateCloudDesktopControls()
    {
        var enabled = _cloudDesktopEnabledCheckBox.Checked;
        _calibrateRegionButton.Enabled = enabled
            && _executionState == ExecutionState.Idle;
        _waitForStableScreenCheckBox.Enabled = _executionState == ExecutionState.Idle;
        _stabilityTimeoutInput.Enabled =
            _executionState == ExecutionState.Idle && _waitForStableScreenCheckBox.Checked;
        UpdateSafetyOptionsControls(_executionState == ExecutionState.Idle);
        _cloudRegionStatusLabel.Text = _cloudDesktopRegion is null
            ? "未校准"
            : $"已校准 {_cloudDesktopRegion.Width}×{_cloudDesktopRegion.Height}";
        _cloudRegionStatusLabel.ForeColor = _cloudDesktopRegion is null
            ? _theme.WarningAccent
            : _theme.CaptureAccent;
    }

    private void ApplyCloudDesktopProfile(ClickProfile profile)
    {
        _cloudDesktopRegion = profile.CloudDesktopRegion is null
            ? null
            : new CloudDesktopRegion
            {
                X = profile.CloudDesktopRegion.X,
                Y = profile.CloudDesktopRegion.Y,
                Width = profile.CloudDesktopRegion.Width,
                Height = profile.CloudDesktopRegion.Height,
                MonitorDeviceName = profile.CloudDesktopRegion.MonitorDeviceName,
                CapturedDpi = profile.CloudDesktopRegion.CapturedDpi
            };
        _cloudDesktopEnabledCheckBox.Checked =
            profile.CoordinateMode == CoordinateMode.CloudDesktopRegion;
        _waitForStableScreenCheckBox.Checked = profile.ScreenStability.Enabled;
        var timeoutSeconds = Math.Max(0.001m, profile.ScreenStability.TimeoutMs / 1000m);
        _stabilityTimeoutInput.Minimum = Math.Min(3m, timeoutSeconds);
        _stabilityTimeoutInput.Maximum = Math.Max(60m, timeoutSeconds);
        _stabilityTimeoutInput.Value = timeoutSeconds;
        UpdateCloudDesktopControls();
    }
}
