using OrderedClicker.Core;
using OrderedClicker.Models;

namespace OrderedClicker.Forms;

public sealed partial class MainForm
{
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
                SetStatus("执行已暂停，按 F9 继续。");
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
        CommitGridChanges();
        var profile = CreateProfileSnapshot();
        var validationErrors = ProfileValidator.Validate(
            profile,
            _monitorService.GetVirtualScreenBounds());
        if (validationErrors.Count > 0)
        {
            ShowError(string.Join(Environment.NewLine, validationErrors));
            return;
        }

        var monitorWarnings = GetMonitorWarnings(profile);
        if (monitorWarnings.Count > 0)
        {
            var warningText = string.Join(Environment.NewLine, monitorWarnings.Take(8));
            if (monitorWarnings.Count > 8)
            {
                warningText += $"{Environment.NewLine}另有 {monitorWarnings.Count - 8} 项警告。";
            }

            if (MessageBox.Show(
                    this,
                    $"{warningText}{Environment.NewLine}{Environment.NewLine}仍要继续执行吗？",
                    "显示器环境已变化",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }
        }

        _captureMode = false;
        UpdateCaptureButton();
        _pauseGate.Resume();
        _executionCancellation = new CancellationTokenSource();
        SetConfigurationEnabled(false);

        try
        {
            SetExecutionState(ExecutionState.Countdown);
            for (var seconds = 3; seconds >= 1; seconds--)
            {
                SetStatus($"{seconds} 秒后开始，请切换到目标窗口。");
                await Task.Delay(TimeSpan.FromSeconds(1), _executionCancellation.Token);
            }

            SetExecutionState(ExecutionState.Running);
            var progress = new Progress<ExecutionProgress>(UpdateExecutionProgress);
            await Task.Run(
                () => _executionEngine.ExecuteAsync(
                    profile,
                    _pauseGate,
                    progress,
                    _executionCancellation.Token),
                _executionCancellation.Token);
            SetStatus("全部循环已完成。");
        }
        catch (OperationCanceledException)
        {
            SetStatus("执行已停止。");
        }
        catch (Exception exception)
        {
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
            $"循环 {progress.CurrentLoop}/{progress.TotalLoops}，" +
            $"点位 {progress.PointIndex}/{progress.PointCount}，" +
            $"点击 {progress.ClickIndex}/{progress.ClickCount}";
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
            ExecutionState.Running => "暂停 (F9)",
            ExecutionState.Paused => "继续 (F9)",
            ExecutionState.Countdown => "准备中…",
            ExecutionState.Stopping => "正在停止…",
            _ => "开始 (F9)"
        };
        _startPauseButton.Enabled = state is ExecutionState.Idle
            or ExecutionState.Running
            or ExecutionState.Paused;
        _stopButton.Enabled = state != ExecutionState.Idle;
    }

    private void SetConfigurationEnabled(bool enabled)
    {
        _profileNameTextBox.Enabled = enabled;
        _totalLoopsInput.Enabled = enabled;
        _loopDelayInput.Enabled = enabled;
        _pointGrid.Enabled = enabled;
        _captureButton.Enabled = enabled;
        _moveUpButton.Enabled = enabled;
        _moveDownButton.Enabled = enabled;
        _deleteButton.Enabled = enabled;
        _clearButton.Enabled = enabled;
        _saveButton.Enabled = enabled;
        _loadButton.Enabled = enabled;
    }
}
