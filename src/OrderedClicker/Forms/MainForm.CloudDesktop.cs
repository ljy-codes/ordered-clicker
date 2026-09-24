using OrderedClicker.Core;
using OrderedClicker.Models;

namespace OrderedClicker.Forms;

public sealed partial class MainForm
{
    private Control BuildCloudDesktopPanel()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(0, 7, 0, 7),
            BackColor = _theme.Window
        };

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
        _stabilityTimeoutInput.Value = 15;
        _stabilityTimeoutInput.Width = 66;
        ConfigureInput(_stabilityTimeoutInput);

        panel.Controls.Add(_cloudDesktopEnabledCheckBox);
        panel.Controls.Add(_calibrateRegionButton);
        panel.Controls.Add(_cloudRegionStatusLabel);
        panel.Controls.Add(_waitForStableScreenCheckBox);
        panel.Controls.Add(CreateFieldLabel("超时(秒)"));
        panel.Controls.Add(_stabilityTimeoutInput);

        _cloudDesktopEnabledCheckBox.CheckedChanged += (_, _) =>
        {
            UpdateCloudDesktopControls();
            ClearExecutionCheckpoint();
        };
        _calibrateRegionButton.Click += (_, _) => BeginCloudRegionCalibration();
        _waitForStableScreenCheckBox.CheckedChanged += (_, _) =>
        {
            _stabilityTimeoutInput.Enabled =
                _waitForStableScreenCheckBox.Checked
                && _cloudDesktopEnabledCheckBox.Checked;
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
        _waitForStableScreenCheckBox.Enabled = enabled;
        _stabilityTimeoutInput.Enabled =
            enabled && _waitForStableScreenCheckBox.Checked;
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
        _stabilityTimeoutInput.Value = Math.Clamp(
            profile.ScreenStability.TimeoutMs / 1000,
            decimal.ToInt32(_stabilityTimeoutInput.Minimum),
            decimal.ToInt32(_stabilityTimeoutInput.Maximum));
        UpdateCloudDesktopControls();
    }
}
