# Ordered Clicker 2.0 Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver Ordered Clicker 2.0 as a safe step-based Windows desktop automation workspace with v4 profiles, automatic draft recovery, conservative checkpoints, dual emergency stop, visible capture/runtime feedback, portable isolation, and updated release assets.

**Architecture:** Preserve the existing mouse, coordinate, hot-key, and theme services where their contracts remain valid. Add focused persistence, migration, execution-stage, safety, diagnostics, and UI workspace components around them; keep `MainForm` as the composition root while moving reusable behavior into testable services.

**Tech Stack:** C# 14, .NET 10 Windows Forms, System.Text.Json, GDI/Win32, PowerShell 7, Inno Setup 7, existing console test harness.

---

## File Structure

New production files:

- `src/OrderedClicker/Models/ProfileDocument.cs`: v4 persisted profile model.
- `src/OrderedClicker/Models/ProfileSaveOptions.cs`: explicit overwrite, backup, and identity-renewal semantics.
- `src/OrderedClicker/Models/ExecutionStage.cs`: conservative checkpoint phases.
- `src/OrderedClicker/Models/SafetyCorner.cs`: emergency corner configuration.
- `src/OrderedClicker/Services/AppDataPaths.cs`: installed/portable data roots.
- `src/OrderedClicker/Services/DraftService.cs`: atomic active-draft persistence.
- `src/OrderedClicker/Services/LegacyProfileMigrationService.cs`: one-way JSON migration.
- `src/OrderedClicker/Services/DiagnosticLogService.cs`: application-wide diagnostics.
- `src/OrderedClicker/Core/ExecutionPlanFingerprintService.cs`: plan/profile consistency.
- `src/OrderedClicker/Core/PausableDelay.cs`: pause-aware timing.
- `src/OrderedClicker/Core/SafetyCornerService.cs`: corner validation and monitoring.
- `src/OrderedClicker/Core/ExecutionDurationEstimator.cs`: base/max duration.
- `src/OrderedClicker/Forms/WorkspaceStep.cs`: workspace step identity.
- `src/OrderedClicker/Forms/CaptureHudForm.cs`: always-on-top capture feedback.
- `src/OrderedClicker/Forms/CloudRegionOverlayForm.cs`: visual region calibration.
- `src/OrderedClicker/Forms/RunStatusForm.cs`: compact runtime control window.

Major modified files:

- `src/OrderedClicker/Models/ClickProfile.cs`
- `src/OrderedClicker/Models/AppSettings.cs`
- `src/OrderedClicker/Models/ExecutionCheckpoint.cs`
- `src/OrderedClicker/Models/ExecutionProgress.cs`
- `src/OrderedClicker/Services/ProfileService.cs`
- `src/OrderedClicker/Services/SettingsService.cs`
- `src/OrderedClicker/Services/WindowsScreenSampler.cs`
- `src/OrderedClicker/Core/ClickExecutionEngine.cs`
- `src/OrderedClicker/Core/ScreenStabilityDetector.cs`
- `src/OrderedClicker/Core/ProfileValidator.cs`
- `src/OrderedClicker/Forms/MainForm*.cs`
- `src/OrderedClicker/Forms/ExecutionPlanDialog.cs`
- build, installer, README, guide source files, and tests.

## Task 1: Baseline and Test Registration

**Files:**
- Modify: `tests/OrderedClicker.Tests/Program.cs`
- Create: test classes listed by later tasks.

- [ ] Run the current suite:

```powershell
.\scripts\dotnet.ps1 run --project .\tests\OrderedClicker.Tests\OrderedClicker.Tests.csproj -c Release
```

Expected: `12/12 tests passed`.

- [ ] Register each new test class in `Program.cs` before production implementation:

```csharp
("DraftService", () =>
{
    DraftServiceTests.Run();
    return Task.CompletedTask;
}),
("LegacyProfileMigration", () =>
{
    LegacyProfileMigrationServiceTests.Run();
    return Task.CompletedTask;
}),
("ExecutionSafety", ExecutionSafetyTests.RunAsync),
("AppDataPaths", () =>
{
    AppDataPathsTests.Run();
    return Task.CompletedTask;
})
```

- [ ] Run tests and verify compilation fails because the new test classes/types do not exist.

## Task 2: Installed and Portable Data Roots

