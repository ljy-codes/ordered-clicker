# Cloud Desktop Enhancements Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build v1.3.0 with reliable cloud-desktop relative coordinates, long-sequence execution accounting and resume, simple configurable hotkeys, Save As, stability waiting, updated guidance, packaging, and product delivery.

**Architecture:** Keep `MainForm` as the UI coordinator and move coordinate conversion, execution planning, checkpointing, stability sampling, and logging into focused services. Convert profiles to version 3 while preserving version 1/2 absolute coordinates. Generate an immutable execution plan before countdown so every enabled source row is accounted for and normal completion is impossible unless planned and completed counts match.

**Tech Stack:** C# 14, .NET 10 Windows Forms, Win32 `SendInput`, `System.Drawing`, JSON persistence, existing console-style test harness, PowerShell/Inno Setup packaging.

---

### Task 1: Version 3 profile model and relative coordinate service

**Files:**
- Create: `src/OrderedClicker/Models/CoordinateMode.cs`
- Create: `src/OrderedClicker/Models/CloudDesktopRegion.cs`
- Create: `src/OrderedClicker/Models/ScreenStabilitySettings.cs`
- Create: `src/OrderedClicker/Core/CloudDesktopCoordinateService.cs`
- Modify: `src/OrderedClicker/Models/ClickProfile.cs`
- Modify: `src/OrderedClicker/Models/ClickPoint.cs`
- Modify: `src/OrderedClicker/Core/ProfileValidator.cs`
- Test: `tests/OrderedClicker.Tests/CloudDesktopCoordinateServiceTests.cs`
- Modify: `tests/OrderedClicker.Tests/Program.cs`

- [ ] **Step 1: Write failing coordinate tests**

Add tests for round-trip conversion, a region on a negative-coordinate monitor, minimum `100 x 100` region validation, and points outside the region:

```csharp
var region = new CloudDesktopRegion { X = 100, Y = 200, Width = 1000, Height = 500 };
var relative = CloudDesktopCoordinateService.ToRelative(region, 600, 450);
TestAssert.Equal(0.5, relative.X, "X 应换算为区域比例");
TestAssert.Equal(0.5, relative.Y, "Y 应换算为区域比例");
var absolute = CloudDesktopCoordinateService.ToAbsolute(region, relative.X, relative.Y);
TestAssert.Equal(new Point(600, 450), absolute, "相对坐标应无损回放到原位置");
```

- [ ] **Step 2: Run tests and verify RED**

Run:

```powershell
.\scripts\dotnet.ps1 run --project .\tests\OrderedClicker.Tests\OrderedClicker.Tests.csproj -c Release
```

Expected: compilation fails because the cloud desktop model and service do not exist.

- [ ] **Step 3: Implement model and conversion**

Use profile version `3`, `CoordinateMode.AbsoluteScreen` as the deserialization default, nullable `RelativeX/RelativeY`, and these conversion formulas:

```csharp
public static (double X, double Y) ToRelative(
    CloudDesktopRegion region,
    int x,
    int y)
{
    ValidateRegion(region);
    return (
        (double)(x - region.X) / region.Width,
        (double)(y - region.Y) / region.Height);
}

public static Point ToAbsolute(
    CloudDesktopRegion region,
    double relativeX,
    double relativeY)
{
    ValidateRelative(relativeX, relativeY);
    return new Point(
        region.X + (int)Math.Round(relativeX * region.Width),
        region.Y + (int)Math.Round(relativeY * region.Height));
}
```

In `ProfileValidator`, validate absolute points against the virtual screen and cloud points against the region and `0..1` relative range.

- [ ] **Step 4: Run tests and verify GREEN**

Run the full test executable and expect all suites to pass.

- [ ] **Step 5: Commit**

```powershell
git add src/OrderedClicker/Models src/OrderedClicker/Core tests/OrderedClicker.Tests
git commit -m "feat: add cloud desktop coordinate model"
```

### Task 2: Absolute virtual-screen mouse input

**Files:**
- Modify: `src/OrderedClicker/Native/NativeMethods.cs`
- Modify: `src/OrderedClicker/Services/IMouseController.cs`
- Modify: `src/OrderedClicker/Services/WindowsMouseController.cs`
- Create: `src/OrderedClicker/Core/VirtualScreenCoordinateService.cs`
- Test: `tests/OrderedClicker.Tests/VirtualScreenCoordinateServiceTests.cs`
- Modify: `tests/OrderedClicker.Tests/Program.cs`

