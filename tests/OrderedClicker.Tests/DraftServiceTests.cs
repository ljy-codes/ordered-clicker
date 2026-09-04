using OrderedClicker.Models;
using OrderedClicker.Services;

namespace OrderedClicker.Tests;

internal static class DraftServiceTests
{
    public static void Run()
    {
        SavesLoadsAndDiscardsDraft();
        FailedWritePreservesPreviousDraft();
        DetectsSourceFileConflicts();
        QuarantinesBrokenDraft();
    }

    private static void SavesLoadsAndDiscardsDraft()
    {
        using var directory = new TemporaryDirectory();
        var service = new DraftService(directory.Path);
        var sourcePath = Path.Combine(directory.Path, "source.oclick");
        var envelope = new DraftEnvelope(
            new ClickProfile { Name = "未保存工作" },
            sourcePath,
            DateTime.UtcNow.AddMinutes(-1),
            DateTime.UtcNow);

        service.Save(envelope);
        var loaded = service.Load();

        TestAssert.True(service.Exists, "保存后应存在活动草稿");
        TestAssert.Equal("未保存工作", loaded!.Profile.Name, "草稿应保留方案内容");
        TestAssert.Equal(sourcePath, loaded.SourcePath, "草稿应保留来源路径");

        service.Discard();

        TestAssert.True(!service.Exists, "丢弃后不应再有活动草稿");
    }

    private static void FailedWritePreservesPreviousDraft()
    {
        using var directory = new TemporaryDirectory();
        var service = new DraftService(directory.Path);
        service.Save(new DraftEnvelope(
            new ClickProfile { Name = "旧草稿" },
            null,
            null,
            DateTime.UtcNow));
        Directory.CreateDirectory(service.DraftPath + ".tmp");

        TestAssert.Throws<UnauthorizedAccessException>(
            () => service.Save(new DraftEnvelope(
                new ClickProfile { Name = "新草稿" },
                null,
                null,
                DateTime.UtcNow)),
            "临时文件不可写时保存应失败");

        TestAssert.Equal("旧草稿", service.Load()!.Profile.Name,
            "草稿写入失败时必须保留上一次成功内容");
    }

    private static void DetectsSourceFileConflicts()
    {
        using var directory = new TemporaryDirectory();
        var sourcePath = Path.Combine(directory.Path, "source.oclick");
        File.WriteAllText(sourcePath, "v1");
        var sourceUpdatedAt = File.GetLastWriteTimeUtc(sourcePath);
        var unchanged = new DraftEnvelope(
            new ClickProfile(),
            sourcePath,
            sourceUpdatedAt,
            DateTime.UtcNow,
            ProfileService.ComputeFileSha256(sourcePath));

        TestAssert.True(
            !DraftService.HasSourceChanged(unchanged),
            "来源文件未变化时应允许保存回原路径");
        TestAssert.True(
            !DraftService.HasSourceChanged(unchanged with
            {
                SourcePath = null,
                SourceUpdatedAtUtc = null
            }),
            "新建方案没有来源文件时不应判定冲突");
        TestAssert.True(
            DraftService.HasSourceChanged(unchanged with
            {
                SourceUpdatedAtUtc = null
            }),
            "缺少来源时间戳时应保守判定冲突");
        TestAssert.True(
            DraftService.HasSourceChanged(unchanged with
            {
                SourceSha256 = null
            }),
            "缺少来源指纹时应保守判定冲突");

        File.WriteAllText(sourcePath, "v2");
        File.SetLastWriteTimeUtc(sourcePath, sourceUpdatedAt);
        TestAssert.True(
            DraftService.HasSourceChanged(unchanged),
            "来源内容变化时即使时间戳相同也应要求另存");

        File.WriteAllText(sourcePath, "v1");
        File.SetLastWriteTimeUtc(sourcePath, sourceUpdatedAt.AddSeconds(2));
        TestAssert.True(
            DraftService.HasSourceChanged(unchanged),
            "来源文件时间戳变化时应要求另存");

        File.Delete(sourcePath);
        TestAssert.True(
            DraftService.HasSourceChanged(unchanged),
            "来源文件被删除时应要求另存");
    }

    private static void QuarantinesBrokenDraft()
    {
        using var directory = new TemporaryDirectory();
        var service = new DraftService(directory.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(service.DraftPath)!);
        File.WriteAllText(service.DraftPath, "{broken");

        var brokenPath = service.QuarantineBrokenDraft();

        TestAssert.True(!service.Exists, "损坏草稿隔离后不应继续触发恢复");
        TestAssert.True(File.Exists(brokenPath), "损坏草稿应保留为可诊断文件");
        TestAssert.True(
            brokenPath.EndsWith(".broken.json", StringComparison.OrdinalIgnoreCase),
            "隔离文件名应明确标记为损坏草稿");
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
