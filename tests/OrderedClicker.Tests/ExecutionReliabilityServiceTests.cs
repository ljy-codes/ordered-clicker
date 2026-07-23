using System.Text.Json;
using OrderedClicker.Core;
using OrderedClicker.Models;
using OrderedClicker.Services;

namespace OrderedClicker.Tests;

internal static class ExecutionReliabilityServiceTests
{
    public static void Run()
    {
        WritesExecutionCountsAndLastSourceRow();
    }

    private static void WritesExecutionCountsAndLastSourceRow()
    {
        using var directory = new TemporaryDirectory();
        var plan = ExecutionPlanService.Create(
            new ClickProfile
            {
                TotalLoops = 2,
                Points =
                [
                    new ClickPoint
                    {
                        X = 10,
                        Y = 20,
                        ClickCount = 3,
                        ClickIntervalMs = 10,
                        AfterDelayMs = 0
                    }
                ]
            },
            new ScreenBounds(0, 0, 1920, 1080));
        var result = new ExecutionResult(
            ExecutionOutcome.Stopped,
            plan.PlannedPointExecutionCount,
            1,
            plan.PlannedClickCount,
            4,
            0,
            new ExecutionCheckpoint(1, 0, 1, 1, 4),
            "执行已停止",
            TimeSpan.FromSeconds(2));

        var path = new ExecutionLogService(directory.Path).Write(plan, result);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;

        TestAssert.Equal(2L,
            root.GetProperty("PlannedPointExecutionCount").GetInt64(),
            "日志应记录计划点次");
        TestAssert.Equal(4L,
            root.GetProperty("CompletedClickCount").GetInt64(),
            "日志应记录已完成点击数");
        TestAssert.Equal(1,
            root.GetProperty("LastSourceRow").GetInt32(),
            "日志应使用用户可见的原始行号");
        TestAssert.Equal("Stopped",
            root.GetProperty("Outcome").GetString(),
            "日志应记录执行结果");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                AppContext.BaseDirectory,
                "test-data",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, true);
            }
        }
    }
}