**Files:**
- Create: `src/OrderedClicker/Services/AppDataPaths.cs`
- Create: `tests/OrderedClicker.Tests/AppDataPathsTests.cs`
- Modify: `src/OrderedClicker/Program.cs`
- Modify: constructors of `ProfileService`, `SettingsService`, and `ExecutionLogService`.

- [ ] Write tests proving `有序连点器-免安装.exe` maps to `<exeDir>\OrderedClickerData`, installed names map to LocalAppData, and an unwritable portable directory fails without fallback.
- [ ] Run tests and verify RED.
- [ ] Implement:

```csharp
public sealed record AppDataPaths(
    string Root,
    string Profiles,
    string Drafts,
    string Logs,
    string Diagnostics,
    bool IsPortable)
{
    public static AppDataPaths Resolve(string executablePath, string localAppData)
    {
        var portable = Path.GetFileNameWithoutExtension(executablePath)
            .Contains("免安装", StringComparison.OrdinalIgnoreCase);
        var root = portable
            ? Path.Combine(Path.GetDirectoryName(executablePath)!, "OrderedClickerData")
            : Path.Combine(localAppData, "OrderedClicker");
        return Create(root, portable);
    }
}
```

- [ ] Pass one shared `AppDataPaths` instance from `Program` to all persistence services.
- [ ] Run tests and verify GREEN.

## Task 3: v4 Profile Identity and Atomic File Semantics

**Files:**
- Modify: `src/OrderedClicker/Models/ClickProfile.cs`
- Modify: `src/OrderedClicker/Models/ClickPoint.cs`
- Modify: `src/OrderedClicker/Models/AppSettings.cs`
- Create: `src/OrderedClicker/Models/ProfileSaveOptions.cs`
- Modify: `src/OrderedClicker/Services/ProfileService.cs`
- Modify: `tests/OrderedClicker.Tests/ProfileServiceTests.cs`

- [ ] Write failing tests for `.oclick` enumeration, `FormatVersion = 4`, stable `ProfileId`, Save As identity renewal, arbitrary-path binding, overwrite backup, and no name-based silent overwrite.
- [ ] Run the `ProfileService` group and verify RED.
- [ ] Add identity and timestamps:

```csharp
public int FormatVersion { get; set; } = 4;
public Guid ProfileId { get; set; } = Guid.NewGuid();
public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
```

- [ ] Replace name-based default save with `GetAvailableProfilePath(name, profileId)` and require explicit overwrite for existing unrelated files.
- [ ] Add `SaveOptions(CreateBackup, AllowOverwrite, RenewIdentity)`.
- [ ] Save through same-directory temporary files, copy existing target to `.bak`, then atomically replace.
- [ ] Preserve existing cloning/equality behavior while excluding transient UI metadata.
- [ ] Run tests and verify GREEN.

## Task 4: Automatic Draft Recovery

**Files:**
- Create: `src/OrderedClicker/Services/DraftService.cs`
- Create: `tests/OrderedClicker.Tests/DraftServiceTests.cs`
- Modify: `src/OrderedClicker/Forms/MainForm.cs`
- Modify: `src/OrderedClicker/Forms/MainForm.Profiles.cs`

- [ ] Write failing tests for atomic save, source metadata, recovery detection, discard, and failed write preserving the previous draft.
- [ ] Run tests and verify RED.
- [ ] Implement:

```csharp
public sealed record DraftEnvelope(
    ClickProfile Profile,
    string? SourcePath,
    DateTime? SourceUpdatedAtUtc,
    DateTime DraftUpdatedAtUtc);
```

- [ ] Add a UI-thread debounce timer that snapshots the profile and calls `DraftService.Save`.
- [ ] Flush drafts before open, migration, mode switch, and close.
- [ ] Add startup recovery choice and preserve the source file until explicit Save.
- [ ] Run service and UI smoke tests and verify GREEN.

## Task 5: One-Way Legacy Migration

**Files:**
- Create: `src/OrderedClicker/Services/LegacyProfileMigrationService.cs`
- Create: `src/OrderedClicker/Services/LegacyMigrationResult.cs`
- Create: `tests/OrderedClicker.Tests/LegacyProfileMigrationServiceTests.cs`
- Modify: `src/OrderedClicker/Forms/MainForm.Profiles.cs`

- [ ] Write failing tests for v1-v3 conversion, invalid input, missing relative coordinates, source preservation, and new v4 identity.
- [ ] Run tests and verify RED.
- [ ] Deserialize legacy JSON into the existing compatible legacy DTO, map supported fields into a new v4 profile, and return warnings:

```csharp
public sealed record LegacyMigrationResult(
    bool Success,
    ClickProfile? Profile,
    IReadOnlyList<string> Warnings,
    string? Error);
```

- [ ] Replace “导入方案” with distinct “打开方案” and “迁移旧方案” commands.
- [ ] Save migrated output only after user chooses a new `.oclick` destination.
- [ ] Run tests and verify GREEN.

## Task 6: Settings Corruption and Diagnostic Logs

**Files:**
- Modify: `src/OrderedClicker/Services/SettingsService.cs`
- Create: `src/OrderedClicker/Services/DiagnosticLogService.cs`
- Create: `tests/OrderedClicker.Tests/DiagnosticAndSettingsTests.cs`
- Modify: `src/OrderedClicker/Program.cs`
- Modify: `src/OrderedClicker/Forms/MainForm*.cs`

- [ ] Write tests proving invalid settings move to `settings.<timestamp>.broken.json`, defaults are returned with a warning, and diagnostic exceptions include operation/version/type/stack.
- [ ] Run tests and verify RED.
- [ ] Return a `SettingsLoadResult` rather than silently swallowing invalid files.
- [ ] Add `DiagnosticLogService.Write(operation, exception, context)`.
- [ ] Route startup, settings, hot-key, profile, migration, calibration, and execution failures through diagnostics.
- [ ] Keep user messages concise and add “复制详情/打开日志目录”.
- [ ] Run tests and verify GREEN.

## Task 7: Immutable Plan Fingerprint and Duration Estimate

**Files:**
- Create: `src/OrderedClicker/Core/ExecutionPlanFingerprintService.cs`
- Create: `src/OrderedClicker/Core/ExecutionDurationEstimator.cs`
- Modify: `src/OrderedClicker/Models/ExecutionPlan.cs`
- Modify: `src/OrderedClicker/Core/ExecutionPlanService.cs`
- Create: `tests/OrderedClicker.Tests/ExecutionPlanSafetyTests.cs`

- [ ] Write tests showing any execution-relevant profile change changes the fingerprint while display-only metadata does not.
- [ ] Write tests for base and maximum duration calculations.
- [ ] Run tests and verify RED.
- [ ] Serialize a canonical execution-only DTO and hash it with SHA-256:

```csharp
public static string Create(ClickProfile profile)
{
    var payload = JsonSerializer.Serialize(CreateCanonicalProfile(profile));
    return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
}
```

- [ ] Store the fingerprint and duration estimate on `ExecutionPlan`.
- [ ] Centralize `InvalidateExecutionPlan()` in `MainForm` and call it from all relevant control, grid, calibration, and profile events.
- [ ] Run tests and verify GREEN.

## Task 8: Conservative Checkpoint Stages and Pause-Aware Delays

**Files:**
- Create: `src/OrderedClicker/Models/ExecutionStage.cs`
- Modify: `src/OrderedClicker/Models/ExecutionCheckpoint.cs`
- Create: `src/OrderedClicker/Core/PausableDelay.cs`
- Modify: `src/OrderedClicker/Core/ClickExecutionEngine.cs`
- Modify: `tests/OrderedClicker.Tests/ClickExecutionEngineTests.cs`

- [ ] Add failing tests for stopping during click interval, after-point delay, stability check, and loop delay; assert completed clicks are not repeated and incomplete barriers run again.
- [ ] Add failing tests proving pause freezes delay consumption.
- [ ] Run tests and verify RED.
- [ ] Extend checkpoint:

```csharp
public sealed record ExecutionCheckpoint(
    int LoopIndex,
    int PointIndex,
    int ClickIndex,
    ExecutionStage Stage,
    long CompletedPointExecutionCount,
    long CompletedClickCount);
```

- [ ] Implement `PausableDelay.WaitAsync(duration, pauseGate, progress, token)` using short monotonic slices and elapsed-time accounting.
- [ ] Update the engine state machine so checkpoint transitions happen at explicit stage boundaries.
- [ ] Ensure cancellation returns the current stage and `EnsureLeftButtonUp()` remains in `finally`.
- [ ] Run tests and verify GREEN.

## Task 9: Pause-Aware RGB Stability Detection

**Files:**
- Modify: `src/OrderedClicker/Services/WindowsScreenSampler.cs`
- Modify: `src/OrderedClicker/Core/ScreenStabilityDetector.cs`
- Modify: `src/OrderedClicker/Services/IScreenSampler.cs`
- Modify: `tests/OrderedClicker.Tests/ScreenStabilityDetectorTests.cs`

