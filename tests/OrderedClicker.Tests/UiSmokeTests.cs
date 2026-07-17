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
        AssertThemeSettingsDialog(form);
        AssertConfiguredThemeLoads();
        AssertThemePreviewCancelAndSave();
        AssertUsageHelpDialog(form);
        AssertMinimumWindowLayout();
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

    private static void AssertThemeSettingsDialog(MainForm form)
    {
        var settingsButton = EnumerateControls(form)
            .OfType<Button>()
            .Single(button => button.Name == "ThemeSettingsButton");
        var dialogOpened = false;
        var themeCardCount = 0;
        var dialogUsesCurrentTheme = false;
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
            dialogUsesCurrentTheme = dialog.BackColor == form.BackColor;
            dialog.Close();
        };

        timer.Start();
        settingsButton.PerformClick();
        timer.Stop();

        TestAssert.True(dialogOpened, "点击主题设置按钮后应打开设置弹窗");
        TestAssert.Equal(5, themeCardCount, "设置弹窗应显示五张主题卡片");
        TestAssert.True(dialogUsesCurrentTheme, "设置弹窗应使用当前主题");
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
            dialogText.Contains("F8")
            && dialogText.Contains("F9")
            && dialogText.Contains("F10"),
            "使用说明应包含快捷键说明");
        TestAssert.True(
            dialogText.Contains("缩放") && dialogText.Contains("重新采点"),
            "使用说明应包含屏幕缩放注意事项");
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
