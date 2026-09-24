# Ordered Clicker 2.1 Reliability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Release Ordered Clicker 2.1.0 with responsive emergency stopping, durable checkpoints, single-instance protection, lower UI overhead, bounded logs, trustworthy tests, and refreshed delivery artifacts.

**Architecture:** Add small non-UI coordinators for checkpoint persistence, latest-progress buffering, safety monitoring, single-instance signaling, delayed action cancellation, and log retention. Integrate them into the existing partial `MainForm` without changing the `.oclick` v4 contract or restructuring unrelated UI code.

**Tech Stack:** C# 14, .NET 10 Windows Forms, PowerShell 7, Inno Setup 7, GitHub Actions.

---

### Task 1: Make the test command trustworthy

**Files:**
- Create: `scripts/test.ps1`
- Modify: `tests/OrderedClicker.Tests/OrderedClicker.Tests.csproj`
- Modify: `README.md`

- [ ] **Step 1: Capture the failing baseline**

Run:

```powershell
dotnet test OrderedClicker.sln -c Release --no-restore -v normal
```

Expected: command succeeds without printing any `PASS` lines or `17/17 tests passed`.

- [ ] **Step 2: Add a repository test entry point**

Create `scripts/test.ps1` that restores when requested, builds the test executable, runs it with `dotnet run --no-build`, propagates the exit code, and fails when the summary line is missing.

- [ ] **Step 3: Wire the test project into `dotnet test`**

Add an explicit `VSTest` target to `OrderedClicker.Tests.csproj`:

```xml
<Target Name="VSTest" DependsOnTargets="Build">
  <Exec Command="dotnet &quot;$(TargetPath)&quot;" />
</Target>
```

The target must run the built test executable and propagate failures without adding external test packages.

- [ ] **Step 4: Verify red-to-green**

Run:

```powershell
dotnet test OrderedClicker.sln -c Release --no-restore -v normal
pwsh -File scripts/test.ps1 -Configuration Release -NoRestore
```

Expected: both commands print all named tests and `17/17 tests passed`.

### Task 2: Add durable execution checkpoints

**Files:**
- Create: `src/OrderedClicker/Models/ExecutionCheckpointEnvelope.cs`
- Create: `src/OrderedClicker/Services/ExecutionCheckpointService.cs`
- Create: `tests/OrderedClicker.Tests/ExecutionCheckpointServiceTests.cs`
- Modify: `tests/OrderedClicker.Tests/Program.cs`
- Modify: `src/OrderedClicker/Forms/MainForm.cs`
- Modify: `src/OrderedClicker/Forms/MainForm.Execution.cs`

- [ ] **Step 1: Write failing checkpoint service tests**

Tests must verify:

```csharp
var service = new ExecutionCheckpointService(tempDirectory);
service.Save(new ExecutionCheckpointEnvelope(1, "2.1.0", plan, plan.ProfileFingerprint, checkpoint, now));
var loaded = service.Load();
TestAssert.Equal(checkpoint, loaded!.Checkpoint, "断点应完整往返");
service.Clear();
TestAssert.True(service.Load() is null, "清理后不应存在断点");
```

Also write invalid JSON to the checkpoint path and verify `QuarantineBrokenCheckpoint()` renames it to `.broken.json`.

- [ ] **Step 2: Run the new test and confirm failure**

Run:

```powershell
dotnet run --project tests/OrderedClicker.Tests/OrderedClicker.Tests.csproj -c Release --no-restore
```

Expected: compile failure because `ExecutionCheckpointService` does not exist.

- [ ] **Step 3: Implement atomic checkpoint persistence**

`ExecutionCheckpointService` must:

- Store `execution-checkpoint.json` under `AppDataPaths.Drafts`.
- Serialize enums as strings and preserve all `ExecutionPlan` fields.
- Use a GUID temporary file and atomic `File.Move(..., true)`.
- Return `null` when no checkpoint exists.
- Validate format version and plan fingerprint.
- Quarantine unreadable data with a timestamped `.broken.json` suffix.

- [ ] **Step 4: Integrate checkpoint lifecycle**

`MainForm` must load a valid checkpoint during `OnShown`, ask whether to restore it, and discard it when declined. `PreserveExecutionCheckpoint` saves immediately for stop/failure and schedules coalesced saves during progress. `ClearExecutionCheckpoint` deletes the file.

- [ ] **Step 5: Verify**

Run the complete test program and manually kill a debug run after progress has been written. Restart and verify the recovery prompt appears.

### Task 3: Coalesce progress and decouple the safety stop