- [ ] **Step 1: Write failing normalization tests**

Cover a normal display and a virtual desktop beginning at a negative X:

```csharp
var normalized = VirtualScreenCoordinateService.Normalize(
    x: -960,
    y: 540,
    new ScreenBounds(-1920, 0, 3840, 1080));
TestAssert.Equal(16388, normalized.X, "负坐标桌面 X 应按虚拟屏幕归一化");
TestAssert.Equal(32800, normalized.Y, "Y 应按虚拟屏幕归一化");
```

- [ ] **Step 2: Run tests and verify RED**

Expected: missing `VirtualScreenCoordinateService`.

- [ ] **Step 3: Implement absolute `SendInput` movement**

Add flags:

```csharp
public const uint MouseEventMove = 0x0001;
public const uint MouseEventAbsolute = 0x8000;
public const uint MouseEventVirtualDesk = 0x4000;
```

Change `IMouseController.MoveTo` to:

```csharp
void MoveTo(int x, int y, ScreenBounds virtualScreen);
```

`WindowsMouseController.MoveTo` sends one absolute move input, waits `40ms`, checks `GetCursorPos`, and retries at most three total attempts. Throw a `Win32Exception` containing target and actual coordinates when the final error exceeds two pixels.

- [ ] **Step 4: Update recording mouse implementations and run GREEN**

Update test doubles to record the virtual screen argument without changing event assertions.

- [ ] **Step 5: Commit**

```powershell
git add src/OrderedClicker/Native src/OrderedClicker/Services src/OrderedClicker/Core tests/OrderedClicker.Tests
git commit -m "fix: use verified absolute mouse input"
```

### Task 3: Immutable execution plan, result accounting, and 500-point regression

**Files:**
- Create: `src/OrderedClicker/Models/ExecutionOutcome.cs`
- Create: `src/OrderedClicker/Models/ExecutionPlan.cs`
- Create: `src/OrderedClicker/Models/ExecutionPlanPoint.cs`
- Create: `src/OrderedClicker/Models/ExecutionResult.cs`
- Create: `src/OrderedClicker/Models/ExecutionCheckpoint.cs`
- Create: `src/OrderedClicker/Core/ExecutionPlanService.cs`
- Modify: `src/OrderedClicker/Models/ExecutionProgress.cs`
- Modify: `src/OrderedClicker/Core/ClickExecutionEngine.cs`
- Test: `tests/OrderedClicker.Tests/ExecutionPlanServiceTests.cs`
- Modify: `tests/OrderedClicker.Tests/ClickExecutionEngineTests.cs`
- Modify: `tests/OrderedClicker.Tests/Program.cs`

- [ ] **Step 1: Write failing plan and stress tests**

Create a 500-point profile with three loops and verify:

```csharp
var profile = new ClickProfile
{
    TotalLoops = 3,
    Points = Enumerable.Range(0, 500)
        .Select(index => CreatePoint(index, index + 1, 1))
        .ToList()
};
var plan = ExecutionPlanService.Create(profile, virtualScreen);
var result = await engine.ExecuteAsync(
    plan,
    ExecutionCheckpoint.Start,
    pauseGate,
    null,
    CancellationToken.None);
TestAssert.Equal(1500L, result.CompletedPointExecutionCount,
    "500 个点执行 3 轮必须完成 1500 个点次");
TestAssert.Equal(1500L, result.CompletedClickCount,
    "每点一次时点击总数必须完整");
```

Also test disabled source row indexes, partial multi-click resume, and count mismatch returning `Failed`.

- [ ] **Step 2: Run tests and verify RED**

Expected: missing execution plan and result types.

- [ ] **Step 3: Implement immutable plan**

`ExecutionPlanService.Create` must:

```csharp
var enabled = profile.Points
    .Select((point, sourceIndex) => (point, sourceIndex))
    .Where(item => item.point.Enabled)
    .Select(item => ResolvePoint(item.point, item.sourceIndex, profile, virtualScreen))
    .ToArray();

var plannedPointExecutions = checked((long)enabled.Length * profile.TotalLoops);
var clicksPerLoop = enabled.Aggregate(
    0L,
    (total, point) => checked(total + point.ClickCount));
var plannedClicks = checked(clicksPerLoop * profile.TotalLoops);
```

Each plan point contains resolved absolute coordinates, source row index, click count, click interval, and after-delay.

- [ ] **Step 4: Implement checkpoint-aware engine**

Iterate from checkpoint loop, point, and click indexes. Update counters after each successful click and point. Return `Completed` only when both completed totals equal plan totals. Preserve `EnsureLeftButtonUp()` in `finally`.

