using OrderedClicker.Core;
using OrderedClicker.Models;
using OrderedClicker.Services;

namespace OrderedClicker.Forms;

public sealed partial class MainForm
{
    private readonly ExecutionLogService _executionLogService = new();

    private async Task HandleStartPauseAsync()
    {
        switch (_executionState)
        {
            case ExecutionState.Idle:
                await StartExecutionAsync();
                break;
            case ExecutionState.Running:
                _pauseGate.Pause();
                SetExecutionState(ExecutionState.Paused);
                SetStatus($"执行已暂停，按 {StartPauseHotKeyText} 继续。");
                break;
            case ExecutionState.Paused:
                _pauseGate.Resume();
                SetExecutionState(ExecutionState.Running);
                SetStatus("继续执行。");
                break;
        }
    }

    private async Task StartExecutionAsync()
    {
        ExecutionPlan plan;
        ExecutionCheckpoint checkpoint;

        if (_pendingExecutionPlan is not null)
        {
            plan = _pendingExecutionPlan;
            checkpoint = _executionCheckpoint;
        }
        else
        {
            CommitGridChanges();
            var profile = CreateProfileSnapshot();
            var virtualScreen = _monitorService.GetVirtualScreenBounds();
            var validationErrors = ProfileValidator.Validate(profile, virtualScreen);
            if (validationErrors.Count > 0)
            {
                ShowError(string.Join(Environment.NewLine, validationErrors));
                return;
            }

            var monitorWarnings = GetMonitorWarnings(profile);
            if (monitorWarnings.Count > 0
                && !ConfirmMonitorWarnings(monitorWarnings))
            {
                return;
            }

            try
            {
                plan = ExecutionPlanService.Create(profile, virtualScreen);
            }
            catch (Exception exception)
            {
                ShowError($"生成执行计划失败：{exception.Message}");
                return;
            }

            using var planDialog = new ExecutionPlanDialog(plan, _theme);
            if (planDialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            checkpoint = ExecutionCheckpoint.Start;
        }

        _captureMode = CaptureMode.Idle;
        UpdateCaptureButton();
        _pauseGate.Resume();
        _executionCancellation = new CancellationTokenSource();
        SetConfigurationEnabled(false);

        try
        {
            SetExecutionState(ExecutionState.Countdown);
            for (var seconds = 3; seconds >= 1; seconds--)
            {
                SetStatus(
                    _pendingExecutionPlan is null
                        ? $"{seconds} 秒后开始，请切换到目标窗口。"
                        : $"{seconds} 秒后从断点继续，请切换到目标窗口。");
                await Task.Delay(
                    TimeSpan.FromSeconds(1),
                    _executionCancellation.Token);
            }

            SetExecutionState(ExecutionState.Running);
            var progress = new Progress<ExecutionProgress>(UpdateExecutionProgress);
            var result = await Task.Run(
                () => _executionEngine.ExecuteAsync(
                    plan,
                    checkpoint,
                    _pauseGate,
                    progress,
                    _executionCancellation.Token));

            try
            {
                _executionLogService.Write(plan, result);
            }
            catch (Exception logException)
            {
                SetStatus($"执行结果已生成，但日志保存失败：{logException.Message}");
            }

            HandleExecutionResult(plan, result);
        }
        catch (OperationCanceledException)
        {
            PreserveExecutionCheckpoint(plan, checkpoint);
            SetStatus("执行在启动前停止，可再次继续。");
        }
        catch (Exception exception)
        {
            PreserveExecutionCheckpoint(plan, checkpoint);
            ShowError($"执行失败：{exception.Message}");
        }
        finally
        {
            _pauseGate.Resume();
            _executionCancellation?.Dispose();
            _executionCancellation = null;
            SetConfigurationEnabled(true);
            SetExecutionState(ExecutionState.Idle);
        }
    }

    private bool ConfirmMonitorWarnings(IReadOnlyList<string> monitorWarnings)
    {
        var warningText = string.Join(Environment.NewLine, monitorWarnings.Take(8));
        if (monitorWarnings.Count > 8)
        {
            warningText +=
                $"{Environment.NewLine}另有 {monitorWarnings.Count - 8} 项警告。";
        }

        return MessageBox.Show(
                this,
                $"{warningText}{Environment.NewLine}{Environment.NewLine}仍要继续执行吗？",
                "显示器环境已变化",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning)
            == DialogResult.Yes;
    }

    private void HandleExecutionResult(ExecutionPlan plan, ExecutionResult result)
    {
        if (result.Outcome == ExecutionOutcome.Completed
            && result.CompletedPointExecutionCount == plan.PlannedPointExecutionCount
            && result.CompletedClickCount == plan.PlannedClickCount)
        {
            ClearExecutionCheckpoint();
            SetStatus(
                $"全部完成：{result.CompletedPointExecutionCount}/"
                + $"{plan.PlannedPointExecutionCount} 点次，"
                + $"{result.CompletedClickCount}/{plan.PlannedClickCount} 次点击。");
            return;
        }

        PreserveExecutionCheckpoint(plan, result.NextCheckpoint);
        if (result.Outcome == ExecutionOutcome.Stopped)
        {
            SetStatus(
                $"已停止：完成 {result.CompletedPointExecutionCount}/"
                + $"{plan.PlannedPointExecutionCount} 点次，"
                + "可从断点继续。");
            return;
        }

        ShowError(
            $"执行未完整完成：{result.Message}{Environment.NewLine}"
            + $"点次 {result.CompletedPointExecutionCount}/"
            + $"{plan.PlannedPointExecutionCount}，点击 "
            + $"{result.CompletedClickCount}/{plan.PlannedClickCount}。");
    }

    private void PreserveExecutionCheckpoint(
        ExecutionPlan plan,
        ExecutionCheckpoint checkpoint)
    {
        _pendingExecutionPlan = plan;
        _executionCheckpoint = checkpoint;
        _restartButton.Visible = true;
    }

    private void ClearExecutionCheckpoint()
    {
        _pendingExecutionPlan = null;
        _executionCheckpoint = ExecutionCheckpoint.Start;
        _restartButton.Visible = false;
        if (_executionState == ExecutionState.Idle)
        {
            SetExecutionState(ExecutionState.Idle);
        }
    }

    private void StopExecution()
    {
        if (_executionState == ExecutionState.Idle)
        {
            return;
        }

        SetExecutionState(ExecutionState.Stopping);
        _pauseGate.Resume();
        _executionCancellation?.Cancel();
    }

    private void UpdateExecutionProgress(ExecutionProgress progress)
    {
        if (IsDisposed)
        {
            return;
        }

        _progressStatusLabel.Text =
            $"循环 {progress.CurrentLoop}/{progress.TotalLoops}，"
            + $"原表第 {progress.SourceIndex + 1} 行，"
            + $"点位 {progress.PointIndex}/{progress.PointCount}，"
            + $"点击 {progress.ClickIndex}/{progress.ClickCount}，"
            + $"总点击 {progress.CompletedClickCount}/{progress.PlannedClickCount}";

        if (progress.RequiresUserContinue)
        {
            SetExecutionState(ExecutionState.Paused);
            SetStatus(
                $"{progress.Message}。确认页面可操作后按 "
                + $"{StartPauseHotKeyText} 继续，或按 {StopHotKeyText} 停止。");
        }
    }

    private void SetExecutionState(ExecutionState state)
    {
        _executionState = state;
        _stateStatusLabel.Text = state switch
        {
            ExecutionState.Idle => "就绪",
            ExecutionState.Countdown => "倒计时",
            ExecutionState.Running => "执行中",
            ExecutionState.Paused => "已暂停",
            ExecutionState.Stopping => "正在停止",
            _ => state.ToString()
        };

        _startPauseButton.Text = state switch
        {
            ExecutionState.Running => $"暂停 ({StartPauseHotKeyText})",
            ExecutionState.Paused => $"继续 ({StartPauseHotKeyText})",
            ExecutionState.Countdown => "准备中…",
            ExecutionState.Stopping => "正在停止…",
            _ when _pendingExecutionPlan is not null =>
                $"从第 {GetResumeSourceRow()} 步继续 ({StartPauseHotKeyText})",
            _ => $"开始 ({StartPauseHotKeyText})"
        };
        _startPauseButton.Enabled = state is ExecutionState.Idle
            or ExecutionState.Running
            or ExecutionState.Paused;
        _stopButton.Enabled = state != ExecutionState.Idle;
        _restartButton.Visible =
            state == ExecutionState.Idle && _pendingExecutionPlan is not null;
    }

    private int GetResumeSourceRow()
    {
        if (_pendingExecutionPlan is null
            || _executionCheckpoint.PointIndex >= _pendingExecutionPlan.Points.Count)
        {
            return 1;
        }

        return _pendingExecutionPlan.Points[_executionCheckpoint.PointIndex].SourceIndex + 1;
    }

    private void SetConfigurationEnabled(bool enabled)
    {
        _profileNameTextBox.Enabled = enabled;
        _totalLoopsInput.Enabled = enabled;
        _loopDelayInput.Enabled = enabled;
        _defaultClickIntervalInput.Enabled = enabled;
        _defaultAfterDelayInput.Enabled = enabled;
        _pointGrid.Enabled = enabled;
        _captureButton.Enabled = enabled;
        _moveUpButton.Enabled = enabled;
        _moveDownButton.Enabled = enabled;
        _deleteButton.Enabled = enabled;
        _clearButton.Enabled = enabled;
        _themeSettingsButton.Enabled = enabled;
        _saveButton.Enabled = enabled;
        _saveAsButton.Enabled = enabled;
        _importProfileButton.Enabled = enabled;
        _exportProfileButton.Enabled = enabled;
        _applyClickIntervalButton.Enabled = enabled;
        _applyAfterDelayButton.Enabled = enabled;
        _cloudDesktopEnabledCheckBox.Enabled = enabled;
        _calibrateRegionButton.Enabled =
            enabled && _cloudDesktopEnabledCheckBox.Checked;
        _waitForStableScreenCheckBox.Enabled =
            enabled && _cloudDesktopEnabledCheckBox.Checked;
        _stabilityTimeoutInput.Enabled =
            enabled
            && _cloudDesktopEnabledCheckBox.Checked
            && _waitForStableScreenCheckBox.Checked;
    }
}
