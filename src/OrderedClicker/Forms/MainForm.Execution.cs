using OrderedClicker.Core;
using OrderedClicker.Models;
using OrderedClicker.Services;

namespace OrderedClicker.Forms;

public sealed partial class MainForm
{
    private readonly ExecutionLogService _executionLogService;

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
        if (_executionStartInProgress
            || _activeExecutionTask is { IsCompleted: false })
        {
            SetStatus("正在准备执行，请勿重复启动。");
            return;
        }

        _executionStartInProgress = true;
        var executionTask = StartExecutionCoreAsync();
        _activeExecutionTask = executionTask;
        try
        {
            await executionTask;
        }
        finally
        {
            _executionStartInProgress = false;
            if (ReferenceEquals(_activeExecutionTask, executionTask))
            {
                _activeExecutionTask = null;
            }
        }
    }

    private async Task StartExecutionCoreAsync()
    {
        ExecutionPlan plan;
        ExecutionCheckpoint checkpoint;

        if (!_settings.SafetyCornerEnabled)
        {
            ShowError("开始执行前必须启用安全角停止。请在设置中选择一个安全角。");
            return;
        }

        if (_enableGlobalHotKeys
            && _activeHotKeysKnown
            && !_activeHotKeyRegistrations.ContainsKey(StopHotKeyId))
        {
            ShowError("停止快捷键当前不可用。请关闭占用快捷键的软件并重新设置后再执行。");
            return;
        }

        if (_pendingExecutionPlan is not null)
        {
            CommitGridChanges();
            var currentFingerprint = ExecutionPlanFingerprintService.Create(
                CreateProfileSnapshot());
            if (!string.Equals(
                    currentFingerprint,
                    _pendingExecutionPlan.ProfileFingerprint,
                    StringComparison.Ordinal))
            {
                ClearExecutionCheckpoint();
                SetStatus("执行配置已变化，旧断点已失效，请重新确认计划。");
            }
        }

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
                RecordDiagnostic("execution.plan", exception);
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

        var safetyCornerPoint = plan.Points.FirstOrDefault(point =>
            SafetyCornerService.Contains(
                new Point(point.X, point.Y),
                plan.VirtualScreen,
                _settings.SafetyCorner,
                _settings.SafetyCornerSize));
        if (safetyCornerPoint is not null)
        {
            ShowError(
                $"原表第 {safetyCornerPoint.SourceIndex + 1} 行点位落在安全角内。"
                + "请移动该点位，或在设置中更换安全角。");
            return;
        }

        _captureMode = CaptureMode.Idle;
        CancelDelayedCapture();
        _latestExecutionProgress.Clear();
        UpdateCaptureButton();
        _pauseGate.Resume();
        using var executionCancellation = new CancellationTokenSource();
        _executionCancellation = executionCancellation;
        _activeExecutionPlan = plan;
        SetConfigurationEnabled(false);
        await using var safetyWatchdog = new SafetyCornerWatchdog(
            () => _settings.SafetyCornerEnabled
                  && _executionState is not ExecutionState.Idle
                  and not ExecutionState.Stopping,
            () => Cursor.Position,
            _monitorService.GetVirtualScreenBounds,
            () => _settings.SafetyCorner,
            () => _settings.SafetyCornerSize,
            () => TimeSpan.FromMilliseconds(_settings.SafetyCornerDwellMs),
            () => RequestSafetyStop(executionCancellation));
        safetyWatchdog.Start();

        try
        {
            SetExecutionState(ExecutionState.Countdown);
            ShowRunStatusForm();
            for (var seconds = 3; seconds >= 1; seconds--)
            {
                SetStatus(
                    _pendingExecutionPlan is null
                        ? $"{seconds} 秒后开始，请切换到目标窗口。"
                        : $"{seconds} 秒后从断点继续，请切换到目标窗口。");
                await Task.Delay(
                    TimeSpan.FromSeconds(1),
                    executionCancellation.Token);
            }

            SetExecutionState(ExecutionState.Running);
            var result = await Task.Run(
                () => _executionEngine.ExecuteAsync(
                    plan,
                    checkpoint,
                    _pauseGate,
                    _latestExecutionProgress,
                    executionCancellation.Token));

            try
            {
                _executionLogService.Write(plan, result);
            }
            catch (Exception logException)
            {
                RecordDiagnostic("execution.log", logException);
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
            RecordDiagnostic("execution.run", exception);
            PreserveExecutionCheckpoint(plan, checkpoint);
            ShowError($"执行失败：{exception.Message}");
        }
        finally
        {
            _pauseGate.Resume();
            if (ReferenceEquals(_executionCancellation, executionCancellation))
            {
                _executionCancellation = null;
            }
            _activeExecutionPlan = null;
            _latestExecutionProgress.Clear();
            SetConfigurationEnabled(true);
            SetExecutionState(ExecutionState.Idle);
            CloseRunStatusForm();
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
        SaveExecutionCheckpoint(plan, checkpoint, true);
    }

    private void ClearExecutionCheckpoint()
    {
        _pendingExecutionPlan = null;
        _executionCheckpoint = ExecutionCheckpoint.Start;
        _restartButton.Visible = false;
        try
        {
            _executionCheckpointService?.Clear();
        }
        catch (Exception exception)
        {
            RecordDiagnostic("execution.checkpoint.clear", exception);
        }
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
            + $"总点击 {progress.CompletedClickCount}/{progress.PlannedClickCount}，"
            + $"阶段 {DescribeExecutionStage(progress.Stage)}";
        _runStatusForm?.UpdateProgress(_progressStatusLabel.Text);
        if (_activeExecutionPlan is not null)
        {
            var checkpoint = new ExecutionCheckpoint(
                Math.Max(0, progress.CurrentLoop - 1),
                Math.Max(0, progress.PointIndex - 1),
                progress.ClickIndex,
                progress.Stage,
                progress.CompletedPointExecutionCount,
                progress.CompletedClickCount);
            _pendingExecutionPlan = _activeExecutionPlan;
            _executionCheckpoint = checkpoint;
            SaveExecutionCheckpoint(_activeExecutionPlan, checkpoint, false);
        }

        if (progress.RequiresUserContinue)
        {
            SetExecutionState(ExecutionState.Paused);
            SetStatus(
                $"{progress.Message}。确认页面可操作后按 "
                + $"{StartPauseHotKeyText} 继续，或按 {StopHotKeyText} 停止。");
        }
    }

    private void RequestSafetyStop(CancellationTokenSource executionCancellation)
    {
        _pauseGate.Resume();
        executionCancellation.Cancel();
        if (!IsHandleCreated || IsDisposed)
        {
            return;
        }

        BeginInvoke(() =>
        {
            if (_executionState != ExecutionState.Idle)
            {
                SetExecutionState(ExecutionState.Stopping);
                SetStatus("安全角已触发，正在停止执行。");
            }
        });
    }

    private void SaveExecutionCheckpoint(
        ExecutionPlan plan,
        ExecutionCheckpoint checkpoint,
        bool force)
    {
        if (_executionCheckpointService is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (!force
            && now - _lastCheckpointSaveAt < TimeSpan.FromMilliseconds(500))
        {
            return;
        }

        try
        {
            _executionCheckpointService.Save(new ExecutionCheckpointEnvelope(
                1,
                Application.ProductVersion,
                plan,
                plan.ProfileFingerprint,
                checkpoint,
                now));
            _lastCheckpointSaveAt = now;
        }
        catch (Exception exception)
        {
            RecordDiagnostic("execution.checkpoint.save", exception);
        }
    }

    private void RecoverExecutionCheckpointIfAvailable()
    {
        if (_executionCheckpointService is null)
        {
            return;
        }

        try
        {
            var envelope = _executionCheckpointService.Load();
            if (envelope is null)
            {
                return;
            }

            var restore = MessageBox.Show(
                this,
                "检测到上次未完成的执行任务。是否从保存的断点继续？\n\n"
                + "异常退出附近的最后一步可能需要确认，避免重复操作。",
                "恢复执行断点",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) == DialogResult.Yes;
            if (!restore)
            {
                _executionCheckpointService.Clear();
                return;
            }

            _pendingExecutionPlan = envelope.Plan;
            _executionCheckpoint = envelope.Checkpoint;
            _restartButton.Visible = true;
            SetExecutionState(ExecutionState.Idle);
            SetStatus($"已恢复执行断点，可从第 {GetResumeSourceRow()} 步继续。");
        }
        catch (Exception exception)
        {
            RecordDiagnostic("execution.checkpoint.load", exception);
            try
            {
                _executionCheckpointService.QuarantineBrokenCheckpoint();
            }
            catch (Exception quarantineException)
            {
                RecordDiagnostic(
                    "execution.checkpoint.quarantine",
                    quarantineException);
            }

            SetStatus("执行断点已损坏并隔离，本次将重新开始。");
        }
    }

    private static string DescribeExecutionStage(ExecutionStage stage)
    {
        return stage switch
        {
            ExecutionStage.Move => "移动鼠标",
            ExecutionStage.Click => "点击",
            ExecutionStage.ClickInterval => "点击间隔",
            ExecutionStage.AfterPointDelay => "点后等待",
            ExecutionStage.StabilityCheck => "等待画面稳定",
            ExecutionStage.LoopDelay => "轮间等待",
            ExecutionStage.Completed => "完成",
            _ => stage.ToString()
        };
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
        _workspaceStateLabel.Text = _stateStatusLabel.Text;
        _runStatusForm?.UpdateState(
            _stateStatusLabel.Text,
            state == ExecutionState.Paused);

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

    private void ShowRunStatusForm()
    {
        _runStatusForm?.Dispose();
        _runStatusForm = new RunStatusForm(_theme);
        _runStatusForm.PauseRequested += () => _ = HandleStartPauseAsync();
        _runStatusForm.StopRequested += StopExecution;
        _runStatusForm.UpdateState(
            _stateStatusLabel.Text ?? "准备执行",
            _executionState == ExecutionState.Paused);
        _runStatusForm.Show(this);
    }

    private void CloseRunStatusForm()
    {
        if (_runStatusForm is null)
        {
            return;
        }

        _runStatusForm.Dispose();
        _runStatusForm = null;
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
        _profileSelector.Enabled = enabled;
        _openProfilesDirectoryButton.Enabled = enabled;
        _totalLoopsInput.Enabled = enabled;
        _loopDelayInput.Enabled = enabled;
        _defaultClickIntervalInput.Enabled = enabled;
        _defaultAfterDelayInput.Enabled = enabled;
        _pointGrid.Enabled = enabled;
        _captureButton.Enabled = enabled;
        _captureNowButton.Enabled = enabled;
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