**Files:**
- Create: `src/OrderedClicker/Core/LatestExecutionProgress.cs`
- Create: `src/OrderedClicker/Core/SafetyCornerWatchdog.cs`
- Create: `tests/OrderedClicker.Tests/ExecutionRuntimeCoordinatorTests.cs`
- Modify: `tests/OrderedClicker.Tests/Program.cs`
- Modify: `src/OrderedClicker/Forms/MainForm.cs`
- Modify: `src/OrderedClicker/Forms/MainForm.Execution.cs`

- [ ] **Step 1: Write failing coordinator tests**

Verify `LatestExecutionProgress.Report` overwrites previous values and `TryConsume` returns only the latest item once. Verify `SafetyCornerWatchdog` invokes its stop callback after continuous dwell and stops after cancellation.

- [ ] **Step 2: Run and confirm the tests fail because the new types are missing**

- [ ] **Step 3: Implement the coordinators**

Use `Interlocked.Exchange` for the latest progress buffer. Use a cancellable background loop with a 50ms delay for the safety watcher. The watcher receives delegates for cursor position, screen bounds, settings, and stop callback so it is testable without Windows UI.

- [ ] **Step 4: Replace per-click UI posting**

Pass `LatestExecutionProgress` directly to `ClickExecutionEngine`. Add a 100ms WinForms timer that consumes and renders the latest progress. When `RequiresUserContinue` is true, marshal an immediate render with `BeginInvoke`.

- [ ] **Step 5: Replace the WinForms safety timer**

Remove `_safetyCornerTimer`. Start the watchdog only while execution is in countdown/running/paused states. Its callback cancels `_executionCancellation` before posting UI updates.

- [ ] **Step 6: Verify**

Run the test suite and a 10ms interval plan. Confirm the displayed progress remains responsive and emergency stop cancels promptly.

### Task 4: Wait for execution during window close

**Files:**
- Modify: `src/OrderedClicker/Forms/MainForm.cs`
- Modify: `src/OrderedClicker/Forms/MainForm.Execution.cs`
- Modify: `tests/OrderedClicker.Tests/UiSmokeTests.cs`

- [ ] **Step 1: Add a failing structural UI test**

Use reflection to assert `MainForm` contains `_activeExecutionTask`, `_allowClose`, and an asynchronous close helper.

- [ ] **Step 2: Track active execution**

Assign the full execution lifecycle task to `_activeExecutionTask`. Ensure repeated start requests reuse or reject while the task is active.

- [ ] **Step 3: Implement graceful close**

On first close while active, cancel the event, call `StopExecution`, await `_activeExecutionTask`, flush checkpoint/draft, dispose services, set `_allowClose`, and call `Close()` again.

- [ ] **Step 4: Verify**

Run tests and manually close during countdown, running, paused, and stopping states.

### Task 5: Enforce single-instance execution

**Files:**
- Create: `src/OrderedClicker/Services/SingleInstanceCoordinator.cs`
- Create: `tests/OrderedClicker.Tests/SingleInstanceCoordinatorTests.cs`
- Modify: `tests/OrderedClicker.Tests/Program.cs`
- Modify: `src/OrderedClicker/Program.cs`
- Modify: `src/OrderedClicker/Forms/MainForm.cs`
- Modify: `src/OrderedClicker/Services/DraftService.cs`
- Modify: `src/OrderedClicker/Services/SettingsService.cs`

- [ ] **Step 1: Write failing tests**

Create two coordinators with the same identifier. Assert the first owns the instance, the second does not, and the first receives an activation signal. Dispose both and assert a third coordinator can become owner.

- [ ] **Step 2: Implement named Mutex plus named EventWaitHandle**

Names must include a stable SHA-256-derived user/application suffix and use the local session namespace. The listener must be cancellable and dispose all handles.

- [ ] **Step 3: Integrate startup and activation**

`Program.Main` exits after signaling when it is not the owner. The owner wires activation to `MainForm.ActivateExistingInstance`, restoring, showing, activating, and briefly setting `TopMost` when required.

- [ ] **Step 4: Harden temporary files**

Change draft and settings temporary paths to include a GUID and preserve cleanup in `finally`.

- [ ] **Step 5: Verify**

Start two release builds. Confirm only one main window exists and the first window is brought forward.

### Task 6: Make delayed capture cancellable and draft saving event-driven

