# Configurable Global Hotkeys Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace fixed F8/F9/F10 hotkeys with configurable global shortcuts defaulting to Ctrl+Alt+F8/F9/F10, including conflict detection, rollback, dynamic UI text, documentation, and a 1.2.0 release.

**Architecture:** Store serializable shortcut definitions in `AppSettings`, validate and format them in a focused core service, and keep native registration behind an injectable registrar. The main form applies all three registrations as one transaction: if any new registration fails, it releases partial registrations and restores the previous set.

**Tech Stack:** C# 14, .NET 10 Windows Forms, Win32 `RegisterHotKey`, JSON settings, PowerShell release scripts, Python ReportLab guide generator.

---

## File Structure

- Create `src/OrderedClicker/Models/HotKeyBinding.cs`: serializable shortcut value and modifier enum.
- Create `src/OrderedClicker/Core/HotKeyBindingService.cs`: defaults, validation, duplicate checks, and display formatting.
- Modify `src/OrderedClicker/Models/AppSettings.cs`: persist three shortcut bindings with backward-compatible defaults.
- Modify `src/OrderedClicker/Services/HotKeyService.cs`: register model bindings and support explicit unregister operations.
- Create `src/OrderedClicker/Services/HotKeyRegistrationCoordinator.cs`: transactional replacement and rollback.
- Create `src/OrderedClicker/Forms/HotKeyInput.cs`: focused shortcut capture control.
- Modify `src/OrderedClicker/Forms/MainForm.cs`: load current settings, register configured bindings, and display dynamic shortcut text.
- Modify `src/OrderedClicker/Forms/MainForm.Settings.cs`: add shortcut inputs, restore-default action, save validation, and registration rollback.
- Modify `src/OrderedClicker/Forms/MainForm.Execution.cs`: dynamic start/pause/stop labels.
- Modify `src/OrderedClicker/Forms/MainForm.Help.cs`: dynamic help text.
- Create `tests/OrderedClicker.Tests/HotKeyBindingServiceTests.cs`: settings, validation, formatting, and rollback tests.
- Modify `tests/OrderedClicker.Tests/ThemeSettingsTests.cs`: settings backward compatibility and round-trip coverage.
- Modify `tests/OrderedClicker.Tests/UiSmokeTests.cs`: settings controls and dynamic shortcut text.
- Modify `tests/OrderedClicker.Tests/Program.cs`: run the new test group.
- Modify release/version/docs files: `README.md`, project file, installer, scripts, HTML/PDF guide sources and generated artifacts.

### Task 1: Shortcut Model And Validation

**Files:**
- Create: `src/OrderedClicker/Models/HotKeyBinding.cs`
- Create: `src/OrderedClicker/Core/HotKeyBindingService.cs`
- Create: `tests/OrderedClicker.Tests/HotKeyBindingServiceTests.cs`
- Modify: `tests/OrderedClicker.Tests/Program.cs`

- [ ] **Step 1: Write failing tests**

Cover these exact cases:

```csharp
TestAssert.Equal("Ctrl+Alt+F8", HotKeyBindingService.Format(HotKeyBindingService.DefaultCapture));
TestAssert.True(HotKeyBindingService.Validate(binding, out _), "带修饰键的快捷键应有效");
TestAssert.True(!HotKeyBindingService.Validate(new(Keys.F8, ShortcutModifiers.None), out _),
    "单独功能键应被拒绝");
TestAssert.True(!HotKeyBindingService.ValidateSet(capture, capture, stop, out _),
    "三个操作不能配置重复快捷键");
```

- [ ] **Step 2: Run tests and confirm failure**

Run:

```powershell
.\scripts\dotnet.ps1 run --project tests\OrderedClicker.Tests\OrderedClicker.Tests.csproj
```

Expected: compilation fails because the shortcut types do not exist.

- [ ] **Step 3: Implement the model and service**

Define `ShortcutModifiers` as flags for `Control`, `Alt`, `Shift`, and `Windows`. Define `HotKeyBinding` with `Keys Key` and `ShortcutModifiers Modifiers`. Provide defaults and deterministic display ordering `Ctrl`, `Alt`, `Shift`, `Win`, key.

