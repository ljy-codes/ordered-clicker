using OrderedClicker.Forms;
using OrderedClicker.Models;
using OrderedClicker.Services;
using OrderedClicker.Theming;
using System.Reflection;

namespace OrderedClicker.Tests;

internal static class UiSmokeTests
{
    public static void Run()
    {
        using var form = CreateLaidOutForm();
        AssertControlsStayInsideParents(form);
        AssertButtonTextFits(form);
        AssertDarkTheme(form);
        AssertUsageHelpEntry(form);
        AssertThemeSettingsEntry(form);
        AssertSettingsDisabledDuringExecution(form);
        AssertGlobalTimingControls(form);
        AssertCloudDesktopControls(form);
        AssertSaveCommands(form);
        AssertProfileDirectoryControls(form);
        AssertImportedCopyRequiresSaveAs(form);
        AssertLocalProfileBindsSourceFile(form);
        AssertGlobalTimingApplication(form);
        AssertDefaultHotKeyText(form);
        AssertUnchangedHotKeysAreDetected();
        AssertThemeSettingsDialog(form);
        AssertConfiguredThemeLoads();
        AssertConfiguredHotKeysLoad();
        AssertThemePreviewCancelAndSave();
        AssertUsageHelpDialog(form);
        AssertMinimumWindowLayout();
    }

    private static void AssertCloudDesktopControls(MainForm form)
    {
        var names = EnumerateControls(form)
            .Select(control => control.Name)
            .ToHashSet(StringComparer.Ordinal);

        TestAssert.True(names.Contains("CloudDesktopEnabledCheckBox"),
            "主窗体应包含云桌面增强开关");
        TestAssert.True(names.Contains("CalibrateRegionButton"),
            "主窗体应包含云桌面区域校准按钮");
        TestAssert.True(names.Contains("CloudRegionStatusLabel"),
            "主窗体应显示云桌面校准状态");
        TestAssert.True(names.Contains("WaitForStableScreenCheckBox"),
            "主窗体应包含画面稳定等待开关");
        TestAssert.True(names.Contains("StabilityTimeoutInput"),
            "主窗体应包含稳定等待超时设置");
    }

    private static void AssertSaveCommands(MainForm form)
    {
        var buttons = EnumerateControls(form).OfType<Button>().ToList();
        TestAssert.True(buttons.Any(button => button.Name == "SaveButton"),
            "主窗体应保留保存按钮");
        TestAssert.True(buttons.Any(button => button.Name == "SaveAsButton"),
            "主窗体应包含另存为按钮");
        TestAssert.True(buttons.Any(button => button.Name == "ImportProfileButton"),
            "主窗体应包含导入方案按钮");
        TestAssert.True(buttons.Any(button => button.Name == "ExportProfileButton"),
            "主窗体应包含导出方案按钮");
    }

    private static void AssertProfileDirectoryControls(MainForm form)
    {
        var controls = EnumerateControls(form).ToList();
        var selector = controls
            .OfType<ComboBox>()
            .SingleOrDefault(control => control.Name == "ProfileSelector");
        var directoryButton = controls
            .OfType<Button>()
            .SingleOrDefault(button => button.Name == "OpenProfilesDirectoryButton");

        TestAssert.True(selector is not null, "主窗体应包含可编辑方案下拉框");
        TestAssert.Equal(
            ComboBoxStyle.DropDown,
            selector!.DropDownStyle,
            "方案下拉框应允许直接编辑名称");
        TestAssert.True(directoryButton is not null, "主窗体应包含方案目录按钮");
        TestAssert.True(directoryButton!.Width >= 36, "方案目录按钮应保持稳定宽度");
    }