- [ ] **Step 5: Run tests and verify GREEN**

Expected: the 500-point test reports all 1500 point executions in order.

- [ ] **Step 6: Commit**

```powershell
git add src/OrderedClicker/Models src/OrderedClicker/Core tests/OrderedClicker.Tests
git commit -m "feat: make long workflows accountable and resumable"
```

### Task 4: Screen stability detector and timeout pause

**Files:**
- Create: `src/OrderedClicker/Services/IScreenSampler.cs`
- Create: `src/OrderedClicker/Services/WindowsScreenSampler.cs`
- Create: `src/OrderedClicker/Core/ScreenStabilityDetector.cs`
- Create: `src/OrderedClicker/Models/ScreenStabilityResult.cs`
- Modify: `src/OrderedClicker/Core/ClickExecutionEngine.cs`
- Modify: `src/OrderedClicker/Models/ExecutionProgress.cs`
- Test: `tests/OrderedClicker.Tests/ScreenStabilityDetectorTests.cs`
- Modify: `tests/OrderedClicker.Tests/ClickExecutionEngineTests.cs`
- Modify: `tests/OrderedClicker.Tests/Program.cs`

- [ ] **Step 1: Write failing stable, timeout, and cancellation tests**

Use a fake sampler returning byte arrays:

```csharp
var sampler = new SequenceScreenSampler(
    ChangingFrame(10),
    StableFrame(),
    StableFrame(),
    StableFrame());
var result = await detector.WaitForStableAsync(region, settings, token);
TestAssert.Equal(ScreenStabilityResult.Stable, result,
    "连续稳定达到阈值后应继续");
```

Add a continuously changing sequence that returns `TimedOut`, and cancellation that throws `OperationCanceledException`.

- [ ] **Step 2: Run tests and verify RED**

Expected: missing sampler and detector.

- [ ] **Step 3: Implement low-resolution sampling**

`WindowsScreenSampler` captures the region with `Graphics.CopyFromScreen`, draws to at most `160 x 90`, and returns grayscale bytes. `ScreenStabilityDetector` compares mean absolute byte difference and tracks continuous stable duration.

- [ ] **Step 4: Integrate timeout pause**

After fixed point delay, call the detector when enabled. On timeout:

```csharp
pauseGate.Pause();
progress?.Report(progressState with
{
    RequiresUserContinue = true,
    Message = $"点位 {sourceIndex + 1} 后画面未稳定"
});
await pauseGate.WaitIfPausedAsync(cancellationToken);
```

Do not advance to the next point until resumed.

- [ ] **Step 5: Run tests and commit**

```powershell
git add src/OrderedClicker tests/OrderedClicker.Tests
git commit -m "feat: wait for cloud desktop screen stability"
```

### Task 5: Execution log and in-session checkpoint service

**Files:**
- Create: `src/OrderedClicker/Services/ExecutionLogService.cs`
- Create: `src/OrderedClicker/Services/ExecutionCheckpointService.cs`
- Test: `tests/OrderedClicker.Tests/ExecutionReliabilityServiceTests.cs`
- Modify: `tests/OrderedClicker.Tests/Program.cs`

- [ ] **Step 1: Write failing persistence and checkpoint tests**

Verify JSON logs contain plan/completed counts, last row, outcome, and message. Verify `ExecutionCheckpointService` stores the next click and clears after a true completed result.

- [ ] **Step 2: Run RED**

Expected: missing services.

- [ ] **Step 3: Implement services**

Write logs atomically to:

```csharp
Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "OrderedClicker",
    "logs")
```

Use a timestamp plus GUID filename to avoid collisions. Keep checkpoint state in memory only.

- [ ] **Step 4: Run GREEN and commit**

```powershell
git add src/OrderedClicker/Services tests/OrderedClicker.Tests
git commit -m "feat: record and resume interrupted executions"
```

### Task 6: Simple hotkey defaults, presets, and migration

**Files:**
- Modify: `src/OrderedClicker/Core/HotKeyBindingService.cs`
- Modify: `src/OrderedClicker/Models/AppSettings.cs`
- Modify: `src/OrderedClicker/Services/SettingsService.cs`
- Modify: `src/OrderedClicker/Forms/MainForm.Settings.cs`
- Modify: `tests/OrderedClicker.Tests/HotKeyBindingServiceTests.cs`
- Modify: `tests/OrderedClicker.Tests/ThemeSettingsTests.cs`
- Modify: `tests/OrderedClicker.Tests/UiSmokeTests.cs`