Validation must reject:

- no modifier;
- modifier-only keys;
- `Keys.None`;
- duplicate action bindings.

- [ ] **Step 4: Run tests**

Expected: new hotkey model tests pass and existing tests remain green.

### Task 2: Settings Compatibility

**Files:**
- Modify: `src/OrderedClicker/Models/AppSettings.cs`
- Modify: `src/OrderedClicker/Services/SettingsService.cs`
- Modify: `tests/OrderedClicker.Tests/ThemeSettingsTests.cs`

- [ ] **Step 1: Add failing settings tests**

Test that:

```csharp
service.Save(new AppSettings {
    Theme = AppThemeId.Emerald,
    CaptureHotKey = new HotKeyBinding(Keys.F6, ShortcutModifiers.Control | ShortcutModifiers.Shift)
});
```

round-trips, and a legacy JSON document such as:

```json
{ "theme": "light" }
```

loads `Ctrl+Alt+F8/F9/F10`.

- [ ] **Step 2: Run tests and confirm failure**

Expected: compilation failure for missing properties.

- [ ] **Step 3: Add shortcut properties and normalization**

Use property initializers for defaults so missing legacy JSON fields are automatically populated. When a stored binding is invalid or the set contains duplicates, retain the loaded theme and replace all shortcuts with defaults.

- [ ] **Step 4: Run tests**

Expected: settings tests pass.

### Task 3: Transactional Native Registration

**Files:**
- Modify: `src/OrderedClicker/Services/HotKeyService.cs`
- Create: `src/OrderedClicker/Services/HotKeyRegistrationCoordinator.cs`
- Modify: `tests/OrderedClicker.Tests/HotKeyBindingServiceTests.cs`

- [ ] **Step 1: Add a failing rollback test**

Use a fake registrar that fails on the second new binding. Verify the event order contains:

```text
unregister old
register new capture
register new start -> failure
unregister partial new
register old capture
register old start
register old stop
```

and verify the coordinator reports failure without replacing its current bindings.

- [ ] **Step 2: Run tests and confirm failure**

Expected: compilation failure because the coordinator and registrar interface do not exist.

- [ ] **Step 3: Implement registrar abstraction and coordinator**

`HotKeyService` maps model modifiers to Win32 modifiers and always adds `NoRepeat`. The coordinator owns the active registrations and exposes:

```csharp
HotKeyRegistrationResult RegisterInitial(IReadOnlyList<HotKeyRegistration> bindings);
HotKeyRegistrationResult Replace(IReadOnlyList<HotKeyRegistration> bindings);
```

`Replace` must restore the previous complete set after any failure.

- [ ] **Step 4: Run tests**

Expected: rollback test and all existing tests pass.

### Task 4: Settings UI And Dynamic Main Form Text

**Files:**
- Create: `src/OrderedClicker/Forms/HotKeyInput.cs`
- Modify: `src/OrderedClicker/Forms/MainForm.cs`
- Modify: `src/OrderedClicker/Forms/MainForm.Settings.cs`
- Modify: `src/OrderedClicker/Forms/MainForm.Execution.cs`
- Modify: `src/OrderedClicker/Forms/MainForm.Help.cs`
- Modify: `tests/OrderedClicker.Tests/UiSmokeTests.cs`

- [ ] **Step 1: Add failing UI tests**

Verify that the settings dialog contains controls named:

```text
CaptureHotKeyInput
StartPauseHotKeyInput
StopHotKeyInput
RestoreDefaultHotKeysButton
```

and that the initial main form text contains `Ctrl+Alt+F8`, `Ctrl+Alt+F9`, and `Ctrl+Alt+F10`.

- [ ] **Step 2: Run tests and confirm failure**

Expected: UI smoke tests fail because the controls and dynamic labels are absent.

- [ ] **Step 3: Implement shortcut capture and settings layout**

`HotKeyInput` is read-only, captures `KeyDown`, ignores bare modifiers, stores a `HotKeyBinding`, and displays formatted text. Extend the current settings dialog with a global-hotkeys section and a “恢复默认快捷键” button.

