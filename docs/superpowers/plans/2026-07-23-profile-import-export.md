# Profile Import Export Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add explicit, safe JSON profile import and export workflows while preserving Save and Save As behavior.

**Architecture:** Extend `ProfileService` with file-transfer operations and environment-independent import validation. Wire separate Import and Export buttons into `MainForm`; imports are applied only after confirmation and never bind the source path, while exports never alter the current save path.

**Tech Stack:** C# 14, .NET 10 Windows Forms, `System.Text.Json`, custom console test harness, PowerShell/Inno Setup packaging.

---

### Task 1: Profile transfer contract

**Files:**
- Modify: `tests/OrderedClicker.Tests/ProfileServiceTests.cs`
- Modify: `src/OrderedClicker/Services/ProfileService.cs`

- [ ] **Step 1: Write failing export round-trip and import validation tests**

Add tests that call the intended API:

```csharp
private static void ExportsAndImportsCompleteProfile()
{
    using var directory = new TemporaryDirectory();
    var service = new ProfileService(directory.Path);
    var path = System.IO.Path.Combine(directory.Path, "share", "方案.json");
    var source = CreateCompleteProfile();

    var exportedPath = service.Export(source, path);
    var imported = service.Import(exportedPath);

    TestAssert.Equal(path, exportedPath, "导出应返回用户选择的路径");
    TestAssert.True(imported.Success, imported.ErrorMessage ?? "导入应成功");
    TestAssert.Equal(source.Points.Count, imported.Profile!.Points.Count, "导入应保留全部点位");
    TestAssert.Equal(source.CoordinateMode, imported.Profile.CoordinateMode, "导入应保留坐标模式");
}

private static void RejectsUnsupportedFutureVersion()
{
    using var directory = new TemporaryDirectory();
    var service = new ProfileService(directory.Path);
    var path = System.IO.Path.Combine(directory.Path, "future.json");
    File.WriteAllText(path, """{"Version":99,"Name":"未来方案","Points":[]}""");

    var result = service.Import(path);

    TestAssert.True(!result.Success, "未来版本应被拒绝");
    TestAssert.Contains("版本", result.ErrorMessage ?? string.Empty, "错误应说明版本不受支持");
}
```

Add equivalent tests for an invalid click count and an invalid cloud relative coordinate.

- [ ] **Step 2: Run tests and verify RED**

Run:

```powershell
.\scripts\dotnet.ps1 run --project .\tests\OrderedClicker.Tests\OrderedClicker.Tests.csproj -c Release
```

Expected: compilation fails because `ProfileService.Export` and `ProfileService.Import` do not exist.

- [ ] **Step 3: Implement minimal transfer API and validation**

Add:

```csharp
public string Export(ClickProfile profile, string destination) =>
    Save(profile, destination);

public ProfileLoadResult Import(string path)
{
    var result = Load(path);
    if (!result.Success)
    {
        return result;
    }

    var error = ValidateImportedProfile(result.Profile!);
    return error is null
        ? result
        : ProfileLoadResult.Failed(error);
}
```

Implement `ValidateImportedProfile` with these exact supported ranges:

- profile version: 1 to 3
- loops: 1 to 100000
- loop/default delays: 0 to 600000
- default click interval: 10 to 600000
- click count: 1 to 100000
- point click interval: 10 to 600000
- point after delay: 0 to 600000
- valid cloud region and relative coordinates when cloud mode is selected

- [ ] **Step 4: Run tests and verify GREEN**

Run the complete test command and expect all tests to pass.

- [ ] **Step 5: Commit service behavior**

```powershell
git add src/OrderedClicker/Services/ProfileService.cs tests/OrderedClicker.Tests/ProfileServiceTests.cs
git commit -m "feat: add safe profile import and export"
```

### Task 2: Main window import and export workflows

**Files:**
- Modify: `tests/OrderedClicker.Tests/UiSmokeTests.cs`
- Modify: `src/OrderedClicker/Forms/MainForm.cs`
- Modify: `src/OrderedClicker/Forms/MainForm.Help.cs`

- [ ] **Step 1: Write failing UI smoke assertions**

Require four unique buttons:

```csharp
TestAssert.True(buttons.Any(button => button.Name == "SaveButton"), "主窗体应包含保存按钮");
TestAssert.True(buttons.Any(button => button.Name == "SaveAsButton"), "主窗体应包含另存为按钮");
TestAssert.True(buttons.Any(button => button.Name == "ImportProfileButton"), "主窗体应包含导入方案按钮");
TestAssert.True(buttons.Any(button => button.Name == "ExportProfileButton"), "主窗体应包含导出方案按钮");
```

- [ ] **Step 2: Run tests and verify RED**

Run the complete test suite and expect `UiSmoke` to fail because the new button names are absent.

- [ ] **Step 3: Replace Load with Import and add Export**

