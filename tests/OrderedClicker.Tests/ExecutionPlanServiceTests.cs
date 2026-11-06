using OrderedClicker.Core;
using OrderedClicker.Models;

namespace OrderedClicker.Tests;

internal static class ExecutionPlanServiceTests
{
    public static void Run()
    {
        KeepsSourceRowIndexesAndCounts();
        ResolvesCloudDesktopCoordinates();
        CustomRegionOverridesCloudAndDesktopFallbacks();
        PlanPreservesAnIndependentEditingSnapshot();
        RejectsAnInvalidCustomRegion();
        RoundTripsSnapshotsAndAcceptsLegacyOptionalFields();
    }

    private static void RoundTripsSnapshotsAndAcceptsLegacyOptionalFields()
    {
        var profile = new ClickProfile { Points = [CreatePoint(10, 20, 1)] };
        var plan = ExecutionPlanService.Create(profile, new ScreenBounds(0, 0, 200, 100));
        var json = System.Text.Json.JsonSerializer.Serialize(plan);
        var restored = System.Text.Json.JsonSerializer.Deserialize<ExecutionPlan>(json)!;
        TestAssert.Equal(profile.ProfileId, restored.SourceProfile!.ProfileId,
            "持久化的执行计划应能恢复原编辑快照");
        var legacy = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        legacy.AsObject().Remove("SourceProfile");
        legacy.AsObject().Remove("TargetWindow");
        var oldPlan = System.Text.Json.JsonSerializer.Deserialize<ExecutionPlan>(legacy.ToJsonString())!;
        TestAssert.True(oldPlan.SourceProfile is null && oldPlan.TargetWindow is null,
            "旧断点缺少新增字段时仍应能读取");
        var oldProfile = System.Text.Json.JsonSerializer.Deserialize<ClickProfile>(
            """{"FormatVersion":4,"Points":[],"ScreenStability":{"Enabled":false}}""")!;
        TestAssert.Equal(4, oldProfile.FormatVersion, "可选安全设置不得改变旧 v4 格式");
        TestAssert.True(oldProfile.TargetWindow is null && oldProfile.ScreenStability.Region is null,
            "旧方案缺少新增字段时应保持原有行为");
    }

    private static void CustomRegionOverridesCloudAndDesktopFallbacks()
    {
        var desktop = new ScreenBounds(-500, 0, 2000, 1000);
        var profile = new ClickProfile { Points = [CreatePoint(1, 2, 1)] };
        TestAssert.Equal(desktop, ExecutionPlanService.Create(profile, desktop).StabilityRegion,
            "未设置区域时应检测整个桌面");
        profile.CoordinateMode = CoordinateMode.CloudDesktopRegion;
        profile.CloudDesktopRegion = new CloudDesktopRegion
        {
            X = 100, Y = 100, Width = 400, Height = 300
        };
        profile.Points[0].RelativeX = 0.5;
        profile.Points[0].RelativeY = 0.5;
        TestAssert.Equal(new ScreenBounds(100, 100, 400, 300),
            ExecutionPlanService.Create(profile, desktop).StabilityRegion,
            "云桌面模式应默认使用云桌面区域");
        profile.ScreenStability.Region = new ScreenBounds(-100, 30, 80, 60);
        var plan = ExecutionPlanService.Create(profile, desktop);
        TestAssert.Equal(new ScreenBounds(-100, 30, 80, 60), plan.StabilityRegion,
            "自定义稳定检测区域应优先于云桌面区域");
        TestAssert.Equal(profile.ScreenStability.Region, plan.ScreenStability.Region,
            "计划设置应保留自定义区域");
    }

