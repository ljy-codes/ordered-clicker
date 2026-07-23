# Profile Directory Selector Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an editable local-profile selector and folder button that safely switches saved profiles, protects unsaved work, and ships both installer and portable executables.

**Architecture:** `ProfileService` will own enumeration and stable sorting of files in the default profile directory. `MainForm` will own the editable selector, folder launch, unsaved snapshot comparison, save confirmation, and local-profile binding semantics. Existing import/export and execution services remain unchanged.

**Tech Stack:** C# 13, .NET 10 Windows Forms, System.Text.Json, PowerShell 7, Python/ReportLab guide generation, Inno Setup 7.

---

### Task 1: Enumerate Local Profile Files

**Files:**
- Modify: `src/OrderedClicker/Services/ProfileService.cs`
- Modify: `tests/OrderedClicker.Tests/ProfileServiceTests.cs`

- [ ] **Step 1: Write failing profile enumeration tests**

Add tests that create `方案2.json`, `方案10.json`, `说明.txt`, and `UPPER.JSON`, then assert:

```csharp
var profiles = service.ListLocalProfiles();
TestAssert.Equal(3, profiles.Count, "只应列出 JSON 方案");
TestAssert.Equal("UPPER", profiles[0].DisplayName, "排序应稳定");
TestAssert.Equal("方案2", profiles[1].DisplayName, "数字文件名应自然排序");
TestAssert.Equal("方案10", profiles[2].DisplayName, "方案10 应排在方案2之后");
```

Also assert that calling `ListLocalProfiles()` creates a missing profile directory and returns an empty list.

- [ ] **Step 2: Run tests and verify RED**

Run:

```powershell
.\scripts\dotnet.ps1 run --project .\tests\OrderedClicker.Tests\OrderedClicker.Tests.csproj -c Release
```

Expected: compilation fails because `ListLocalProfiles` and `LocalProfileEntry` do not exist.

- [ ] **Step 3: Implement enumeration**

Add:

```csharp
public sealed record LocalProfileEntry(string DisplayName, string Path)
{
    public override string ToString() => DisplayName;
}
```

Implement `ListLocalProfiles()` to create `ProfilesDirectory`, enumerate only top-level files with a case-insensitive `.json` extension, normalize full paths, and sort display names with a numeric-aware comparer.

- [ ] **Step 4: Run tests and verify GREEN**

Run the .NET test command and expect `12/12 tests passed`.

- [ ] **Step 5: Commit**

```powershell
git add src/OrderedClicker/Services/ProfileService.cs tests/OrderedClicker.Tests/ProfileServiceTests.cs
git commit -m "feat: enumerate local profile directory"
```

### Task 2: Add Selector and Folder Button

**Files:**
- Modify: `src/OrderedClicker/Forms/MainForm.cs`
- Modify: `src/OrderedClicker/Forms/MainForm.Execution.cs`
- Modify: `tests/OrderedClicker.Tests/UiSmokeTests.cs`

- [ ] **Step 1: Write failing UI tests**

Update UI smoke tests to require:

```csharp
TestAssert.True(controls.OfType<ComboBox>().Any(
    control => control.Name == "ProfileSelector"),
    "主窗体应包含可编辑方案下拉框");
TestAssert.True(buttons.Any(
    button => button.Name == "OpenProfilesDirectoryButton"),
    "主窗体应包含方案目录按钮");
```

Extend execution-state tests to assert both controls are disabled while configuration is locked.

- [ ] **Step 2: Run tests and verify RED**

Run the .NET test command.

Expected: UI smoke test fails because the selector and directory button do not exist.

- [ ] **Step 3: Implement controls**

Replace `_profileNameTextBox` with an editable `ComboBox` named `ProfileSelector`, preserve the 200-pixel width, and add a 38-pixel folder icon button named `OpenProfilesDirectoryButton`.

Wire:

```csharp
_profileSelector.DropDown += (_, _) => RefreshProfileDirectory();
_profileSelector.SelectionChangeCommitted += (_, _) => SelectLocalProfile();
_openProfilesDirectoryButton.Click += (_, _) => OpenProfilesDirectory();
```

Update every profile-name read/write to use `_profileSelector.Text`. Disable both controls in `SetConfigurationEnabled`.

- [ ] **Step 4: Run tests and verify GREEN**

Run the .NET tests and expect `12/12 tests passed`.

- [ ] **Step 5: Commit**

```powershell
git add src/OrderedClicker/Forms/MainForm.cs src/OrderedClicker/Forms/MainForm.Execution.cs tests/OrderedClicker.Tests/UiSmokeTests.cs
git commit -m "feat: add local profile selector controls"
```

### Task 3: Protect Unsaved Work During Switching

**Files:**
- Modify: `src/OrderedClicker/Services/ProfileService.cs`
- Modify: `src/OrderedClicker/Forms/MainForm.cs`
- Modify: `tests/OrderedClicker.Tests/ProfileServiceTests.cs`
- Modify: `tests/OrderedClicker.Tests/UiSmokeTests.cs`

- [ ] **Step 1: Write failing snapshot and binding tests**

Add tests for:

```csharp
TestAssert.True(
    ProfileService.ProfilesEqual(profile, clone),
    "相同方案快照应判定为未修改");
clone.Points[0].AfterDelayMs++;
TestAssert.True(
    !ProfileService.ProfilesEqual(profile, clone),
    "单点参数变化应判定为未保存修改");
```

Add a UI reflection test that applies a local profile with a path and asserts:

```csharp
TestAssert.True(ProfileService.PathsEqual(currentPath, sourcePath),
    "本机方案加载后应绑定原文件");
TestAssert.True(!saveImportedCopy,
    "本机方案不应进入导入副本状态");
```

- [ ] **Step 2: Run tests and verify RED**

Expected: tests fail because `ProfilesEqual` and `ApplyLocalProfile` do not exist.

- [ ] **Step 3: Implement snapshot comparison and switch flow**

Implement deterministic profile comparison in `ProfileService` using the existing JSON options.

In `MainForm`:

- Store `_baselineProfile`.
- Change `SaveProfile()` and `SaveProfileAs()` to return `bool`.
- Refresh the baseline after successful save, save-as, and local load.
- Treat imported copies as unsaved until saved.
- Before switching, show a Yes/No/Cancel dialog:
  - Yes calls `SaveProfile()` and continues only on success.
  - No continues without saving.
  - Cancel restores selector text and selection.
- Validate selected local files through `ProfileService.Import`.
- Apply a successful local load with `_currentProfilePath` bound to the selected file and imported-copy flags cleared.
- On failure, keep the current UI and path unchanged.

- [ ] **Step 4: Implement directory launch**

Create the profile directory, then launch it with:

```csharp
Process.Start(new ProcessStartInfo
{
    FileName = _profileService.ProfilesDirectory,
    UseShellExecute = true
});
```

Catch launch exceptions and display a controlled error.

- [ ] **Step 5: Run tests and verify GREEN**

Run the complete .NET test suite and expect `12/12 tests passed`.

- [ ] **Step 6: Commit**

```powershell
git add src/OrderedClicker/Services/ProfileService.cs src/OrderedClicker/Forms/MainForm.cs tests/OrderedClicker.Tests/ProfileServiceTests.cs tests/OrderedClicker.Tests/UiSmokeTests.cs
git commit -m "feat: safely switch local profiles"
```

### Task 4: Update Help and Product Guides

**Files:**
- Modify: `src/OrderedClicker/Forms/MainForm.Help.cs`
- Modify: `scripts/guide/build_product_guide.py`
- Modify: `scripts/guide/build_pdf_guide.py`
- Modify: `scripts/guide/verify_guide.py`
- Regenerate: `操作指导/有序连点器-使用说明.html`
- Regenerate: `操作指导/有序连点器-使用说明.pdf`

- [ ] **Step 1: Add failing guide requirements**

Require HTML and PDF to contain:

```text
方案下拉
打开方案目录
保存 / 不保存 / 取消
```

Run `verify_guide.py --skip-video` and expect failure before guide updates.

- [ ] **Step 2: Update built-in and generated guidance**

Explain:

- The dropdown lists only the default local profile directory.
- Selecting another profile prompts before discarding unsaved work.
- The folder button opens `%LocalAppData%\OrderedClicker\profiles`.
- External JSON files still use “导入方案”.

- [ ] **Step 3: Regenerate and verify**

Run:

```powershell
python .\scripts\guide\build_product_guide.py
python .\scripts\guide\build_pdf_guide.py
python .\scripts\guide\verify_guide.py --skip-video
```

Expected: guide verification passes and PDF remains at least 15 pages.

- [ ] **Step 4: Commit**

```powershell
git add src/OrderedClicker/Forms/MainForm.Help.cs scripts/guide 操作指导/有序连点器-使用说明.html 操作指导/有序连点器-使用说明.pdf
git commit -m "docs: explain local profile directory"
```

### Task 5: Build Four-File Delivery

**Files:**
- Modify: `scripts/build-installer.ps1`
- Modify: `tests/Installer.Tests.ps1`
- Modify: `README.md`
- Output: `D:\专用工具\连接器产品\ordered-clicker-setup-v1.3.1.exe`
- Output: `D:\专用工具\连接器产品\OrderedClicker.exe`
- Output: `D:\专用工具\连接器产品\有序连点器-使用说明.pdf`
- Output: `D:\专用工具\连接器产品\有序连点器-使用说明.html`

- [ ] **Step 1: Write failing delivery contract tests**

Require the build script to copy `publish\win-x64\OrderedClicker.exe` into the product directory and require README text stating that the product directory contains four files.

- [ ] **Step 2: Run tests and verify RED**

Run:

```powershell
pwsh -NoProfile -File .\tests\Installer.Tests.ps1
```

Expected: failure because the portable executable is not copied.

- [ ] **Step 3: Implement four-file delivery**

Extend the existing staging and hash-verified copy flow to include `OrderedClicker.exe`. Keep cleanup restricted to owned filenames and retain path-safety checks.

- [ ] **Step 4: Build**

Run:

```powershell
pwsh -NoProfile -File .\scripts\build-installer.ps1 `
  -Version 1.3.1 `
  -ProductDirectory 'D:\专用工具\连接器产品' `
  -GuideSourceDirectory 'D:\专用工具\连点器\操作指导'
```

Expected: successful tests, publish, Inno Setup compile, and exactly four files in the product directory.

- [ ] **Step 5: Final verification**

Verify:

- .NET tests: `12/12`.
- Installer contract tests pass.
- Guide validation passes.
- Both executables have `ProductVersion=1.3.1`.
- Product directory contains four files and no subdirectories.
- Independent review reports no release blocker.

- [ ] **Step 6: Commit**

```powershell
git add scripts/build-installer.ps1 tests/Installer.Tests.ps1 README.md
git commit -m "build: include portable executable in product delivery"
```