**Files:**
- Create: `src/OrderedClicker/Core/ReplaceableDelay.cs`
- Create: `tests/OrderedClicker.Tests/ReplaceableDelayTests.cs`
- Modify: `tests/OrderedClicker.Tests/Program.cs`
- Modify: `src/OrderedClicker/Forms/MainForm.cs`
- Modify: `src/OrderedClicker/Forms/MainForm.Profiles.cs`
- Modify: `src/OrderedClicker/Forms/MainForm.Settings.cs`

- [ ] **Step 1: Write failing replaceable-delay tests**

Schedule two actions before the first delay expires. Assert only the second action runs. Cancel and assert no action runs.

- [ ] **Step 2: Implement `ReplaceableDelay`**

Use a lock-protected `CancellationTokenSource`. Every `RunAsync` cancels and disposes the previous source. `Cancel` is idempotent.

- [ ] **Step 3: Integrate delayed capture**

Disable the delayed capture button during countdown, update its text once per second, cancel on finish/start/close, and guarantee one `CaptureCurrentPoint` call.

- [ ] **Step 4: Replace draft comparison polling**

Add `_draftDirty`. Subscribe to point list changes and relevant input events. The 800ms timer returns immediately unless dirty, then saves one snapshot. Remove `_lastDraftSnapshot` and `ProfilesEqual` from the timer path.

- [ ] **Step 5: Verify**

Run tests, repeatedly click delayed capture, and edit a 1000-point profile while observing that idle timer ticks do not serialize unchanged data.

### Task 7: Bound execution and diagnostic logs

**Files:**
- Create: `src/OrderedClicker/Services/LogRetentionService.cs`
- Create: `tests/OrderedClicker.Tests/LogRetentionServiceTests.cs`
- Modify: `tests/OrderedClicker.Tests/Program.cs`
- Modify: `src/OrderedClicker/Services/ExecutionLogService.cs`
- Modify: `src/OrderedClicker/Services/DiagnosticLogService.cs`

- [ ] **Step 1: Write failing retention tests**

Create timestamped files that exceed age, count, and byte limits. Verify cleanup removes oldest files first and never removes non-JSON files.

- [ ] **Step 2: Implement retention**

Expose:

```csharp
public sealed record LogRetentionPolicy(TimeSpan MaximumAge, int MaximumFiles, long MaximumBytes);
public int Prune(string directory, LogRetentionPolicy policy, DateTimeOffset now);
```

Use creation/write time ordering, swallow only individual file race exceptions, and validate positive limits.

- [ ] **Step 3: Integrate after successful writes**

Use the default 30-day, 1000-file, 100MB policy. Cleanup failures must not invalidate the log that was just written.

- [ ] **Step 4: Verify**

Run the test suite and inspect a temporary directory containing files over each limit.

### Task 8: Version, CI, documentation, and delivery artifacts

**Files:**
- Create: `.github/workflows/release.yml`
- Modify: `src/OrderedClicker/OrderedClicker.csproj`
- Modify: `installer/OrderedClicker.iss`
- Modify: `scripts/publish.ps1`
- Modify: `scripts/build-installer.ps1`
- Modify: `scripts/guide/build_product_guide.py`
- Modify: `scripts/guide/build_pdf_guide.py`
- Modify: `scripts/guide/verify_guide.py`
- Modify: `src/OrderedClicker/Forms/MainForm.Help.cs`
- Modify: `README.md`
- Modify: `交付产品/*`

- [ ] **Step 1: Change all product version references from 2.0.0 to 2.1.0**

Keep profile format at v4 and settings format unchanged.

- [ ] **Step 2: Add the Windows CI and release workflow**

On pushes and pull requests, run `scripts/test.ps1` and build. On `v*` tags, build packages, verify `SHA256SUMS.txt`, and upload package artifacts to the GitHub Release. Signing uses secrets only when configured.

- [ ] **Step 3: Rebuild guides and packages**

Run:

```powershell
pwsh -File scripts/build-installer.ps1 -Version 2.1.0 -ProductDirectory "D:\专用工具\连点器\交付产品"
```

Use the configured Inno Setup path. Do not claim a signed release unless Authenticode verification reports `Valid`.

- [ ] **Step 4: Full verification**

Run:

```powershell
pwsh -File scripts/test.ps1 -Configuration Release
dotnet build OrderedClicker.sln -c Release
pwsh -File tests/Installer.Tests.ps1 -Version 2.1.0
git diff --check
```

Verify file versions, package contents, checksums, guide references, and a clean startup smoke test.

- [ ] **Step 5: Review, commit, merge, tag, push, and release**

Review the complete diff for unrelated changes. Commit implementation, merge `codex/v2.1.0` into `main`, tag `v2.1.0`, push branch/main/tag, create or update GitHub Release, and upload the verified artifacts.
