using OrderedClicker.Models;
using OrderedClicker.Services;

namespace OrderedClicker.Tests;

internal static class ProfileServiceTests
{
    public static void Run()
    {
        RecoversNewestValidPreviousVersion();
        PreservesFormalFileDuringOrphanRecovery();
        ReturnsHashForTheLoadedBytes();
        KeepsFormalPathPresentDuringExpectedSave();
        PreservesPreviousReaderAndBackupDuringExpectedSave();
        SavesExpectedVersionUnderLongUnicodePath();
        RejectsConcurrentExpectedSaves();
        PersistsV4IdentityAndTimestamps();
        RenewsIdentityWhenSavingAs();
        CreatesBackupOnlyForExplicitOverwrite();
        RejectsExternalModificationConflict();
        RejectsActiveExternalWriterAsConflict();
        DoesNotUseSharedTemporaryPath();
        DoesNotOverwriteConcurrentCreation();
        AvoidsSilentNameBasedOverwrite();
        RoundTripsAllProfileFields();
        SavesToExplicitPath();
        SuggestsDistinctPathForImportedCopy();
        CreatesAndListsLocalProfileDirectory();
        ListsOnlyJsonProfilesInNaturalOrder();
        ComparesProfileSnapshots();
        ExportsAndImportsCompleteProfile();
        RejectsUnsupportedFutureVersion();
        RejectsInvalidPointTiming();
        RejectsInvalidCloudRelativeCoordinate();
        RejectsNullPointWithControlledError();
        ReturnsControlledErrorForMalformedJson();
    }

    private static void RecoversNewestValidPreviousVersion()
    {
        using var directory = new TemporaryDirectory();
        var service = new ProfileService(directory.Path);
        var destination = Path.Combine(directory.Path, "recovery.oclick");
        var profile = CreateCompleteProfile();
        service.Save(profile, destination);
        var older = $"{destination}.save-{Guid.NewGuid():N}.previous";
        File.Move(destination, older);
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddMinutes(-3));
        profile.TotalLoops = 17;
        var staging = Path.Combine(directory.Path, "staging.oclick");
        service.Save(profile, staging);
        var newer = $"{destination}.save-{Guid.NewGuid():N}.previous";
        File.Move(staging, newer);
        File.SetLastWriteTimeUtc(newer, DateTime.UtcNow.AddMinutes(-2));
        var invalid = $"{destination}.save-{Guid.NewGuid():N}.previous";
        File.WriteAllText(invalid, "{broken");
        File.SetLastWriteTimeUtc(invalid, DateTime.UtcNow.AddMinutes(-1));

        var result = service.Load(destination);

