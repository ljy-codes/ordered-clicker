using OrderedClicker.Core;
using OrderedClicker.Models;
using OrderedClicker.Services;

namespace OrderedClicker.Tests;

internal static class ExecutionCheckpointServiceTests
{
    public static void Run()
    {
        SavesLoadsClearsAndQuarantinesCheckpoint();
    }

    private static void SavesLoadsClearsAndQuarantinesCheckpoint()
    {
        using var directory = new TemporaryDirectory();
        var service = new ExecutionCheckpointService(directory.Path);
        var plan = ExecutionPlanService.Create(
            new ClickProfile
            {
                Points = [new ClickPoint { X = 10, Y = 20, ClickCount = 2 }]
            },
            new ScreenBounds(0, 0, 1920, 1080));
        var checkpoint = new ExecutionCheckpoint(
            0, 0, 1, ExecutionStage.ClickInterval, 0, 1);
        service.Save(new ExecutionCheckpointEnvelope(
            1,
            "2.1.0",
            plan,
            plan.ProfileFingerprint,
            checkpoint,
            DateTimeOffset.UtcNow));

        var loaded = service.Load();

        TestAssert.Equal(checkpoint, loaded!.Checkpoint, "断点应完整往返");
        TestAssert.Equal(plan.ProfileFingerprint, loaded.PlanFingerprint,
            "断点应保留执行计划指纹");
        service.Clear();
        TestAssert.True(service.Load() is null, "清理后不应存在断点");

        File.WriteAllText(service.CheckpointPath, "{broken");
        var brokenPath = service.QuarantineBrokenCheckpoint();
        TestAssert.True(File.Exists(brokenPath), "损坏断点应被隔离");
        TestAssert.True(!File.Exists(service.CheckpointPath), "隔离后活动断点应消失");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
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