- [ ] **Step 4: Implement save and rollback flow**

On OK:

1. validate three bindings;
2. attempt coordinator replacement when global hotkeys are enabled;
3. save the complete `AppSettings`;
4. if persistence fails, restore old registration and settings;
5. update all main-form shortcut text.

- [ ] **Step 5: Replace hard-coded shortcut strings**

Generate capture, start/pause, stop, status strip, tooltip, execution status, and help text from the active settings.

- [ ] **Step 6: Run tests and render UI**

Run:

```powershell
.\scripts\dotnet.ps1 run --project tests\OrderedClicker.Tests\OrderedClicker.Tests.csproj
.\scripts\dotnet.ps1 run --project tests\OrderedClicker.Tests\OrderedClicker.Tests.csproj -- --render-ui artifacts\ordered-clicker-ui-v1.2.0.png
.\scripts\dotnet.ps1 run --project tests\OrderedClicker.Tests\OrderedClicker.Tests.csproj -- --render-settings artifacts\ordered-clicker-settings-v1.2.0.png
```

Expected: all tests pass and screenshots show non-overlapping shortcut controls.

### Task 5: Version, Documentation, And Product Build

**Files:**
- Modify: `src/OrderedClicker/OrderedClicker.csproj`
- Modify: `installer/OrderedClicker.iss`
- Modify: `scripts/publish.ps1`
- Modify: `scripts/build-installer.ps1`
- Modify: `README.md`
- Modify: `操作指导/有序连点器-深度操作指导.html`
- Modify: `scripts/guide/build_product_guide.py`
- Modify: `scripts/guide/build_pdf_guide.py`
- Regenerate: `操作指导/有序连点器-使用说明.html`
- Regenerate: `操作指导/有序连点器-使用说明.pdf`

- [ ] **Step 1: Update version contracts to 1.2.0**

Replace release filenames and assembly/package versions with `1.2.0`. Add installer contract expectations for the new version.

- [ ] **Step 2: Update user guidance**

Document default shortcuts, how to change them, conflict messages, restore-default behavior, and the fact that interface buttons remain available.

- [ ] **Step 3: Regenerate guides**

Run:

```powershell
python scripts\guide\build_product_guide.py
python scripts\guide\build_pdf_guide.py
python scripts\guide\verify_guide.py --skip-video
```

Expected: HTML and PDF verification succeeds and the unchanged video remains accepted.

- [ ] **Step 4: Build release packages**

Run:

```powershell
.\scripts\build-installer.ps1 -Version 1.2.0 -ProductDirectory "D:\专用工具\连接器产品"
```

Expected product directory:

```text
ordered-clicker-setup-v1.2.0.exe
ordered-clicker-portable-v1.2.0.zip
有序连点器-使用说明.pdf
有序连点器-使用说明.html
有序连点器-视频演示.mp4
SHA256SUMS.txt
```

### Task 6: Verification And GitHub Release

**Files:**
- Commit all source, tests, docs, and generated guide artifacts.

- [ ] **Step 1: Run complete verification**

```powershell
.\scripts\dotnet.ps1 build OrderedClicker.sln
.\scripts\dotnet.ps1 run --project tests\OrderedClicker.Tests\OrderedClicker.Tests.csproj
.\tests\Installer\InstallerContract.Tests.ps1
git diff --check
```

Expected: build succeeds, all tests pass, installer contract passes, and `git diff --check` is empty.

- [ ] **Step 2: Verify product hashes and file count**

Confirm the product directory contains exactly six files and every SHA-256 value matches `SHA256SUMS.txt`.

- [ ] **Step 3: Commit and push**

```powershell
git add .
git commit -m "feat: add configurable global hotkeys"
git push origin main
git tag -a v1.2.0 -m "有序连点器 1.2.0"
git push origin v1.2.0
```

- [ ] **Step 4: Create and verify GitHub Release**

Create public release `v1.2.0`, upload the six release assets with stable ASCII guide filenames where necessary, and verify GitHub asset digests against local hashes.