        TestAssert.True(result.Success, "缺失正式文件时应恢复有效的孤留版本");
        TestAssert.Equal(17, result.Profile!.TotalLoops, "应选择最近的有效孤留版本");
        TestAssert.True(File.Exists(older) && File.Exists(invalid),
            "恢复不得删除其他候选或无效证据");
    }

    private static void PreservesFormalFileDuringOrphanRecovery()
    {
        using var directory = new TemporaryDirectory();
        var service = new ProfileService(directory.Path);
        var destination = Path.Combine(directory.Path, "formal.oclick");
        var profile = CreateCompleteProfile();
        service.Save(profile, destination);
        var orphan = $"{destination}.save-{Guid.NewGuid():N}.previous";
        File.Copy(destination, orphan);
        profile.TotalLoops = 21;
        service.Save(profile, destination, new ProfileSaveOptions(AllowOverwrite: true));
        var content = File.ReadAllText(destination);

        service.ListLocalProfiles();
        var result = service.Load(destination);

        TestAssert.Equal(21, result.Profile!.TotalLoops, "孤留副本不得替换正式方案");
        TestAssert.Equal(content, File.ReadAllText(destination), "恢复不得写入已有正式文件");
        TestAssert.True(File.Exists(orphan), "已有正式文件时保留孤留副本");
    }

    private static void ReturnsHashForTheLoadedBytes()
    {
        using var directory = new TemporaryDirectory();
        var service = new ProfileService(directory.Path);
        var destination = Path.Combine(directory.Path, "hash.oclick");
        service.Save(CreateCompleteProfile(), destination);
        var expected = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(destination)));

        var loaded = service.Load(destination);
        var imported = service.Import(destination);
        File.WriteAllText(destination, "external-change");

        TestAssert.Equal(expected, loaded.SourceSha256, "加载哈希应来自解析的同一批字节");
        TestAssert.Equal(expected, imported.SourceSha256, "导入不得丢失来源哈希");
    }

    private static void PreservesPreviousReaderAndBackupDuringExpectedSave()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var directory = new TemporaryDirectory();
        var service = new ProfileService(directory.Path);
        var destination = Path.Combine(directory.Path, "atomic.oclick");
        var profile = CreateCompleteProfile();
        service.Save(profile, destination);
        var hash = ProfileService.ComputeFileSha256(destination);
        var previous = File.ReadAllText(destination);
        using var reader = new StreamReader(new FileStream(destination,
            FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete));
        profile.TotalLoops++;

        service.Save(profile, destination, new ProfileSaveOptions(
            CreateBackup: true, AllowOverwrite: true, ExpectedExistingSha256: hash));

        TestAssert.Equal(previous, reader.ReadToEnd(),
            "替换期间已打开的只读句柄应保留旧版本");
        TestAssert.Equal(previous, File.ReadAllText(destination + ".bak"),
            "原子提交备份应精确保留前一版本");
        TestAssert.Equal(profile.TotalLoops, service.Load(destination).Profile!.TotalLoops,
            "正式路径应指向完整的新版本");
    }

    private static void KeepsFormalPathPresentDuringExpectedSave()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var directory = new TemporaryDirectory();
        var service = new ProfileService(directory.Path);
        var destination = Path.Combine(directory.Path, "continuous.oclick");
        var profile = CreateCompleteProfile();
        service.Save(profile, destination);
        using var stop = new CancellationTokenSource();
        using var ready = new ManualResetEventSlim();
        var missing = false;
        var observer = new Thread(() =>
        {
            ready.Set();
            while (!stop.IsCancellationRequested)
            {
                if (!File.Exists(destination))
                {
                    missing = true;
                }
            }
        }) { IsBackground = true };
        observer.Start();
        ready.Wait();
        try
        {
            for (var index = 0; index < 32; index++)
            {
                var hash = ProfileService.ComputeFileSha256(destination);
                profile.TotalLoops++;
                service.Save(profile, destination, new ProfileSaveOptions(
                    CreateBackup: true, AllowOverwrite: true, ExpectedExistingSha256: hash));
                TestAssert.Equal(profile.TotalLoops, service.Load(destination).Profile!.TotalLoops,
                    "每次保存后的正式路径必须包含新版本");
            }
        }
        finally
        {
            stop.Cancel();
            observer.Join();
        }

        TestAssert.True(!missing, "保存期间正式方案路径不得短暂消失");
        TestAssert.Equal(2, Directory.EnumerateFiles(directory.Path).Count(),
            "保存不得生成正式文件和备份以外的意外文件名");
    }

    private static void SavesExpectedVersionUnderLongUnicodePath()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var directory = new TemporaryDirectory();
        var nested = Path.Combine(directory.Path, new string('长', 180));
        var service = new ProfileService(nested);
        var profile = CreateCompleteProfile();
        var destination = Path.Combine(nested, "中文方案.oclick");
        service.Save(profile, destination);
        var hash = ProfileService.ComputeFileSha256(destination);
        profile.TotalLoops++;

        service.Save(profile, destination, new ProfileSaveOptions(
            CreateBackup: true, AllowOverwrite: true, ExpectedExistingSha256: hash));

        TestAssert.Equal(profile.TotalLoops, service.Load(destination).Profile!.TotalLoops,
            "长中文路径必须可覆盖保存并读取新版本");
        TestAssert.Equal(profile.TotalLoops - 1,
            service.Load(destination + ".bak").Profile!.TotalLoops,
            "长中文路径备份必须保留上一版本");
        TestAssert.Equal(2, Directory.EnumerateFiles(nested).Count(),
            "长路径保存不得产生带意外后缀的文件");
    }

    private static void RejectsConcurrentExpectedSaves()
    {
        using var directory = new TemporaryDirectory();
        var service = new ProfileService(directory.Path);
        var destination = Path.Combine(directory.Path, "same-process.oclick");
        var original = CreateCompleteProfile();
        service.Save(original, destination);
        var hash = ProfileService.ComputeFileSha256(destination);
        var successes = 0;
        var conflicts = 0;
        Parallel.For(0, 2, index =>
        {
            var profile = ProfileService.CloneProfile(original);
            profile.TotalLoops = 30 + index;
            try
            {
                new ProfileService(directory.Path).Save(profile, destination,
                    new ProfileSaveOptions(AllowOverwrite: true, ExpectedExistingSha256: hash));
                Interlocked.Increment(ref successes);
            }
            catch (ProfileConflictException)
            {
                Interlocked.Increment(ref conflicts);
            }
        });
        TestAssert.Equal(1, successes, "相同旧哈希的并发保存仅一个可提交");
        TestAssert.Equal(1, conflicts, "第二个同进程保存必须报告冲突");
    }

    private static void PersistsV4IdentityAndTimestamps()
    {
        using var directory = new TemporaryDirectory();
        var service = new ProfileService(directory.Path);
        var source = CreateCompleteProfile();

        var path = service.Save(source);
        var loaded = service.Load(path);

        TestAssert.True(path.EndsWith(".oclick", StringComparison.OrdinalIgnoreCase),
            "v4 方案应使用 .oclick 扩展名");
        TestAssert.True(loaded.Success, loaded.ErrorMessage ?? "v4 方案应可加载");
        TestAssert.Equal(4, loaded.Profile!.FormatVersion, "方案格式版本应为 4");
        TestAssert.Equal(source.ProfileId, loaded.Profile.ProfileId, "保存后方案身份应稳定");
        TestAssert.Equal(source.CreatedAtUtc, loaded.Profile.CreatedAtUtc, "创建时间应保持");
        TestAssert.True(loaded.Profile.UpdatedAtUtc >= source.CreatedAtUtc,
            "更新时间不应早于创建时间");
    }

    private static void RenewsIdentityWhenSavingAs()
    {
        using var directory = new TemporaryDirectory();
        var service = new ProfileService(directory.Path);
        var profile = CreateCompleteProfile();
        var originalId = profile.ProfileId;
        var destination = Path.Combine(directory.Path, "副本.oclick");

        service.Save(
            profile,
            destination,
            new ProfileSaveOptions(RenewIdentity: true));

        TestAssert.True(profile.ProfileId != originalId, "另存为应生成新的方案身份");
        TestAssert.True(File.Exists(destination), "另存为目标应存在");
    }

    private static void CreatesBackupOnlyForExplicitOverwrite()
    {
        using var directory = new TemporaryDirectory();
        var service = new ProfileService(directory.Path);
        var destination = Path.Combine(directory.Path, "稳定方案.oclick");
        var profile = CreateCompleteProfile();
        service.Save(profile, destination);
        var firstContent = File.ReadAllText(destination);
        profile.TotalLoops++;

        TestAssert.Throws<IOException>(
            () => service.Save(profile, destination),
            "未明确允许覆盖时不应替换已有文件");

        service.Save(
            profile,
            destination,
            new ProfileSaveOptions(CreateBackup: true, AllowOverwrite: true));

        TestAssert.True(File.Exists(destination + ".bak"), "显式覆盖应创建备份");
        TestAssert.Equal(firstContent, File.ReadAllText(destination + ".bak"),
            "备份应保留覆盖前内容");
    }

    private static void RejectsExternalModificationConflict()
    {
        using var directory = new TemporaryDirectory();
        var service = new ProfileService(directory.Path);
        var destination = Path.Combine(directory.Path, "并发方案.oclick");
        var profile = CreateCompleteProfile();
        service.Save(profile, destination);
        var expectedHash = ProfileService.ComputeFileSha256(destination);
        File.WriteAllText(destination, "external-change");

        TestAssert.Throws<ProfileConflictException>(
            () => service.Save(
                profile,
                destination,
                new ProfileSaveOptions(
                    CreateBackup: true,
                    AllowOverwrite: true,
                    ExpectedExistingSha256: expectedHash)),
            "外部修改后的方案不应被静默覆盖");
        TestAssert.Equal(
            "external-change",
            File.ReadAllText(destination),
            "保存冲突时必须保留外部写入内容");
    }

    private static void RejectsActiveExternalWriterAsConflict()
    {
        using var directory = new TemporaryDirectory();
        var service = new ProfileService(directory.Path);
        var destination = Path.Combine(directory.Path, "活动写入方案.oclick");
        var profile = CreateCompleteProfile();
        service.Save(profile, destination);
        var expectedHash = ProfileService.ComputeFileSha256(destination);
        using var externalWriter = new FileStream(
            destination,
            FileMode.Open,
            FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete);

        TestAssert.Throws<ProfileConflictException>(
            () => service.Save(
                profile,
                destination,
                new ProfileSaveOptions(
                    CreateBackup: true,
                    AllowOverwrite: true,
                    ExpectedExistingSha256: expectedHash)),
            "外部程序持有写句柄时应作为保存冲突处理");
    }

    private static void DoesNotUseSharedTemporaryPath()
    {
        using var directory = new TemporaryDirectory();
        var service = new ProfileService(directory.Path);
        var destination = Path.Combine(directory.Path, "独立临时文件.oclick");
        Directory.CreateDirectory(destination + ".tmp");

        service.Save(CreateCompleteProfile(), destination);

        TestAssert.True(File.Exists(destination),
            "固定 .tmp 路径被占用时仍应使用独立临时文件完成保存");
    }

    private static void DoesNotOverwriteConcurrentCreation()
    {
        using var directory = new TemporaryDirectory();
        var service = new ProfileService(directory.Path);
        var destination = Path.Combine(directory.Path, "并发新建.oclick");
        var profile = CreateCompleteProfile();
        profile.Points = Enumerable.Range(0, 50000)
            .Select(_ => new ClickPoint
            {
                Enabled = true,
                X = 400,
                Y = 300,
                RelativeX = 0.25,
                RelativeY = 0.275,
                ClickCount = 1,
                ClickIntervalMs = 100,
                AfterDelayMs = 0
            })
            .ToList();
        using var targetCreated = new ManualResetEventSlim();
        using var observing = new CancellationTokenSource();
        using var ready = new ManualResetEventSlim();
        // macOS FileSystemWatcher coalesces events after the rename; observe the
        // temp file directly so the concurrent creation really precedes commit.
        var creator = new Thread(() =>
        {
            ready.Set();
            while (!observing.IsCancellationRequested)
            {
                if (Directory.EnumerateFiles(directory.Path, "*.tmp").Any())
                {
                    File.WriteAllText(destination, "external-create");
                    targetCreated.Set();
                    return;
                }
                Thread.Yield();
            }
        }) { IsBackground = true };
        creator.Start();
        ready.Wait();
        try
        {
            TestAssert.Throws<IOException>(
                () => service.Save(profile, destination),
                "禁止覆盖时不应替换保存期间由其他程序创建的目标文件");
        }
        finally
        {
            observing.Cancel();
            creator.Join();
        }
        TestAssert.True(targetCreated.Wait(TimeSpan.FromSeconds(2)),
            "测试应在提交前模拟外部创建目标文件");
        TestAssert.Equal("external-create", File.ReadAllText(destination),
            "并发创建的目标文件必须保持不变");
    }

    private static void AvoidsSilentNameBasedOverwrite()
    {
        using var directory = new TemporaryDirectory();
        var service = new ProfileService(directory.Path);
        var first = CreateCompleteProfile();
        first.Name = "同名方案";
        var second = CreateCompleteProfile();
        second.Name = "同名方案";

        var firstPath = service.Save(first);
        var secondPath = service.Save(second);

        TestAssert.True(!ProfileService.PathsEqual(firstPath, secondPath),
            "不同身份的同名方案不应静默覆盖");
        TestAssert.True(File.Exists(firstPath) && File.Exists(secondPath),
            "两个同名方案都应保留");
    }

    private static void ExportsAndImportsCompleteProfile()
    {
        using var directory = new TemporaryDirectory();
        var service = new ProfileService(directory.Path);
        var path = System.IO.Path.Combine(directory.Path, "share", "方案.oclick");
        var source = CreateCompleteProfile();

        var exportedPath = service.Export(source, path);
        var imported = service.Import(exportedPath);

        TestAssert.Equal(path, exportedPath, "导出应返回用户选择的路径");
        TestAssert.True(imported.Success, imported.ErrorMessage ?? "导入应成功");
        TestAssert.Equal(source.Name, imported.Profile!.Name, "导入应保留方案名称");
        TestAssert.Equal(source.Points.Count, imported.Profile.Points.Count, "导入应保留全部点位");
        TestAssert.Equal(source.CoordinateMode, imported.Profile.CoordinateMode, "导入应保留坐标模式");
        TestAssert.Equal(
            source.ScreenStability.TimeoutMs,
            imported.Profile.ScreenStability.TimeoutMs,
            "导入应保留画面稳定等待设置");
    }

    private static void RejectsUnsupportedFutureVersion()
    {
        using var directory = new TemporaryDirectory();
        var service = new ProfileService(directory.Path);
        var path = System.IO.Path.Combine(directory.Path, "future.oclick");
        File.WriteAllText(
            path,
            """{"FormatVersion":99,"Name":"未来方案","TotalLoops":1,"Points":[]}""",
            System.Text.Encoding.UTF8);

        var result = service.Import(path);

        TestAssert.True(!result.Success, "未来版本应被拒绝");
        TestAssert.True(
            (result.ErrorMessage ?? string.Empty).Contains("版本", StringComparison.Ordinal),
            "错误应说明版本不受支持");
    }

    private static void RejectsInvalidPointTiming()
    {
        using var directory = new TemporaryDirectory();
        var service = new ProfileService(directory.Path);
        var profile = CreateCompleteProfile();
        profile.CoordinateMode = CoordinateMode.AbsoluteScreen;
        profile.CloudDesktopRegion = null;
        profile.Points[0].ClickCount = 0;
        var path = System.IO.Path.Combine(directory.Path, "invalid-point.oclick");

        TestAssert.Throws<InvalidDataException>(
            () => service.Save(profile, path),
            "非法点击次数不应写入 v4 文件");
        TestAssert.True(!File.Exists(path), "验证失败时不应生成目标文件");
    }

    private static void RejectsInvalidCloudRelativeCoordinate()
    {
        using var directory = new TemporaryDirectory();
        var service = new ProfileService(directory.Path);
        var profile = CreateCompleteProfile();
        profile.Points[0].RelativeX = 1.2;
        var path = System.IO.Path.Combine(directory.Path, "invalid-cloud.oclick");

        TestAssert.Throws<InvalidDataException>(
            () => service.Save(profile, path),
            "非法云桌面相对坐标不应写入 v4 文件");
        TestAssert.True(!File.Exists(path), "验证失败时不应生成目标文件");
    }

    private static void SavesToExplicitPath()
    {
        using var directory = new TemporaryDirectory();
        var service = new ProfileService(directory.Path);
        var path = System.IO.Path.Combine(directory.Path, "自定义位置", "方案.oclick");

        var savedPath = service.Save(
            new ClickProfile { Name = "另存为方案" },
            path);

        TestAssert.Equal(path, savedPath, "另存为应返回用户选择的路径");
        TestAssert.True(File.Exists(path), "另存为文件应存在");
    }

    private static void SuggestsDistinctPathForImportedCopy()
    {
        using var directory = new TemporaryDirectory();
        var service = new ProfileService(directory.Path);
        var sourcePath = System.IO.Path.Combine(directory.Path, "日报方案.oclick");
        File.WriteAllText(sourcePath, "source", System.Text.Encoding.UTF8);

        var suggestedPath = service.GetAvailableImportCopyPath("日报方案", sourcePath);

        TestAssert.True(
            !ProfileService.PathsEqual(suggestedPath, sourcePath),
            "导入副本的建议路径不能与来源文件相同");
        TestAssert.Equal(
            directory.Path,
            System.IO.Path.GetDirectoryName(suggestedPath)!,
            "导入副本应默认保存到本机方案目录");
        TestAssert.True(
            System.IO.Path.GetFileNameWithoutExtension(suggestedPath)
                .Contains("副本", StringComparison.Ordinal),
            "导入副本的建议文件名应明确标识副本");
    }

    private static void CreatesAndListsLocalProfileDirectory()
    {
        using var parent = new TemporaryDirectory();
        var profilesDirectory = System.IO.Path.Combine(parent.Path, "profiles");
        var service = new ProfileService(profilesDirectory);

        var profiles = service.ListLocalProfiles();

        TestAssert.True(Directory.Exists(profilesDirectory),
            "枚举本机方案时应自动创建默认目录");
        TestAssert.Equal(0, profiles.Count, "新建方案目录应返回空列表");
    }

    private static void ListsOnlyJsonProfilesInNaturalOrder()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(
            System.IO.Path.Combine(directory.Path, "方案10.oclick"),
            "{}",
            System.Text.Encoding.UTF8);
        File.WriteAllText(
            System.IO.Path.Combine(directory.Path, "说明.txt"),
            "ignore",
            System.Text.Encoding.UTF8);
        File.WriteAllText(
            System.IO.Path.Combine(directory.Path, "UPPER.OCLICK"),
            "{}",
            System.Text.Encoding.UTF8);
        File.WriteAllText(
            System.IO.Path.Combine(directory.Path, "方案2.oclick"),
            "{}",
            System.Text.Encoding.UTF8);
        Directory.CreateDirectory(System.IO.Path.Combine(directory.Path, "nested"));
        File.WriteAllText(
            System.IO.Path.Combine(directory.Path, "nested", "嵌套.oclick"),
            "{}",
            System.Text.Encoding.UTF8);
        var service = new ProfileService(directory.Path);

        var profiles = service.ListLocalProfiles();

        TestAssert.Equal(3, profiles.Count, "只应列出顶层 .oclick 方案");
        TestAssert.Equal("UPPER", profiles[0].DisplayName, "英文方案应稳定排序");
        TestAssert.Equal("方案2", profiles[1].DisplayName, "数字文件名应自然排序");
        TestAssert.Equal("方案10", profiles[2].DisplayName, "方案10 应排在方案2之后");
        TestAssert.True(
            profiles.All(profile => System.IO.Path.IsPathFullyQualified(profile.Path)),
            "方案列表应返回规范化完整路径");
    }

    private static void ComparesProfileSnapshots()
    {
        var profile = CreateCompleteProfile();
        var clone = ProfileService.CloneProfile(profile);

        TestAssert.True(
            ProfileService.ProfilesEqual(profile, clone),
            "相同方案快照应判定为未修改");

        clone.Points[0].AfterDelayMs++;

        TestAssert.True(
            !ProfileService.ProfilesEqual(profile, clone),
            "单点参数变化应判定为未保存修改");
    }

    private static void RoundTripsAllProfileFields()
    {
        using var directory = new TemporaryDirectory();
        var service = new ProfileService(directory.Path);
        var profile = new ClickProfile
        {
            FormatVersion = 4,
            Name = "日报录入方案",
            TotalLoops = 12,
            LoopDelayMs = 345,
            DefaultClickIntervalMs = 1234,
            DefaultAfterDelayMs = 5678,
            Points =
            [
                new ClickPoint
                {
                    Id = Guid.Parse("9c7b1379-d293-4e82-8c50-030ea289934f"),
                    Enabled = true,
                    X = -321,
                    Y = 654,
                    ClickCount = 7,
                    ClickIntervalMs = 88,
                    AfterDelayMs = 999,
                    MonitorDeviceName = "\\\\.\\DISPLAY2",
                    MonitorBounds = new ScreenBounds(-1920, 0, 1920, 1080),
                    CapturedDpi = 144
                }
            ]
        };

        var savedPath = service.Save(profile);
        var result = service.Load(savedPath);

        TestAssert.True(result.Success, $"配置应成功加载：{result.ErrorMessage}");
        TestAssert.Equal("日报录入方案", result.Profile!.Name, "中文方案名应保持不变");
        TestAssert.Equal(12, result.Profile.TotalLoops, "总循环次数应保持不变");
        TestAssert.Equal(1234, result.Profile.DefaultClickIntervalMs, "全局点击间隔应保持不变");
        TestAssert.Equal(5678, result.Profile.DefaultAfterDelayMs, "全局点后等待应保持不变");
        TestAssert.Equal(-321, result.Profile.Points[0].X, "负坐标应保持不变");
        TestAssert.Equal((uint)144, result.Profile.Points[0].CapturedDpi, "DPI 应保持不变");
        TestAssert.True(File.Exists(savedPath), "保存后配置文件应存在");
    }

    private static void ReturnsControlledErrorForMalformedJson()
    {
        using var directory = new TemporaryDirectory();
        var service = new ProfileService(directory.Path);
        var path = System.IO.Path.Combine(directory.Path, "损坏.oclick");
        File.WriteAllText(path, "{not-json", System.Text.Encoding.UTF8);

        var result = service.Load(path);

        TestAssert.True(!result.Success, "损坏 JSON 应返回失败结果");
        TestAssert.True(result.Profile is null, "损坏 JSON 不应返回配置对象");
        TestAssert.True(!string.IsNullOrWhiteSpace(result.ErrorMessage), "失败结果应包含错误信息");
    }

    private static void RejectsNullPointWithControlledError()
    {
        using var directory = new TemporaryDirectory();
        var service = new ProfileService(directory.Path);
        var path = System.IO.Path.Combine(directory.Path, "空点位.oclick");
        File.WriteAllText(
            path,
            """
            {
              "FormatVersion": 4,
              "Name": "空点位方案",
              "TotalLoops": 1,
              "LoopDelayMs": 0,
              "DefaultClickIntervalMs": 1000,
              "DefaultAfterDelayMs": 500,
              "CoordinateMode": 0,
              "Points": [null]
            }
            """,
            System.Text.Encoding.UTF8);

        var result = service.Import(path);

        TestAssert.True(!result.Success, "空点位应返回受控失败结果");
        TestAssert.True(
            (result.ErrorMessage ?? string.Empty).Contains("点位 1", StringComparison.Ordinal),
            "错误应指出空点位所在序号");
    }

    private static ClickProfile CreateCompleteProfile()
    {
        return new ClickProfile
        {
            FormatVersion = 4,
            Name = "云桌面日报方案",
            TotalLoops = 8,
            LoopDelayMs = 1200,
            DefaultClickIntervalMs = 150,
            DefaultAfterDelayMs = 700,
            CoordinateMode = CoordinateMode.CloudDesktopRegion,
            CloudDesktopRegion = new CloudDesktopRegion
            {
                X = 100,
                Y = 80,
                Width = 1200,
                Height = 800,
                MonitorDeviceName = "\\\\.\\DISPLAY1",
                CapturedDpi = 96
            },
            ScreenStability = new ScreenStabilitySettings
            {
                Enabled = true,
                TimeoutMs = 15000
            },
            Points =
            [
                new ClickPoint
                {
                    Enabled = true,
                    X = 400,
                    Y = 300,
                    RelativeX = 0.25,
                    RelativeY = 0.275,
                    ClickCount = 2,
                    ClickIntervalMs = 150,
                    AfterDelayMs = 700,
                    MonitorDeviceName = "\\\\.\\DISPLAY1",
                    MonitorBounds = new ScreenBounds(0, 0, 1920, 1080),
                    CapturedDpi = 96
                }
            ]
        };
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