- [ ] Write failing tests for RGB-only differences, pause preventing sampling, progress reporting, timeout, and buffer-size stability.
- [ ] Run tests and verify RED.
- [ ] Return RGB samples rather than grayscale bytes.
- [ ] Use Win32 `StretchBlt` or equivalent direct scaled capture into a reusable maximum `160x90` buffer.
- [ ] Pass `AsyncPauseGate` and progress into stability detection.
- [ ] Calculate normalized RGB difference and expose elapsed/stable duration.
- [ ] Run tests and verify GREEN.

## Task 10: Dual Emergency Stop

**Files:**
- Create: `src/OrderedClicker/Models/SafetyCorner.cs`
- Create: `src/OrderedClicker/Models/SafetySettings.cs`
- Create: `src/OrderedClicker/Core/SafetyCornerService.cs`
- Modify: `src/OrderedClicker/Core/ProfileValidator.cs`
- Modify: `src/OrderedClicker/Forms/MainForm.Execution.cs`
- Create: `tests/OrderedClicker.Tests/ExecutionSafetyTests.cs`

- [ ] Write tests for corner bounds, point overlap, multi-monitor coordinates, monitor removal, and cancellation when the cursor enters the selected corner.
- [ ] Run tests and verify RED.
- [ ] Add safety settings to profiles and execution plans.
- [ ] Validate that a registered stop hot key and valid non-overlapping safety corner both exist before enabling Start.
- [ ] Run a lightweight safety monitor during countdown and execution; its cancellation joins the normal execution cancellation path.
- [ ] Correct hot-key failure messages and provide a “记录当前位置” fallback for capture.
- [ ] Run tests and verify GREEN.

## Task 11: Step-Based Main Workspace

**Files:**
- Create: `src/OrderedClicker/Forms/WorkspaceStep.cs`
- Modify: `src/OrderedClicker/Forms/MainForm.cs`
- Modify: `src/OrderedClicker/Forms/MainForm.Profiles.cs`
- Modify: `src/OrderedClicker/Forms/MainForm.CloudDesktop.cs`
- Modify: `src/OrderedClicker/Forms/MainForm.Execution.cs`
- Modify: `tests/OrderedClicker.Tests/UiSmokeTests.cs`

- [ ] Add failing UI smoke assertions for five navigation steps, central page host, persistent status panel, empty state, wrapping command bars, and renamed timing labels.
- [ ] Run UI smoke and verify RED.
- [ ] Build a three-column `TableLayoutPanel`: collapsible navigation, page host, collapsible status.
- [ ] Create pages for mode, plan, capture, check, and run/logs using existing controls where possible.
- [ ] Replace fixed non-wrapping bars with wrapping layouts and scrollable pages.
- [ ] Add dirty/check/complete status per step and navigation guards.
- [ ] Rename “点击间隔” to “同点连击间隔” and “点后等待” to “步骤完成后等待”.
- [ ] Run UI smoke and verify GREEN.

## Task 12: Capture HUD, Undo, Duplicate Detection, and Calibration Overlay

**Files:**
- Create: `src/OrderedClicker/Forms/CaptureHudForm.cs`
- Create: `src/OrderedClicker/Forms/CloudRegionOverlayForm.cs`
- Modify: `src/OrderedClicker/Forms/MainForm.cs`
- Modify: `src/OrderedClicker/Forms/MainForm.CloudDesktop.cs`
- Modify: `tests/OrderedClicker.Tests/UiSmokeTests.cs`
- Create: `tests/OrderedClicker.Tests/CaptureWorkflowTests.cs`

- [ ] Write failing tests for duplicate proximity detection, delete undo, recalibration preserving valid relative coordinates, missing-relative summary, and separate cancel/record calibration actions.
- [ ] Run tests and verify RED.
- [ ] Show an always-on-top HUD during capture with count, record, and finish controls.
- [ ] Add optional system sound and visible capture flash.
- [ ] Maintain a bounded undo stack for point deletion and clear operations.
- [ ] Replace two-hot-key corner capture with a visual rectangle overlay; keep keyboard capture as an accessibility alternative.
- [ ] Never recompute valid relative coordinates during recalibration.
- [ ] Run tests and verify GREEN.

## Task 13: Execution Check, Runtime Window, and Log Actions