- [ ] **Step 1: Write failing shortcut tests**

Assert new defaults are `F6/F7/F8`, no-modifier keys outside `F6..F12` are rejected, legacy default combinations migrate once, and custom combinations remain unchanged.

- [ ] **Step 2: Run RED**

Expected: current defaults remain `Ctrl+Alt+F8/F9/F10`.

- [ ] **Step 3: Implement defaults and migration**

Add `AppSettings.Version = 2`. Define:

```csharp
public static HotKeyBinding DefaultCapture { get; } =
    new(Keys.F6, ShortcutModifiers.None);
public static HotKeyBinding DefaultStartPause { get; } =
    new(Keys.F7, ShortcutModifiers.None);
public static HotKeyBinding DefaultStop { get; } =
    new(Keys.F8, ShortcutModifiers.None);
```

Keep compatibility preset constants for the old combinations. During load, migrate only settings version `< 2` whose three bindings equal the old defaults. Save migrated settings only after successful app startup registration.

- [ ] **Step 4: Add preset controls**

Add `简洁模式`, `兼容模式`, and existing custom input behavior to the settings dialog. Preset buttons only populate inputs; the existing transactional registration still decides whether saving succeeds.

- [ ] **Step 5: Run GREEN and commit**

```powershell
git add src/OrderedClicker tests/OrderedClicker.Tests
git commit -m "feat: simplify configurable hotkeys"
```

### Task 7: Save As and current profile path

**Files:**
- Modify: `src/OrderedClicker/Services/ProfileService.cs`
- Modify: `src/OrderedClicker/Forms/MainForm.cs`
- Modify: `tests/OrderedClicker.Tests/ProfileServiceTests.cs`
- Modify: `tests/OrderedClicker.Tests/UiSmokeTests.cs`

- [ ] **Step 1: Write failing save-path tests**

Add `ProfileService.Save(profile, path)` tests for atomic overwrite and preserving the original file after a failed write. Add UI smoke assertions that `SaveButton`, `SaveAsButton`, and `LoadButton` are visible.

- [ ] **Step 2: Run RED**

Expected: no save overload and no Save As button.

- [ ] **Step 3: Implement Save As**

Track `_currentProfilePath`. Add:

```csharp
private readonly Button _saveAsButton = new() { Name = "SaveAsButton" };
```

`SaveProfile` overwrites `_currentProfilePath` when set. `SaveProfileAs` opens a `SaveFileDialog`, saves to the chosen `.json`, and updates `_currentProfilePath` only after success. `LoadProfile` sets the current path after successful load.

- [ ] **Step 4: Run GREEN and commit**

```powershell
git add src/OrderedClicker/Services src/OrderedClicker/Forms tests/OrderedClicker.Tests
git commit -m "feat: restore explicit save as workflow"
```

### Task 8: Cloud desktop toolbar and capture state machine

**Files:**
- Create: `src/OrderedClicker/Models/CaptureMode.cs`
- Create: `src/OrderedClicker/Forms/MainForm.CloudDesktop.cs`
- Modify: `src/OrderedClicker/Forms/MainForm.cs`
- Modify: `src/OrderedClicker/Forms/MainForm.Execution.cs`
- Modify: `tests/OrderedClicker.Tests/UiSmokeTests.cs`

- [ ] **Step 1: Write failing UI and capture tests**

Assert the main form contains `CloudDesktopEnabledCheckBox`, `CalibrateRegionButton`, `CloudRegionStatusLabel`, `WaitForStableScreenCheckBox`, and `StabilityTimeoutInput`. Invoke capture mode transitions and verify the first hotkey stores top-left, the second stores bottom-right, and ordinary points are not added during calibration.

- [ ] **Step 2: Run RED**

Expected: controls and capture state do not exist.

- [ ] **Step 3: Add a seventh layout row**

Insert a compact cloud desktop row between timing and grid. Use checkboxes for binary settings, one button for calibration, a status label, and a numeric timeout input from `3` to `60` seconds.

- [ ] **Step 4: Replace `_captureMode` with `CaptureMode`**

Supported states:

```csharp
Idle,
PointCapture,
CloudRegionTopLeft,
CloudRegionBottomRight
```

Track point-capture session start count. When point capture ends, report newly added and total counts. In cloud mode, reject point capture outside the calibrated region and save relative values for accepted points.

- [ ] **Step 5: Run GREEN and commit**

