using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using OrderedClicker.Core;
using OrderedClicker.Models;

namespace OrderedClicker.Services;

public sealed record LocalProfileEntry(string DisplayName, string Path)
{
    public override string ToString() => DisplayName;
}

public sealed class ProfileService
{
    private static readonly ConcurrentDictionary<string, object> SaveLocks =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public ProfileService(string? profilesDirectory = null)
    {
        ProfilesDirectory = profilesDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OrderedClicker",
            "profiles");
    }

    public ProfileService(AppDataPaths paths)
        : this(paths?.Profiles)
    {
    }

    public string ProfilesDirectory { get; }

    public IReadOnlyList<LocalProfileEntry> ListLocalProfiles()
    {
        Directory.CreateDirectory(ProfilesDirectory);
        foreach (var orphan in Directory.EnumerateFiles(
                     ProfilesDirectory, "*.previous", SearchOption.TopDirectoryOnly).ToArray())
        {
            var marker = orphan.LastIndexOf(".save-", StringComparison.OrdinalIgnoreCase);
            if (marker >= 0
                && Guid.TryParseExact(
                    orphan.AsSpan(marker + 6, orphan.Length - marker - 6 - ".previous".Length),
                    "N", out _))
            {
                TryRecoverPreviousVersion(orphan[..marker]);
            }
        }

        return Directory
            .EnumerateFiles(ProfilesDirectory, "*", SearchOption.TopDirectoryOnly)
            .Where(path => string.Equals(
                Path.GetExtension(path),
                ".oclick",
                StringComparison.OrdinalIgnoreCase))
            .Select(path => new LocalProfileEntry(
                Path.GetFileNameWithoutExtension(path),
                Path.GetFullPath(path)))
            .OrderBy(entry => entry.DisplayName, NaturalFileNameComparer.Instance)
            .ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public string Save(ClickProfile profile)
    {
        Directory.CreateDirectory(ProfilesDirectory);
        var destination = GetAvailableProfilePath(profile.Name, profile.ProfileId);
        return Save(profile, destination);
    }

    public string Save(ClickProfile profile, string destination)
    {
        return Save(profile, destination, new ProfileSaveOptions());
    }

    public string Save(
        ClickProfile profile,
        string destination,
        ProfileSaveOptions options)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(destination))
        {
            throw new ArgumentException("保存路径不能为空。", nameof(destination));
        }

        var directory = Path.GetDirectoryName(destination);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("保存路径无效。");
        }

        Directory.CreateDirectory(directory);
        TryRecoverPreviousVersion(destination);
        if (File.Exists(destination) && !options.AllowOverwrite)
        {
            throw new IOException("目标方案已存在，必须明确确认后才能覆盖。");
        }

        var validationError = ValidateProfile(profile);
        if (validationError is not null)
        {
            throw new InvalidDataException(validationError);
        }

        if (options.RenewIdentity)
        {
            profile.ProfileId = Guid.NewGuid();
            profile.CreatedAtUtc = DateTime.UtcNow;
        }

        profile.FormatVersion = 4;
        profile.UpdatedAtUtc = DateTime.UtcNow;
        var temporary = $"{destination}.{Guid.NewGuid():N}.tmp";
        var json = JsonSerializer.Serialize(profile, JsonOptions);

        try
        {
            File.WriteAllText(temporary, json, new UTF8Encoding(false));
            CommitTemporaryFile(temporary, destination, options);
            return destination;
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    public string Export(ClickProfile profile, string destination)
    {
        return Save(
            profile,
            destination,
            new ProfileSaveOptions(
                CreateBackup: File.Exists(destination),
                AllowOverwrite: true));
    }

    public string GetAvailableProfilePath(string profileName, Guid profileId)
    {
        Directory.CreateDirectory(ProfilesDirectory);
        var baseName = SanitizeFileName(profileName);
        var candidate = Path.Combine(ProfilesDirectory, $"{baseName}.oclick");
        var suffix = 2;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(ProfilesDirectory, $"{baseName}-{suffix}.oclick");
            suffix++;
        }

        return candidate;
    }

    public string GetAvailableImportCopyPath(string profileName, string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("导入来源路径不能为空。", nameof(sourcePath));
        }

        var baseName = $"{SanitizeFileName(profileName)}-副本";
        var candidate = Path.Combine(ProfilesDirectory, $"{baseName}.oclick");
        var suffix = 2;
        while (PathsEqual(candidate, sourcePath) || File.Exists(candidate))
        {
            candidate = Path.Combine(ProfilesDirectory, $"{baseName}-{suffix}.oclick");
            suffix++;
        }

        return candidate;
    }

    public static bool PathsEqual(string first, string second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
        {
            return false;
        }

        var normalizedFirst = Path.TrimEndingDirectorySeparator(Path.GetFullPath(first));
        var normalizedSecond = Path.TrimEndingDirectorySeparator(Path.GetFullPath(second));
        return string.Equals(
            normalizedFirst,
            normalizedSecond,
            StringComparison.OrdinalIgnoreCase);
    }

    public static ClickProfile CloneProfile(ClickProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var json = JsonSerializer.Serialize(profile, JsonOptions);
        return JsonSerializer.Deserialize<ClickProfile>(json, JsonOptions)
               ?? throw new InvalidOperationException("无法创建方案快照。");
    }

    public static bool ProfilesEqual(ClickProfile first, ClickProfile second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        return string.Equals(
            JsonSerializer.Serialize(first, JsonOptions),
            JsonSerializer.Serialize(second, JsonOptions),
            StringComparison.Ordinal);
    }

    public static string ComputeFileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public ProfileLoadResult Import(string path)
    {
        var result = Load(path);
        if (!result.Success)
        {
            return result;
        }

        var profile = result.Profile!;
        var validationError = ValidateProfile(profile);
        return validationError is null
            ? result
            : ProfileLoadResult.Failed(validationError);
    }

    public ProfileLoadResult Load(string path)
    {
        try
        {
            TryRecoverPreviousVersion(path);
            byte[] bytes;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var content = new MemoryStream())
            {
                stream.CopyTo(content);
                bytes = content.ToArray();
            }

            using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, true);
            var json = reader.ReadToEnd();
            var profile = JsonSerializer.Deserialize<ClickProfile>(json, JsonOptions);
            if (profile is null)
            {
                return ProfileLoadResult.Failed("配置文件内容为空。");
            }

            profile.Points ??= [];
            profile.ScreenStability ??= new ScreenStabilitySettings();
            return ProfileLoadResult.Loaded(
                profile, Convert.ToHexString(SHA256.HashData(bytes)));
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or NotSupportedException)
        {
            return ProfileLoadResult.Failed($"读取配置失败：{exception.Message}");
        }
    }

    public static string? ValidateProfile(ClickProfile profile)
    {
        if (profile.FormatVersion != 4)
        {
            return $"不支持方案文件版本 {profile.FormatVersion}，请使用“迁移旧方案”转换为版本 4。";
        }

        if (profile.TotalLoops is < 1 or > 100000)
        {
            return "总循环次数必须在 1 到 100000 之间。";
        }

        if (profile.LoopDelayMs is < 0 or > 600000)
        {
            return "轮间等待必须在 0 到 600000 毫秒之间。";
        }

        if (profile.DefaultClickIntervalMs is < 10 or > 600000)
        {
            return "默认点击间隔必须在 10 到 600000 毫秒之间。";
        }

        if (profile.DefaultAfterDelayMs is < 0 or > 600000)
        {
            return "默认点后等待必须在 0 到 600000 毫秒之间。";
        }

        if (!Enum.IsDefined(profile.CoordinateMode))
        {
            return "方案文件包含不支持的坐标模式。";
        }

        if (profile.CoordinateMode == CoordinateMode.CloudDesktopRegion)
        {
            try
            {
                CloudDesktopCoordinateService.ValidateRegion(
                    profile.CloudDesktopRegion
                    ?? throw new ArgumentOutOfRangeException(nameof(profile.CloudDesktopRegion)));
            }
            catch (ArgumentOutOfRangeException)
            {
                return "云桌面区域无效，宽度和高度不能小于 100 像素。";
            }
        }

        for (var index = 0; index < profile.Points.Count; index++)
        {
            var point = profile.Points[index];
            var displayIndex = index + 1;
            if (point is null)
            {
                return $"点位 {displayIndex} 内容为空。";
            }

            if (point.ClickCount is < 1 or > 100000)
            {
                return $"点位 {displayIndex} 的点击次数必须在 1 到 100000 之间。";
            }

            if (point.ClickIntervalMs is < 10 or > 600000)
            {
                return $"点位 {displayIndex} 的点击间隔必须在 10 到 600000 毫秒之间。";
            }

            if (point.AfterDelayMs is < 0 or > 600000)
            {
                return $"点位 {displayIndex} 的点后等待必须在 0 到 600000 毫秒之间。";
            }

            if (profile.CoordinateMode != CoordinateMode.CloudDesktopRegion)
            {
                continue;
            }

            if (point.RelativeX is null || point.RelativeY is null)
            {
                return $"点位 {displayIndex} 缺少云桌面相对坐标。";
            }

            try
            {
                CloudDesktopCoordinateService.ValidateRelative(
                    point.RelativeX.Value,
                    point.RelativeY.Value);
            }
            catch (ArgumentOutOfRangeException)
            {
                return $"点位 {displayIndex} 的云桌面相对坐标必须在 0 到 1 之间。";
            }
        }

        return null;
    }

    private static void EnsureExpectedFileVersion(
        string destination,
        string? expectedExistingSha256)
    {
        if (string.IsNullOrWhiteSpace(expectedExistingSha256))
        {
            return;
        }

        try
        {
            if (!File.Exists(destination)
                || !string.Equals(
                    ComputeFileSha256(destination),
                    expectedExistingSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ProfileConflictException(
                    "方案文件已被其他程序或窗口修改，请另存为新文件。");
            }
        }
        catch (ProfileConflictException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            throw new ProfileConflictException(
                "方案文件正在被其他程序写入，请稍后重试或另存为新文件。",
                exception);
        }
    }

    private static void CommitTemporaryFile(
        string temporary,
        string destination,
        ProfileSaveOptions options)
    {
        var normalizedDestination = Path.GetFullPath(destination);
        var saveLock = SaveLocks.GetOrAdd(normalizedDestination, static _ => new object());
        lock (saveLock)
        {
            if (string.IsNullOrWhiteSpace(options.ExpectedExistingSha256))
            {
                if (File.Exists(destination) && options.CreateBackup)
                {
                    File.Copy(destination, destination + ".bak", true);
                }

                File.Move(temporary, destination, options.AllowOverwrite);
                return;
            }

            CommitExpectedFileVersion(
                temporary,
                destination,
                options.ExpectedExistingSha256,
                options.CreateBackup);
        }
    }

    private static void CommitExpectedFileVersion(
        string temporary,
        string destination,
        string expectedExistingSha256,
        bool createBackup)
    {
        var capturedPath =
            $"{destination}.save-{Guid.NewGuid():N}.previous";
        try
        {
            // Deny in-place writers while allowing the atomic name replacement.
            using (var lease = OpenCapturedVersion(destination))
            {
                var expectedHash = Convert.ToHexString(SHA256.HashData(lease));
                if (!string.Equals(expectedHash, expectedExistingSha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new ProfileConflictException(
                        "方案文件已被其他程序或窗口修改，请另存为新文件。");
                }

                lease.Position = 0;
                using (var previous = new FileStream(capturedPath, FileMode.CreateNew,
                           FileAccess.Write, FileShare.Read))
                {
                    lease.CopyTo(previous);
                    previous.Flush(true);
                }

                EnsureExpectedFileVersion(destination, expectedExistingSha256);
                AtomicReplace(temporary, destination);
            }

            if (createBackup)
            {
                File.Move(capturedPath, destination + ".bak", true);
            }
            else
            {
                TryDeleteFile(capturedPath);
            }
        }
        catch (ProfileConflictException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            TryRecoverPreviousVersion(destination);
            throw new ProfileConflictException(
                "方案保存期间文件状态发生变化，请另存为新文件。",
                exception);
        }
    }

    private static void AtomicReplace(string temporary, string destination)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.Move(temporary, destination, true);
            return;
        }

        // ReplaceFileW briefly removes the destination on Windows. A single
        // POSIX rename keeps the name present and preserves existing read handles.
        using var source = CreateFileForRename(
            GetWindowsExtendedPath(temporary), 0x00010000,
            FileShare.ReadWrite | FileShare.Delete, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (source.IsInvalid)
        {
            throw new IOException("无法打开待提交的方案文件。",
                new Win32Exception(Marshal.GetLastWin32Error()));
        }

        var name = Encoding.Unicode.GetBytes(GetWindowsExtendedPath(destination));
        var nameOffset = IntPtr.Size == 8 ? 20 : 12;
        // The Win32 wrapper also reads the name as a terminated string.
        var size = nameOffset + name.Length + sizeof(char);
        var information = Marshal.AllocHGlobal(size);
        try
        {
            // FILE_RENAME_INFO: flags, aligned root handle, byte length, UTF-16 name.
            Marshal.Copy(new byte[size], 0, information, size);
            const int replaceIfExists = 1;
            const int posixSemantics = 2;
            Marshal.WriteInt32(information, replaceIfExists | posixSemantics);
            Marshal.WriteInt32(information, nameOffset - sizeof(int), name.Length);
            Marshal.Copy(name, 0, IntPtr.Add(information, nameOffset), name.Length);
            const int fileRenameInfoEx = 22;
            if (!SetFileInformationByHandle(source, fileRenameInfoEx,
                    information, (uint)size))
            {
                var error = Marshal.GetLastWin32Error();
                if (error is 1 or 50 or 87)
                {
                    // Older filesystems may not support extended rename flags.
                    source.Dispose();
                    File.Move(temporary, destination, true);
                    return;
                }

                throw new IOException("无法原子替换方案文件。",
                    new Win32Exception(error));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(information);
        }
    }

    private static string GetWindowsExtendedPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (fullPath.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            return fullPath;
        }

        return fullPath.StartsWith(@"\\", StringComparison.Ordinal)
            ? @"\\?\UNC\" + fullPath[2..]
            : @"\\?\" + fullPath;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileForRename(
        string path, uint access, FileShare share, IntPtr securityAttributes,
        uint creationDisposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(
        SafeFileHandle file, int informationClass, IntPtr information, uint size);

    private static FileStream OpenCapturedVersion(string capturedPath)
    {
        try
        {
            return new FileStream(
                capturedPath,
                FileMode.Open,
                FileAccess.Read,
                OperatingSystem.IsWindows() ? FileShare.Read | FileShare.Delete : FileShare.None);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            throw new ProfileConflictException(
                "方案文件仍被其他程序写入，请稍后重试或另存为新文件。",
                exception);
        }
    }

    private static void TryRecoverPreviousVersion(string destination)
    {
        var fullPath = Path.GetFullPath(destination);
        var saveLock = SaveLocks.GetOrAdd(fullPath, static _ => new object());
        lock (saveLock)
        {
            if (File.Exists(fullPath))
            {
                return;
            }

            var directory = Path.GetDirectoryName(fullPath)!;
            if (!Directory.Exists(directory))
            {
                return;
            }

            var prefix = Path.GetFileName(fullPath) + ".save-";
            try
            {
                var candidates = Directory.EnumerateFiles(directory, "*.previous")
                    .Where(path => Path.GetFileName(path).StartsWith(prefix,
                        StringComparison.OrdinalIgnoreCase))
                    .Where(path => Guid.TryParseExact(
                        Path.GetFileName(path).AsSpan(prefix.Length,
                            Path.GetFileName(path).Length - prefix.Length - ".previous".Length),
                        "N", out _))
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .ThenBy(path => path, StringComparer.Ordinal)
                    .ToArray();
                foreach (var candidate in candidates)
                {
                    try
                    {
                        using var lease = OpenCapturedVersion(candidate);
                        using var reader = new StreamReader(lease, Encoding.UTF8, true,
                            bufferSize: 1024, leaveOpen: true);
                        var profile = JsonSerializer.Deserialize<ClickProfile>(
                            reader.ReadToEnd(), JsonOptions);
                        if (profile is null || profile.Points is null
                            || ValidateProfile(profile) is not null)
                        {
                            continue;
                        }

                        File.Move(candidate, fullPath, false);
                        return;
                    }
                    catch (Exception exception) when (
                        exception is IOException or UnauthorizedAccessException
                        or JsonException or NotSupportedException)
                    {
                        // Invalid or busy candidates remain available for manual recovery.
                    }
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                // Recovery is best effort and must never replace a newer formal file.
            }
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // The committed destination is valid; a stale recovery copy is harmless.
        }
    }

    private static string SanitizeFileName(string name)
    {
        var value = string.IsNullOrWhiteSpace(name) ? "默认方案" : name.Trim();
        foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalidCharacter, '_');
        }

        return value;
    }

    private sealed class NaturalFileNameComparer : IComparer<string>
    {
        public static NaturalFileNameComparer Instance { get; } = new();

        public int Compare(string? first, string? second)
        {
            if (ReferenceEquals(first, second))
            {
                return 0;
            }

            if (first is null)
            {
                return -1;
            }

            if (second is null)
            {
                return 1;
            }

            var firstIndex = 0;
            var secondIndex = 0;
            while (firstIndex < first.Length && secondIndex < second.Length)
            {
                var firstIsDigit = char.IsDigit(first[firstIndex]);
                var secondIsDigit = char.IsDigit(second[secondIndex]);
                if (firstIsDigit && secondIsDigit)
                {
                    var firstStart = firstIndex;
                    var secondStart = secondIndex;
                    while (firstIndex < first.Length && char.IsDigit(first[firstIndex]))
                    {
                        firstIndex++;
                    }

                    while (secondIndex < second.Length && char.IsDigit(second[secondIndex]))
                    {
                        secondIndex++;
                    }

                    var firstNumber = first.AsSpan(firstStart, firstIndex - firstStart)
                        .TrimStart('0');
                    var secondNumber = second.AsSpan(secondStart, secondIndex - secondStart)
                        .TrimStart('0');
                    var lengthComparison = firstNumber.Length.CompareTo(secondNumber.Length);
                    if (lengthComparison != 0)
                    {
                        return lengthComparison;
                    }

                    var numberComparison = firstNumber.CompareTo(
                        secondNumber,
                        StringComparison.Ordinal);
                    if (numberComparison != 0)
                    {
                        return numberComparison;
                    }

                    var digitCountComparison = (firstIndex - firstStart)
                        .CompareTo(secondIndex - secondStart);
                    if (digitCountComparison != 0)
                    {
                        return digitCountComparison;
                    }

                    continue;
                }

                var characterComparison = char.ToUpperInvariant(first[firstIndex])
                    .CompareTo(char.ToUpperInvariant(second[secondIndex]));
                if (characterComparison != 0)
                {
                    return characterComparison;
                }

                firstIndex++;
                secondIndex++;
            }

            return first.Length.CompareTo(second.Length);
        }
    }
}