Add `_importProfileButton` and `_exportProfileButton`. Configure button text and tooltips:

```csharp
_importProfileButton.Name = "ImportProfileButton";
_importProfileButton.Text = "导入方案";
_exportProfileButton.Name = "ExportProfileButton";
_exportProfileButton.Text = "导出方案";
```

Wire `ImportProfile()` and `ExportProfile()` handlers.

`ExportProfile()` must:

1. Open a JSON `SaveFileDialog`.
2. Commit grid edits.
3. Call `_profileService.Export(CreateProfileSnapshot(), dialog.FileName)`.
4. Leave `_currentProfilePath` unchanged.
5. Report the exported path.

`ImportProfile()` must:

1. Open a JSON `OpenFileDialog`.
2. Call `_profileService.Import(dialog.FileName)`.
3. Show a confirmation containing name, point count, enabled count, and loops.
4. Apply only after confirmation.
5. Set `_currentProfilePath = null`.
6. Clear execution checkpoint.
7. Report that the file was imported as a copy.

- [ ] **Step 4: Update built-in help**

Explain the four operations and explicitly state that Import is a copy and Export does not change the save target.

- [ ] **Step 5: Run all tests and verify GREEN**

Run the complete test suite and expect every test to pass.

- [ ] **Step 6: Commit UI behavior**

```powershell
git add src/OrderedClicker/Forms/MainForm.cs src/OrderedClicker/Forms/MainForm.Help.cs tests/OrderedClicker.Tests/UiSmokeTests.cs
git commit -m "feat: expose profile import and export workflows"
```

### Task 3: Version and user documentation

**Files:**
- Modify: `src/OrderedClicker/OrderedClicker.csproj`
- Modify: `installer/OrderedClicker.iss`
- Modify: `scripts/build-installer.ps1`
- Modify: `scripts/publish.ps1`
- Modify: `tests/Installer.Tests.ps1`
- Modify: `README.md`
- Modify: `scripts/guide/build_product_guide.py`
- Modify: `scripts/guide/build_pdf_guide.py`
- Modify: `scripts/guide/verify_guide.py`
- Regenerate: `操作指导/有序连点器-使用说明.html`
- Regenerate: `操作指导/有序连点器-使用说明.pdf`

- [ ] **Step 1: Update installer contract tests to 1.3.1**

Change assertions from `1.3.0` to `1.3.1`, then run:

```powershell
pwsh -NoProfile -File .\tests\Installer.Tests.ps1
```

Expected: version assertions fail before production version files are changed.

- [ ] **Step 2: Update all production version values**

Set application, installer, build and publish defaults to `1.3.1`.

- [ ] **Step 3: Update README and guide generators**

Document:

- Save versus Save As.
- Import as a copy.
- Export without changing the current save target.
- JSON compatibility and validation behavior.

Update guide verification required terms to include `导入方案`, `导出方案`, `导入为副本`, and `JSON`.

- [ ] **Step 4: Regenerate and verify guides**

Run:

```powershell
& 'C:\Users\nb-liuxinsheng\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe' .\scripts\guide\build_product_guide.py
& 'C:\Users\nb-liuxinsheng\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe' .\scripts\guide\build_pdf_guide.py
& 'C:\Users\nb-liuxinsheng\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe' .\scripts\guide\verify_guide.py --skip-video
```

Expected: HTML/PDF verification passes and PDF page count is reported.

- [ ] **Step 5: Run installer contracts and commit**

Run installer tests, then commit version and documentation changes:

```powershell
git add README.md installer scripts src/OrderedClicker/OrderedClicker.csproj tests/Installer.Tests.ps1 操作指导
git commit -m "docs: prepare profile transfer release v1.3.1"
```

### Task 4: Package and release

**Files:**
- Produce: `D:\专用工具\连接器产品\ordered-clicker-setup-v1.3.1.exe`
- Produce: `D:\专用工具\连接器产品\ordered-clicker-portable-v1.3.1.zip`
- Produce: updated HTML/PDF and checksum files

- [ ] **Step 1: Run final tests**

Run the complete .NET test suite and installer contract suite. Both must have zero failures.

- [ ] **Step 2: Build product package**

```powershell
.\scripts\build-installer.ps1 `
  -Version 1.3.1 `
  -ProductDirectory 'D:\专用工具\连接器产品' `
  -GuideSourceDirectory 'D:\专用工具\连点器\操作指导'
```

- [ ] **Step 3: Verify package contents**

Require exactly six files in the product directory, verify executable versions are `1.3.1`, and recompute all hashes listed in `SHA256SUMS.txt`.

- [ ] **Step 4: Push and publish GitHub Release**

Push `main`, create annotated tag `v1.3.1`, create a public GitHub Release, upload all six delivery assets, and verify remote asset sizes and SHA-256 digests.