```powershell
git add src/OrderedClicker/Models src/OrderedClicker/Forms tests/OrderedClicker.Tests
git commit -m "feat: add cloud desktop calibration workflow"
```

### Task 9: Execution confirmation, full-count completion, and resume UI

**Files:**
- Create: `src/OrderedClicker/Forms/ExecutionPlanDialog.cs`
- Modify: `src/OrderedClicker/Forms/MainForm.Execution.cs`
- Modify: `src/OrderedClicker/Forms/MainForm.cs`
- Modify: `tests/OrderedClicker.Tests/UiSmokeTests.cs`

- [ ] **Step 1: Write failing UI behavior tests**

Verify plan text includes total rows, enabled rows, disabled source row numbers, loops, planned point executions, and clicks. Verify a stopped result changes start text to `从第 N 步继续`, while a completed result clears the checkpoint.

- [ ] **Step 2: Run RED**

Expected: no plan dialog or resume state.

- [ ] **Step 3: Build plan before countdown**

Call `ExecutionPlanService.Create` after committing the grid. Show `ExecutionPlanDialog`; do not start if the user cancels. Store the immutable plan for the run and pass it to the engine.

- [ ] **Step 4: Handle results and logs**

Write every result through `ExecutionLogService`. Show normal completion only after count equality. On stopped/failed results, preserve checkpoint and expose two commands:

```text
从第 N 步继续
重新开始
```

The ordinary start shortcut continues when a checkpoint exists; a dedicated secondary button or menu resets and starts over.

- [ ] **Step 5: Run GREEN and commit**

```powershell
git add src/OrderedClicker/Forms tests/OrderedClicker.Tests
git commit -m "feat: confirm and resume complex workflows"
```

### Task 10: Documentation, version 1.3.0, packaging, and delivery

**Files:**
- Modify: `README.md`
- Modify: `src/OrderedClicker/OrderedClicker.csproj`
- Modify: `installer/OrderedClicker.iss`
- Modify: `scripts/publish.ps1`
- Modify: `scripts/build-installer.ps1`
- Modify: `tests/Installer.Tests.ps1`
- Modify: `scripts/guide/build_pdf_guide.py`
- Modify: `scripts/guide/verify_guide.py`
- Modify: `操作指导/有序连点器-使用说明.html`
- Modify: `操作指导/有序连点器-使用说明.pdf`
- Create: `artifacts/ordered-clicker-ui-v1.3.0.png`

- [ ] **Step 1: Update installer contract tests first**

Change expected version to `1.3.0` and assert product output names:

```text
ordered-clicker-setup-v1.3.0.exe
ordered-clicker-portable-v1.3.0.zip
```

- [ ] **Step 2: Run installer tests and verify RED**

Expected: scripts and project still report `1.2.0`.

- [ ] **Step 3: Update version and guidance**

Document:

- F6/F7/F8 defaults and custom/compatibility presets.
- Save, Save As, and Load.
- Cloud region two-point calibration.
- Stable-screen waiting and timeout continue.
- Plan counts, disabled rows, completion counts, logs, and resume.
- Pointer-lock and elevated-window limitations.

Keep the existing video unchanged.

- [ ] **Step 4: Render and verify guide**

Run:

```powershell
& 'C:\Users\nb-liuxinsheng\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe' `
  .\scripts\guide\build_product_guide.py
& 'C:\Users\nb-liuxinsheng\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe' `
  .\scripts\guide\verify_guide.py --skip-video
```

Expected: guide verification passes and the PDF contains cloud desktop, Save As, F6/F7/F8, long-flow counts, and resume guidance.

- [ ] **Step 5: Run complete build and product delivery**

```powershell
.\scripts\build-installer.ps1 `
  -Version 1.3.0 `
  -ProductDirectory 'D:\专用工具\连接器产品' `
  -GuideSourceDirectory 'D:\专用工具\连点器\操作指导'
```

Expected: all automated tests and installer contract tests pass, and the product directory contains exactly the v1.3.0 installer, portable ZIP, PDF, HTML, unchanged video, and `SHA256SUMS.txt`.

- [ ] **Step 6: Final verification**

Run:

```powershell
git diff --check
git status --short
```

Verify the product checksums and executable version, then commit:

```powershell
git add .
git commit -m "feat: add cloud desktop enhanced workflows"
```

- [ ] **Step 7: Push and publish**

Push `main`, tag `v1.3.0`, and upload the six delivery files to:

```text
https://github.com/ljy-codes/ordered-clicker/releases/tag/v1.3.0
```