**Files:**
- Modify: `src/OrderedClicker/Forms/ExecutionPlanDialog.cs`
- Create: `src/OrderedClicker/Forms/RunStatusForm.cs`
- Modify: `src/OrderedClicker/Forms/MainForm.Execution.cs`
- Modify: `src/OrderedClicker/Models/ExecutionProgress.cs`
- Modify: `src/OrderedClicker/Services/ExecutionLogService.cs`
- Modify: `tests/OrderedClicker.Tests/UiSmokeTests.cs`
- Modify: `tests/OrderedClicker.Tests/ExecutionReliabilityServiceTests.cs`

- [ ] Write failing tests for duration text, safety failures, current-stage progress, log-failure preservation, and returned log path.
- [ ] Run tests and verify RED.
- [ ] Replace the plain plan dialog body with grouped validation results, base/max duration, click totals, and single-loop trial action.
- [ ] Extend progress with stage, elapsed stage time, remaining duration, and stability difference.
- [ ] Show a topmost runtime form with pause/continue/stop.
- [ ] Store the successful log path and expose open/copy actions.
- [ ] Merge log-write failure into the final completion result instead of overwriting it.
- [ ] Run tests and verify GREEN.

## Task 14: Responsive Settings, Modal Hot Keys, and Help

**Files:**
- Modify: `src/OrderedClicker/Forms/MainForm.Settings.cs`
- Modify: `src/OrderedClicker/Forms/MainForm.Help.cs`
- Modify: `src/OrderedClicker/Forms/HotKeyInput.cs`
- Modify: `tests/OrderedClicker.Tests/UiSmokeTests.cs`

- [ ] Write failing UI assertions for settings tabs, resizable dialogs, scroll support, inline hot-key conflicts, and renamed presets.
- [ ] Run UI smoke and verify RED.
- [ ] Split settings into 快捷键、安全、界面、数据目录、诊断 tabs.
- [ ] Rename presets to 单键快捷键 and 组合快捷键.
- [ ] Suspend capture/start hot-key actions around every modal dialog and file picker; allow stop only while a task is active.
- [ ] Add buttons for profiles, drafts, logs, diagnostics, and copying environment details.
- [ ] Replace dense help text with step sections matching the new workspace.
- [ ] Run tests and verify GREEN.

## Task 15: Version, Packaging, Signing, Checksums, and Guides

**Files:**
- Modify: `src/OrderedClicker/OrderedClicker.csproj`
- Modify: `scripts/publish.ps1`
- Modify: `scripts/build-installer.ps1`
- Modify: `installer/OrderedClicker.iss`
- Modify: `tests/Installer.Tests.ps1`
- Modify: `README.md`
- Modify: `scripts/guide/*`
- Modify: `操作指导/*`

- [ ] Add failing installer contract assertions for `2.0.0`, `有序连点器-免安装.exe`, `SHA256SUMS.txt`, and release signing requirements.
- [ ] Run installer tests and verify RED.
- [ ] Update all version defaults to `2.0.0`.
- [ ] Copy the portable executable under the required Chinese name and keep installer payload as `OrderedClicker.exe`.
- [ ] Generate SHA-256 entries for every delivery file.
- [ ] Add `-Release`, `-SigningCertificatePath`, secure password input/environment source, and timestamp URL parameters.
- [ ] In Release mode sign the app before packaging, sign the installer after compilation, and verify both signatures.
- [ ] Update README, HTML/PDF generators, video content, subtitles, and narration to the new workflow and file names.
- [ ] Run installer and guide verification and verify GREEN.

## Task 16: Full Verification and Delivery

**Files:**
- Modify only defects found by verification.

- [ ] Run formatting checks:

```powershell
git diff --check
```

- [ ] Run all application tests:

```powershell
.\scripts\dotnet.ps1 run --project .\tests\OrderedClicker.Tests\OrderedClicker.Tests.csproj -c Release
```

- [ ] Run installer contracts:

```powershell
.\tests\Installer.Tests.ps1
```

- [ ] Build Release:

```powershell
.\scripts\dotnet.ps1 build .\OrderedClicker.sln -c Release '-m:1'
```

- [ ] Render main, settings, help, capture HUD, calibration, and runtime states at minimum size and inspect for clipping.
- [ ] Run unsigned development package build and verify the five expected delivery files.
- [ ] If a signing certificate is available, run Release package build and verify valid Authenticode signatures and timestamps.
- [ ] Confirm Git status contains only intended source, tests, scripts, docs, and generated guide changes.
- [ ] Commit implementation in coherent phase commits, then push `main` after final verification.