    private static void PlanPreservesAnIndependentEditingSnapshot()
    {
        var profile = new ClickProfile
        {
            Name = "snapshot",
            DefaultClickIntervalMs = 250,
            DefaultAfterDelayMs = 350,
            CloudDesktopRegion = new CloudDesktopRegion { X = 5, Y = 6, Width = 100, Height = 200 },
            TargetWindow = new TargetWindowBinding
            {
                Platform = "windows", ApplicationId = "app", WindowTitle = "title"
            },
            Points = [CreatePoint(10, 20, 1), CreatePoint(30, 40, 2)]
        };
        profile.Points[1].Enabled = false;
        profile.Points[0].MonitorDeviceName = "monitor";
        profile.Points[0].MonitorBounds = new ScreenBounds(0, 0, 200, 100);
        profile.Points[0].CapturedDpi = 144;
        profile.ScreenStability.Region = new ScreenBounds(1, 2, 10, 20);
        var plan = ExecutionPlanService.Create(profile, new ScreenBounds(0, 0, 200, 100));
        var snapshot = plan.SourceProfile ?? throw new InvalidOperationException("缺少原始编辑快照");
        var json = System.Text.Json.JsonSerializer.Serialize(profile);
        TestAssert.Equal(json, System.Text.Json.JsonSerializer.Serialize(snapshot),
            "快照必须保留身份、默认值、禁用行及显示器元数据");
        profile.Points[0].X = 99;
        profile.Points.Clear();
        profile.TargetWindow.WindowTitle = "changed";
        profile.ScreenStability.Region = null;
        profile.CloudDesktopRegion.X = 90;
        TestAssert.Equal(2, snapshot.Points.Count, "原编辑点位列表变化不得污染快照");
        TestAssert.Equal(10, snapshot.Points[0].X, "快照点位必须深拷贝");
        TestAssert.Equal("title", snapshot.TargetWindow!.WindowTitle, "快照窗口绑定必须深拷贝");
        TestAssert.Equal(5, snapshot.CloudDesktopRegion!.X, "快照云桌面区域必须深拷贝");
        snapshot.TargetWindow.WindowTitle = "snapshot changed";
        snapshot.ScreenStability.Enabled = true;
        TestAssert.Equal("title", plan.TargetWindow!.WindowTitle, "计划绑定也不得与快照共用可变对象");
        TestAssert.True(!plan.ScreenStability.Enabled, "计划设置不得与快照共用可变对象");
    }

    private static void RejectsAnInvalidCustomRegion()
    {
        var profile = new ClickProfile { Points = [CreatePoint(10, 20, 1)] };
        profile.ScreenStability.Region = new ScreenBounds(90, 90, 20, 20);
        TestAssert.Throws<InvalidOperationException>(() =>
                ExecutionPlanService.Create(profile, new ScreenBounds(0, 0, 100, 100)),
            "创建计划时也必须拒绝越界区域，防止绕过 UI 校验");
    }

    private static void KeepsSourceRowIndexesAndCounts()
    {
        var disabled = CreatePoint(10, 20, 2);
        disabled.Enabled = false;
        var profile = new ClickProfile
        {
            TotalLoops = 3,
            Points =
            [
                CreatePoint(1, 2, 1),
                disabled,
                CreatePoint(3, 4, 2)
            ]
        };

        var plan = ExecutionPlanService.Create(
            profile,
            new ScreenBounds(0, 0, 1920, 1080));

        TestAssert.Equal(2, plan.Points.Count, "执行计划只能包含启用点位");
        TestAssert.Equal(0, plan.Points[0].SourceIndex, "首点应保留原始行号");
        TestAssert.Equal(2, plan.Points[1].SourceIndex, "跳过禁用点后仍应保留原始行号");
        TestAssert.Equal(6L, plan.PlannedPointExecutionCount, "点次总数应包含循环");
        TestAssert.Equal(9L, plan.PlannedClickCount, "点击总数应包含循环");
        TestAssert.Equal("2", string.Join(",", plan.DisabledSourceRowNumbers),
            "禁用行应按用户可见行号报告");
    }

    private static void ResolvesCloudDesktopCoordinates()
    {
        var profile = new ClickProfile
        {
            CoordinateMode = CoordinateMode.CloudDesktopRegion,
            CloudDesktopRegion = new CloudDesktopRegion
            {
                X = 100,
                Y = 200,
                Width = 1000,
                Height = 600
            },
            Points =
            [
                new ClickPoint
                {
                    X = 0,
                    Y = 0,
                    RelativeX = 0.5,
                    RelativeY = 0.5,
                    ClickCount = 1,
                    ClickIntervalMs = 10,
                    AfterDelayMs = 0
                }
            ]
        };

        var plan = ExecutionPlanService.Create(
            profile,
            new ScreenBounds(0, 0, 1920, 1080));

        TestAssert.Equal(600, plan.Points[0].X, "云桌面 X 应在运行前解析");
        TestAssert.Equal(500, plan.Points[0].Y, "云桌面 Y 应在运行前解析");
    }

    private static ClickPoint CreatePoint(int x, int y, int clickCount)
    {
        return new ClickPoint
        {
            X = x,
            Y = y,
            ClickCount = clickCount,
            ClickIntervalMs = 10,
            AfterDelayMs = 0
        };
    }
}
