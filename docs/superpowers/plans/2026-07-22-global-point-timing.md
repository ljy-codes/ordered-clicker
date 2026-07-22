# Global Point Timing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add independent global click-interval and after-delay controls that update all existing points and provide defaults for newly captured points.

**Architecture:** Store the two defaults in `ClickProfile`, centralize batch update and legacy-profile resolution in a small `PointTimingService`, and keep the execution engine unchanged. `MainForm` owns the controls and applies the service results to its binding list.

**Tech Stack:** C# 14, .NET 10 Windows Forms, custom console test runner, PowerShell 7, Inno Setup 7, HTML/PDF guide generator.

---

### Task 1: Point timing behavior

**Files:**
- Create: `src/OrderedClicker/Core/PointTimingService.cs`
- Create: `tests/OrderedClicker.Tests/PointTimingServiceTests.cs`
- Modify: `tests/OrderedClicker.Tests/Program.cs`

- [ ] **Step 1: Write failing tests**

Cover these behaviors with real `ClickPoint` instances:

```csharp
PointTimingService.ApplyClickInterval(points, 1000);
PointTimingService.ApplyAfterDelay(points, 750);
var defaults = PointTimingService.ResolveDefaults(legacyProfile);
```

Assert that each batch operation changes only its target property and that a version 1 profile resolves defaults from its first point.

- [ ] **Step 2: Run tests and verify RED**

Run:

```powershell
.\scripts\dotnet.ps1 run --project .\tests\OrderedClicker.Tests\OrderedClicker.Tests.csproj -c Release
```

Expected: compilation fails because `PointTimingService` does not exist.

- [ ] **Step 3: Implement the service**

Create a static service with:

```csharp
public static void ApplyClickInterval(IEnumerable<ClickPoint> points, int value)
public static void ApplyAfterDelay(IEnumerable<ClickPoint> points, int value)
public static (int ClickIntervalMs, int AfterDelayMs) ResolveDefaults(ClickProfile profile)
```

For profile version 2 use stored defaults. For version 1 use the first point or model defaults when no point exists.

- [ ] **Step 4: Run tests and verify GREEN**

Run the full test executable and expect all registered suites to pass.

### Task 2: Profile persistence

**Files:**
- Modify: `src/OrderedClicker/Models/ClickProfile.cs`
- Modify: `tests/OrderedClicker.Tests/ProfileServiceTests.cs`

- [ ] **Step 1: Write failing round-trip test**

Save and load a version 2 profile containing:

```csharp
DefaultClickIntervalMs = 1234,
DefaultAfterDelayMs = 5678
```

Assert both values survive serialization.

- [ ] **Step 2: Run tests and verify RED**

Expected: compilation failure because the properties do not exist.

- [ ] **Step 3: Add model properties**

Add properties with defaults:

```csharp
public int DefaultClickIntervalMs { get; set; } = 100;
public int DefaultAfterDelayMs { get; set; } = 500;
```

- [ ] **Step 4: Run tests and verify GREEN**

Run the full test executable and expect all suites to pass.

### Task 3: Main window controls

**Files:**
- Modify: `src/OrderedClicker/Forms/MainForm.cs`
- Modify: `src/OrderedClicker/Forms/MainForm.Execution.cs`
- Modify: `tests/OrderedClicker.Tests/UiSmokeTests.cs`

- [ ] **Step 1: Write failing UI smoke tests**

Assert the form contains unique named controls:

```text
DefaultClickIntervalInput
ApplyClickIntervalButton
DefaultAfterDelayInput
ApplyAfterDelayButton
```

Load test points, click each apply button, and assert only the matching property changes. Also verify controls remain inside the minimum-size window.

- [ ] **Step 2: Run tests and verify RED**

Expected: UI smoke failure because the controls are absent.

- [ ] **Step 3: Build timing toolbar**

Add a 52-pixel row between the profile panel and grid. Configure numeric limits to match `ProfileValidator`. Give each timing field its own “应用全部” button and tooltip.

- [ ] **Step 4: Wire behavior**

On apply:

```csharp
CommitGridChanges();
PointTimingService.ApplyClickInterval(_points, value);
_pointGrid.Refresh();
```

Use the equivalent method for after-delay. In `CaptureCurrentPoint`, copy current global values into the new `ClickPoint`.

- [ ] **Step 5: Persist and restore defaults**

Save profile version 2 and the two default values. During load call `PointTimingService.ResolveDefaults(profile)` before binding points.

- [ ] **Step 6: Disable controls while running**

Include both inputs and both buttons in `SetConfigurationEnabled`.

- [ ] **Step 7: Run tests and verify GREEN**

Run the full suite and render the main UI at default and minimum sizes.

### Task 4: Help and product guide

**Files:**
- Modify: `src/OrderedClicker/Forms/MainForm.Help.cs`
- Modify: `操作指导/有序连点器-操作指导PRD.html`
- Modify: `scripts/guide/verify_guide.py`

- [ ] **Step 1: Extend help tests**

Update the help smoke test to require “应用全部” and “新采集点” in the dialog text. Run it and verify failure.

- [ ] **Step 2: Update in-app help**

Explain the two independent global settings, inheritance for new points, and per-row overrides.

- [ ] **Step 3: Update HTML guide**

Add the batch timing toolbar to the interface description and include a beginner workflow:

1. Set a global time.
2. Click the corresponding “应用全部”.
3. Adjust exceptional rows directly.
4. Capture new points using the current defaults.

- [ ] **Step 4: Regenerate PDF**

Run:

```powershell
python .\scripts\guide\build_product_guide.py
```

Then run:

```powershell
python .\scripts\guide\verify_guide.py
```

Expected: HTML and PDF verification succeeds. Do not regenerate the MP4.

### Task 5: Version, public statement, and build

**Files:**
- Modify: `src/OrderedClicker/OrderedClicker.csproj`
- Modify: `installer/OrderedClicker.iss`
- Modify: `scripts/build-installer.ps1`
- Modify: `scripts/publish.ps1`
- Modify: `README.md`

- [ ] **Step 1: Set defaults to 1.1.0**

Update project, installer, and build-script default version values from `1.0.1` to `1.1.0`.

- [ ] **Step 2: Update README**

Document the global time controls and state clearly that the public application is free, has no authorization code, has no device/time restriction, and requires no activation server.

- [ ] **Step 3: Run full verification**

Run:

```powershell
.\scripts\dotnet.ps1 build .\OrderedClicker.sln -c Release '-m:1'
.\scripts\dotnet.ps1 run --project .\tests\OrderedClicker.Tests\OrderedClicker.Tests.csproj -c Release
pwsh -NoProfile -File .\tests\Installer.Tests.ps1
```

Expected: build succeeds and every test passes.

- [ ] **Step 4: Build final product**

Run:

```powershell
pwsh -NoProfile -File .\scripts\build-installer.ps1 `
  -Version 1.1.0 `
  -ProductDirectory "D:\专用工具\连接器产品" `
  -GuideSourceDirectory "D:\专用工具\连点器\操作指导"
```

Expected product files:

```text
ordered-clicker-setup-v1.1.0.exe
ordered-clicker-portable-v1.1.0.zip
有序连点器-使用说明.pdf
有序连点器-使用说明.html
有序连点器-视频演示.mp4
SHA256SUMS.txt
```

### Task 6: GitHub synchronization

**Files:**
- Modify: repository history and GitHub Release `v1.1.0`

- [ ] **Step 1: Review changes**

Run `git diff --check`, inspect `git status`, and confirm no authorization inventory or private repository material is tracked.

- [ ] **Step 2: Commit feature**

Commit source, tests, docs, and version updates with a focused feature commit.

- [ ] **Step 3: Push main and tag**

Push `main`, create annotated tag `v1.1.0`, and push the tag.

- [ ] **Step 4: Create GitHub Release**

Upload installer, portable ZIP, PDF, HTML, video, and checksum file to Release `v1.1.0`.

- [ ] **Step 5: Verify public download**

Open the public Release page and verify all six assets are visible.