    private static void AssertImportedCopyRequiresSaveAs(MainForm form)
    {
        var applyImportedProfile = typeof(MainForm).GetMethod(
            "ApplyImportedProfile",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var requiresSaveAsField = typeof(MainForm).GetField(
            "_saveImportedProfileAsCopy",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var importedSourcePathField = typeof(MainForm).GetField(
            "_importedProfileSourcePath",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var currentPathField = typeof(MainForm).GetField(
            "_currentProfilePath",
            BindingFlags.Instance | BindingFlags.NonPublic);

        TestAssert.True(applyImportedProfile is not null,
            "主窗体应集中处理导入副本状态");
        TestAssert.True(requiresSaveAsField is not null,
            "导入副本首次保存应强制另存为");
        TestAssert.True(importedSourcePathField is not null,
            "导入副本应保留来源路径用于防覆盖");

        var sourcePath = @"C:\profiles\source.json";
        currentPathField!.SetValue(form, sourcePath);
        applyImportedProfile!.Invoke(
            form,
            [new ClickProfile { Version = 3, Name = "导入副本" }, sourcePath]);

        TestAssert.True(currentPathField.GetValue(form) is null,
            "导入副本不应绑定来源文件路径");
        TestAssert.True((bool)requiresSaveAsField!.GetValue(form)!,
            "导入副本首次保存必须进入另存为");
        TestAssert.True(
            ProfileService.PathsEqual(
                (string)importedSourcePathField!.GetValue(form)!,
                sourcePath),
            "导入副本应保留规范化来源路径");
    }

    private static void AssertLocalProfileBindsSourceFile(MainForm form)
    {
        var applyLocalProfile = typeof(MainForm).GetMethod(
            "ApplyLocalProfile",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var requiresSaveAsField = typeof(MainForm).GetField(
            "_saveImportedProfileAsCopy",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var currentPathField = typeof(MainForm).GetField(
            "_currentProfilePath",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var hasUnsavedChanges = typeof(MainForm).GetMethod(
            "HasUnsavedProfileChanges",
            BindingFlags.Instance | BindingFlags.NonPublic);

        TestAssert.True(applyLocalProfile is not null,
            "主窗体应集中处理本机方案绑定状态");
        TestAssert.True(hasUnsavedChanges is not null,
            "主窗体应能识别未保存方案修改");

        var sourcePath = @"C:\profiles\local.json";
        applyLocalProfile!.Invoke(
            form,
            [new ClickProfile { Version = 2, Name = "本机方案" }, sourcePath]);

        TestAssert.True(
            ProfileService.PathsEqual(
                (string)currentPathField!.GetValue(form)!,
                sourcePath),
            "本机方案加载后应绑定原文件");
        TestAssert.True(!(bool)requiresSaveAsField!.GetValue(form)!,
            "本机方案不应进入导入副本状态");
        TestAssert.True(!(bool)hasUnsavedChanges!.Invoke(form, null)!,
            "刚加载的本机方案不应标记为未保存");

        var selector = EnumerateControls(form)
            .OfType<ComboBox>()
            .Single(control => control.Name == "ProfileSelector");
        selector.Text = "本机方案-修改";

        TestAssert.True((bool)hasUnsavedChanges.Invoke(form, null)!,
            "编辑方案名称后应标记为未保存");
    }

    public static void Render(string outputPath)
    {
        using var form = CreateLaidOutForm();
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        var fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        bitmap.Save(fullPath, System.Drawing.Imaging.ImageFormat.Png);
        Console.WriteLine($"UI rendered: {fullPath}");
    }

    public static void RenderSettings(string outputPath)
    {
        RenderDialog("ThemeSettingsButton", "ThemeSettingsDialog", outputPath);
    }

    public static void RenderHelp(string outputPath)
    {
        RenderDialog("UsageHelpButton", "UsageHelpDialog", outputPath);
    }

    public static void RenderTheme(AppThemeId themeId, string outputPath)
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"OrderedClicker.RenderTheme.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var settingsService = new SettingsService(Path.Combine(directory, "settings.json"));
            settingsService.Save(new AppSettings { Theme = themeId });
            using var form = CreateLaidOutForm(settingsService);
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            var fullPath = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            bitmap.Save(fullPath, System.Drawing.Imaging.ImageFormat.Png);
            Console.WriteLine($"Theme rendered: {fullPath}");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static MainForm CreateLaidOutForm(SettingsService? settingsService = null)
    {
        settingsService ??= new SettingsService(Path.Combine(
            Path.GetTempPath(),
            $"OrderedClicker.Tests.{Guid.NewGuid():N}.settings.json"));
        var form = new MainForm(enableGlobalHotKeys: false, settingsService)
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-32000, -32000),
            ShowInTaskbar = false
        };
        form.Show();
        Application.DoEvents();
        CreateHandles(form);
        PerformLayout(form);
        TestAssert.True(form.Controls.Cast<Control>().Any(control => control.Visible),
            "显示后的主窗体应包含可见控件");
        return form;
    }

    private static void CreateHandles(Control control)
    {
        control.CreateControl();
        foreach (Control child in control.Controls)
        {
            CreateHandles(child);
        }
    }

    private static void PerformLayout(Control control)
    {
        control.PerformLayout();
        foreach (Control child in control.Controls)
        {
            PerformLayout(child);
        }
    }

    private static void AssertControlsStayInsideParents(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            if (child.Visible)
            {
                TestAssert.True(child.Width > 0 && child.Height > 0,
                    $"{child.GetType().Name} 尺寸必须大于 0");
                TestAssert.True(child.Left >= 0 && child.Top >= 0,
                    $"{child.GetType().Name} 不应超出父容器左侧或顶部");
                TestAssert.True(child.Right <= parent.ClientSize.Width + 1,
                    $"{child.GetType().Name} 不应超出父容器右侧");
                TestAssert.True(child.Bottom <= parent.ClientSize.Height + 1,
                    $"{child.GetType().Name} 不应超出父容器底部");
            }

            AssertControlsStayInsideParents(child);
        }
    }

    private static void AssertButtonTextFits(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            if (child is Button button)
            {
                var textSize = TextRenderer.MeasureText(button.Text, button.Font);
                TestAssert.True(textSize.Width + 14 <= button.ClientSize.Width,
                    $"按钮文字“{button.Text}”不应横向溢出");
                TestAssert.True(textSize.Height + 8 <= button.ClientSize.Height,
                    $"按钮文字“{button.Text}”不应纵向溢出");
            }

            AssertButtonTextFits(child);
        }
    }

    private static void AssertDarkTheme(MainForm form)
    {
        AssertDarkSurface(form.BackColor, "主窗体背景");

        foreach (var control in EnumerateControls(form)
                     .Where(control => control is TextBox or NumericUpDown))
        {
            AssertDarkSurface(control.BackColor, $"{control.GetType().Name} 背景");
            AssertLightText(control.ForeColor, $"{control.GetType().Name} 文字");
        }

        var grid = EnumerateControls(form).OfType<DataGridView>().Single();
        AssertDarkSurface(grid.BackgroundColor, "表格空白区");
        AssertDarkSurface(grid.DefaultCellStyle.BackColor, "表格数据区");
        AssertDarkSurface(grid.ColumnHeadersDefaultCellStyle.BackColor, "表格表头");
        AssertLightText(grid.DefaultCellStyle.ForeColor, "表格数据文字");
        AssertLightText(grid.ColumnHeadersDefaultCellStyle.ForeColor, "表格表头文字");

        var statusStrip = EnumerateControls(form).OfType<StatusStrip>().Single();
        AssertDarkSurface(statusStrip.BackColor, "状态栏背景");
        AssertLightText(statusStrip.ForeColor, "状态栏文字");
    }

    private static void AssertUsageHelpEntry(MainForm form)
    {
        var buttons = EnumerateControls(form).OfType<Button>().ToList();
        var helpButton = buttons.SingleOrDefault(button => button.Name == "UsageHelpButton");
        TestAssert.True(helpButton is not null, "主窗体应包含使用说明按钮");
        TestAssert.Equal("? 使用说明", helpButton!.Text, "使用说明按钮文案应明确");
        TestAssert.True(helpButton.Width >= 140, "使用说明按钮应保持足够宽度");

        var clearButton = buttons.Single(button => button.Text == "清空");
        TestAssert.True(helpButton.Left > clearButton.Right,
            "使用说明按钮应位于清空按钮右侧的空闲区域");
    }

    private static void AssertConfiguredThemeLoads()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"OrderedClicker.UiTheme.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var settingsService = new SettingsService(Path.Combine(directory, "settings.json"));
            settingsService.Save(new AppSettings { Theme = AppThemeId.Light });
            using var form = CreateLaidOutForm(settingsService);
            var theme = AppThemeCatalog.Get(AppThemeId.Light);

            TestAssert.Equal(theme.Window, form.BackColor,
                "主窗体应加载已保存的主题背景");
            TestAssert.Equal(
                theme.Input,
                EnumerateControls(form).OfType<TextBox>().First().BackColor,
                "输入框应应用已保存主题");
            TestAssert.Equal(
                theme.Input,
                EnumerateControls(form).OfType<DataGridView>().Single().BackgroundColor,
                "表格应应用已保存主题");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static void AssertConfiguredHotKeysLoad()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"OrderedClicker.UiHotKeys.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var settingsService = new SettingsService(Path.Combine(directory, "settings.json"));
            settingsService.Save(new AppSettings
            {
                CaptureHotKey = new HotKeyBinding(
                    Keys.F6,
                    ShortcutModifiers.Control | ShortcutModifiers.Shift),
                StartPauseHotKey = new HotKeyBinding(
                    Keys.F7,
                    ShortcutModifiers.Control | ShortcutModifiers.Shift),
                StopHotKey = new HotKeyBinding(
                    Keys.F8,
                    ShortcutModifiers.Control | ShortcutModifiers.Shift)
            });
            using var form = CreateLaidOutForm(settingsService);
            var text = string.Join(
                Environment.NewLine,
                EnumerateControls(form).Select(control => control.Text));

            TestAssert.True(
                text.Contains("Ctrl+Shift+F6")
                && text.Contains("Ctrl+Shift+F7")
                && text.Contains("Ctrl+Shift+F8"),
                "主窗体应显示已保存的自定义快捷键");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static void AssertThemeSettingsEntry(MainForm form)
    {
        var buttons = EnumerateControls(form).OfType<Button>().ToList();
        var settingsButton = buttons.SingleOrDefault(
            button => button.Name == "ThemeSettingsButton");
        TestAssert.True(settingsButton is not null, "主窗体应包含主题设置按钮");
        TestAssert.Equal("⚙ 设置", settingsButton!.Text, "主题设置按钮文案应明确");
        TestAssert.True(settingsButton.Width >= 120, "主题设置按钮应保持足够宽度");

        var clearButton = buttons.Single(button => button.Text == "清空");
        var helpButton = buttons.Single(button => button.Name == "UsageHelpButton");
        TestAssert.True(settingsButton.Left > clearButton.Right,
            "主题设置按钮应位于清空按钮右侧的空闲区域");
        TestAssert.True(helpButton.Left > settingsButton.Right,
            "使用说明按钮应排列在主题设置按钮右侧");
    }

    private static void AssertSettingsDisabledDuringExecution(MainForm form)
    {
        var buttons = EnumerateControls(form)
            .OfType<Button>()
            .ToList();
        var guardedButtonNames = new[]
        {
            "ThemeSettingsButton",
            "SaveButton",
            "SaveAsButton",
            "ImportProfileButton",
            "ExportProfileButton",
            "OpenProfilesDirectoryButton"
        };
        var profileSelector = EnumerateControls(form)
            .OfType<ComboBox>()
            .Single(control => control.Name == "ProfileSelector");
        var method = typeof(MainForm).GetMethod(
            "SetConfigurationEnabled",
            BindingFlags.Instance | BindingFlags.NonPublic);

        method!.Invoke(form, [false]);
        foreach (var buttonName in guardedButtonNames)
        {
            var button = buttons.Single(candidate => candidate.Name == buttonName);
            TestAssert.True(
                !button.Enabled,
                $"执行期间应禁用 {buttonName}");
        }
        TestAssert.True(!profileSelector.Enabled, "执行期间应禁用方案下拉框");

        method.Invoke(form, [true]);
        foreach (var buttonName in guardedButtonNames)
        {
            var button = buttons.Single(candidate => candidate.Name == buttonName);
            TestAssert.True(
                button.Enabled,
                $"执行结束后应恢复 {buttonName}");
        }
        TestAssert.True(profileSelector.Enabled, "执行结束后应恢复方案下拉框");
    }

    private static void AssertGlobalTimingControls(MainForm form)
    {
        var controls = EnumerateControls(form).ToList();
        var intervalInput = controls
            .OfType<NumericUpDown>()
            .SingleOrDefault(control => control.Name == "DefaultClickIntervalInput");
        var intervalButton = controls
            .OfType<Button>()
            .SingleOrDefault(control => control.Name == "ApplyClickIntervalButton");
        var afterDelayInput = controls
            .OfType<NumericUpDown>()
            .SingleOrDefault(control => control.Name == "DefaultAfterDelayInput");
        var afterDelayButton = controls
            .OfType<Button>()
            .SingleOrDefault(control => control.Name == "ApplyAfterDelayButton");

        TestAssert.True(intervalInput is not null, "主窗体应包含全局点击间隔输入框");
        TestAssert.True(intervalButton is not null, "主窗体应包含点击间隔应用全部按钮");
        TestAssert.True(afterDelayInput is not null, "主窗体应包含全局点后等待输入框");
        TestAssert.True(afterDelayButton is not null, "主窗体应包含点后等待应用全部按钮");
        TestAssert.Equal(10m, intervalInput!.Minimum, "全局点击间隔最小值应与点位校验一致");
        TestAssert.Equal(600000m, intervalInput.Maximum, "全局点击间隔最大值应与点位校验一致");
        TestAssert.Equal(0m, afterDelayInput!.Minimum, "全局点后等待最小值应与点位校验一致");
        TestAssert.Equal(600000m, afterDelayInput.Maximum, "全局点后等待最大值应与点位校验一致");
    }

    private static void AssertGlobalTimingApplication(MainForm form)
    {
        ApplyProfile(
            form,
            new ClickProfile
            {
                Version = 2,
                DefaultClickIntervalMs = 100,
                DefaultAfterDelayMs = 500,
                Points =
                [
                    new ClickPoint { ClickIntervalMs = 100, AfterDelayMs = 500 },
                    new ClickPoint { ClickIntervalMs = 200, AfterDelayMs = 800 }
                ]
            });

        var controls = EnumerateControls(form).ToList();
        var intervalInput = controls
            .OfType<NumericUpDown>()
            .Single(control => control.Name == "DefaultClickIntervalInput");
        var intervalButton = controls
            .OfType<Button>()
            .Single(control => control.Name == "ApplyClickIntervalButton");
        var afterDelayInput = controls
            .OfType<NumericUpDown>()
            .Single(control => control.Name == "DefaultAfterDelayInput");
        var afterDelayButton = controls
            .OfType<Button>()
            .Single(control => control.Name == "ApplyAfterDelayButton");

        intervalInput.Value = 1000;
        intervalButton.PerformClick();
        var points = GetPoints(form);
        TestAssert.True(
            points.All(point => point.ClickIntervalMs == 1000),
            "点击间隔应用全部应更新所有点位");
        TestAssert.Equal(500, points[0].AfterDelayMs, "点击间隔应用全部不应覆盖单点点后等待");
        TestAssert.Equal(800, points[1].AfterDelayMs, "点击间隔应用全部不应覆盖其他点后等待");

        afterDelayInput.Value = 750;
        afterDelayButton.PerformClick();
        TestAssert.True(
            points.All(point => point.AfterDelayMs == 750),
            "点后等待应用全部应更新所有点位");
        TestAssert.True(
            points.All(point => point.ClickIntervalMs == 1000),
            "点后等待应用全部不应覆盖点击间隔");

        points[0].ClickIntervalMs = 200;
        TestAssert.Equal(200, points[0].ClickIntervalMs, "单点仍应允许覆盖全局点击间隔");
        TestAssert.Equal(1000, points[1].ClickIntervalMs, "单点覆盖不应影响其他点位");
        TestAssert.Equal(1000m, intervalInput.Value, "单点覆盖不应反向修改全局默认值");
    }

    private static void AssertThemeSettingsDialog(MainForm form)
    {
        var settingsButton = EnumerateControls(form)
            .OfType<Button>()
            .Single(button => button.Name == "ThemeSettingsButton");
        var dialogOpened = false;
        var themeCardCount = 0;
        var dialogUsesCurrentTheme = false;
        var hotKeyControlNames = new HashSet<string>();
        var restoreDefaultsButtonFound = false;
        using var timer = new System.Windows.Forms.Timer { Interval = 20 };
        timer.Tick += (_, _) =>
        {
            var dialog = Application.OpenForms
                .Cast<Form>()
                .FirstOrDefault(openForm => openForm.Name == "ThemeSettingsDialog");
            if (dialog is null)
            {
                return;
            }

            dialogOpened = true;
            themeCardCount = EnumerateControls(dialog)
                .Count(control => control.Name.StartsWith("ThemeCard_", StringComparison.Ordinal));
            hotKeyControlNames = EnumerateControls(dialog)
                .Where(control => control.Name.EndsWith("HotKeyInput", StringComparison.Ordinal))
                .Select(control => control.Name)
                .ToHashSet(StringComparer.Ordinal);
            restoreDefaultsButtonFound = EnumerateControls(dialog)
                .OfType<Button>()
                .Any(button => button.Name == "RestoreDefaultHotKeysButton");
            dialogUsesCurrentTheme = dialog.BackColor == form.BackColor;
            dialog.Close();
        };

        timer.Start();
        settingsButton.PerformClick();
        timer.Stop();

        TestAssert.True(dialogOpened, "点击主题设置按钮后应打开设置弹窗");
        TestAssert.Equal(5, themeCardCount, "设置弹窗应显示五张主题卡片");
        TestAssert.True(
            hotKeyControlNames.SetEquals(
                [
                    "CaptureHotKeyInput",
                    "StartPauseHotKeyInput",
                    "StopHotKeyInput"
                ]),
            "设置弹窗应包含三个快捷键输入框");
        TestAssert.True(restoreDefaultsButtonFound, "设置弹窗应包含恢复默认快捷键按钮");
        TestAssert.True(dialogUsesCurrentTheme, "设置弹窗应使用当前主题");
    }

    private static void AssertDefaultHotKeyText(MainForm form)
    {
        var text = string.Join(
            Environment.NewLine,
            EnumerateControls(form).Select(control => control.Text));
        var statusText = string.Join(
            Environment.NewLine,
            EnumerateControls(form)
                .OfType<StatusStrip>()
                .SelectMany(strip => strip.Items.Cast<ToolStripItem>())
                .Select(item => item.Text));

        TestAssert.True(
            text.Contains("F6")
            && text.Contains("F7")
            && text.Contains("F8"),
            "主界面按钮和提示应显示默认简洁快捷键");
        TestAssert.True(
            statusText.Contains("F6")
            && statusText.Contains("F7")
            && statusText.Contains("F8"),
            "状态栏应显示默认简洁快捷键");
    }

    private static void AssertUnchangedHotKeysAreDetected()
    {
        var method = typeof(MainForm).GetMethod(
            "HotKeysChanged",
            BindingFlags.Static | BindingFlags.NonPublic);
        var original = new AppSettings();
        var themeOnlyChange = new AppSettings
        {
            Theme = AppThemeId.Light,
            CaptureHotKey = original.CaptureHotKey,
            StartPauseHotKey = original.StartPauseHotKey,
            StopHotKey = original.StopHotKey
        };

        var changed = (bool)method!.Invoke(null, [original, themeOnlyChange])!;

        TestAssert.True(
            !changed,
            "仅切换主题时不应注销并重新注册全局快捷键");
    }

    private static void AssertThemePreviewCancelAndSave()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"OrderedClicker.ThemeDialog.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var settingsService = new SettingsService(Path.Combine(directory, "settings.json"));
            settingsService.Save(new AppSettings { Theme = AppThemeId.Aurora });
            using var form = CreateLaidOutForm(settingsService);

            var lightPreviewed = RunThemeDialogAction(
                form,
                "ThemeCard_Light",
                "取消",
                AppThemeCatalog.Get(AppThemeId.Light).Window);
            TestAssert.True(lightPreviewed, "点击主题卡片后应立即预览主题");
            TestAssert.Equal(
                AppThemeCatalog.Get(AppThemeId.Aurora).Window,
                form.BackColor,
                "取消设置后应恢复原主题");
            TestAssert.Equal(
                AppThemeId.Aurora,
                settingsService.Load().Theme,
                "取消设置不应修改持久化主题");

            var oceanPreviewed = RunThemeDialogAction(
                form,
                "ThemeCard_Ocean",
                "保存",
                AppThemeCatalog.Get(AppThemeId.Ocean).Window);
            TestAssert.True(oceanPreviewed, "保存前应预览选中的主题");
            TestAssert.Equal(
                AppThemeCatalog.Get(AppThemeId.Ocean).Window,
                form.BackColor,
                "保存后应保留选中的主题");
            TestAssert.Equal(
                AppThemeId.Ocean,
                settingsService.Load().Theme,
                "保存后应持久化选中的主题");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static bool RunThemeDialogAction(
        MainForm form,
        string cardName,
        string actionButtonText,
        Color expectedPreviewColor)
    {
        var settingsButton = EnumerateControls(form)
            .OfType<Button>()
            .Single(button => button.Name == "ThemeSettingsButton");
        var previewObserved = false;
        using var timer = new System.Windows.Forms.Timer { Interval = 20 };
        timer.Tick += (_, _) =>
        {
            var dialog = Application.OpenForms
                .Cast<Form>()
                .FirstOrDefault(openForm => openForm.Name == "ThemeSettingsDialog");
            if (dialog is null)
            {
                return;
            }

            var card = EnumerateControls(dialog).Single(control => control.Name == cardName);
            RaiseClick(card);
            previewObserved = form.BackColor == expectedPreviewColor
                              && dialog.BackColor == expectedPreviewColor;
            EnumerateControls(dialog)
                .OfType<Button>()
                .Single(button => button.Text == actionButtonText)
                .PerformClick();
        };

        timer.Start();
        settingsButton.PerformClick();
        timer.Stop();
        return previewObserved;
    }

    private static void AssertUsageHelpDialog(MainForm form)
    {
        var helpButton = EnumerateControls(form)
            .OfType<Button>()
            .Single(button => button.Name == "UsageHelpButton");
        var dialogOpened = false;
        var dialogText = string.Empty;
        var hasScrollableBody = false;
        var dialogUsesCurrentTheme = false;
        using var timer = new System.Windows.Forms.Timer { Interval = 20 };
        timer.Tick += (_, _) =>
        {
            var dialog = Application.OpenForms
                .Cast<Form>()
                .FirstOrDefault(openForm => openForm.Name == "UsageHelpDialog");
            if (dialog is null)
            {
                return;
            }

            dialogOpened = true;
            dialogText = string.Join(
                Environment.NewLine,
                EnumerateControls(dialog).Select(control => control.Text));
            hasScrollableBody = EnumerateControls(dialog)
                .OfType<RichTextBox>()
                .Any(body => body.ReadOnly && body.ScrollBars == RichTextBoxScrollBars.Vertical);
            dialogUsesCurrentTheme = dialog.BackColor == form.BackColor;
            dialog.Close();
        };

        timer.Start();
        helpButton.PerformClick();
        timer.Stop();

        TestAssert.True(dialogOpened, "点击使用说明按钮后应打开说明弹窗");
        TestAssert.True(hasScrollableBody, "使用说明正文应只读且可滚动");
        TestAssert.True(
            dialogText.Contains("F6")
            && dialogText.Contains("F7")
            && dialogText.Contains("F8"),
            "使用说明应包含当前快捷键说明");
        TestAssert.True(
            dialogText.Contains("缩放") && dialogText.Contains("重新采点"),
            "使用说明应包含屏幕缩放注意事项");
        TestAssert.True(
            dialogText.Contains("应用全部") && dialogText.Contains("新采集点"),
            "使用说明应包含全局时间设置和新点继承说明");
        TestAssert.True(
            dialogText.Contains("导入为副本") && dialogText.Contains("导出方案"),
            "使用说明应解释方案导入和导出语义");
        TestAssert.True(
            dialogText.Contains("方案下拉")
            && dialogText.Contains("打开方案目录")
            && dialogText.Contains("保存 / 不保存 / 取消"),
            "使用说明应解释本机方案目录和切换保护");
        TestAssert.True(dialogUsesCurrentTheme, "使用说明弹窗应使用当前主题");
    }

    private static void AssertMinimumWindowLayout()
    {
        using var form = CreateLaidOutForm();
        form.Size = form.MinimumSize;
        Application.DoEvents();
        PerformLayout(form);
        AssertControlsStayInsideParents(form);
    }

    private static void RaiseClick(Control control)
    {
        var method = typeof(Control).GetMethod(
            "OnClick",
            BindingFlags.Instance | BindingFlags.NonPublic);
        method!.Invoke(control, [EventArgs.Empty]);
    }

    private static void ApplyProfile(MainForm form, ClickProfile profile)
    {
        var method = typeof(MainForm).GetMethod(
            "ApplyProfile",
            BindingFlags.Instance | BindingFlags.NonPublic);
        method!.Invoke(form, [profile]);
        Application.DoEvents();
    }

    private static IReadOnlyList<ClickPoint> GetPoints(MainForm form)
    {
        var field = typeof(MainForm).GetField(
            "_points",
            BindingFlags.Instance | BindingFlags.NonPublic);
        return ((System.ComponentModel.BindingList<ClickPoint>)field!.GetValue(form)!).ToList();
    }

    private static void RenderDialog(
        string buttonName,
        string dialogName,
        string outputPath)
    {
        using var form = CreateLaidOutForm();
        var button = EnumerateControls(form)
            .OfType<Button>()
            .Single(candidate => candidate.Name == buttonName);
        var rendered = false;
        using var timer = new System.Windows.Forms.Timer { Interval = 30 };
        timer.Tick += (_, _) =>
        {
            var dialog = Application.OpenForms
                .Cast<Form>()
                .FirstOrDefault(openForm => openForm.Name == dialogName);
            if (dialog is null || rendered)
            {
                return;
            }

            CreateHandles(dialog);
            PerformLayout(dialog);
            using var bitmap = new Bitmap(dialog.Width, dialog.Height);
            dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, dialog.Size));
            var fullPath = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            bitmap.Save(fullPath, System.Drawing.Imaging.ImageFormat.Png);
            Console.WriteLine($"Dialog rendered: {fullPath}");
            rendered = true;
            dialog.Close();
        };

        timer.Start();
        button.PerformClick();
        timer.Stop();
        TestAssert.True(rendered, $"应成功渲染 {dialogName}");
    }

    private static IEnumerable<Control> EnumerateControls(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;

            foreach (var descendant in EnumerateControls(child))
            {
                yield return descendant;
            }
        }
    }

    private static void AssertDarkSurface(Color color, string description)
    {
        TestAssert.True(color.GetBrightness() < 0.32f,
            $"{description}应使用深色，当前颜色为 {color}");
    }

    private static void AssertLightText(Color color, string description)
    {
        TestAssert.True(color.GetBrightness() > 0.62f,
            $"{description}应使用浅色，当前颜色为 {color}");
    }
}
