// Modified for HongxueAssistant on 2026-11-16; see NOTICE.
using System.Security.Cryptography;
using System.Text.Json;

namespace HongxueAssistant.Core.Data;

public static class UserSettingsDirectory
{
    public const string FolderName = "HongxueAssistant";

    private static readonly string[] SettingsFiles =
    [
        "appearance.json", "assistant-hotkey.json", "automatic-purchases.json", "board-overlay.json", "custom-operators.json",
        "deploy-pool.json", "game-connection.json", "game-theme.json", "game-theme-auto.json",
        "light-theme.json", "multiple-lineups.json", "recognition-hotkey.json",
        "side-panel-position.json", "skipped_version.txt", "terminal-level.json", "tracking.json"
    ];

    public static string Prepare(string appDataDirectory, string? overrideDirectory)
    {
        if (overrideDirectory is not null) return Path.GetFullPath(overrideDirectory);

        var settingsDir = Path.Combine(Path.GetFullPath(appDataDirectory), FolderName);
        using var mutex = new Mutex(false, @"Local\HongxueAssistant.SettingsMigration");
        try { mutex.WaitOne(); }
        catch (AbandonedMutexException)
        {
            // A crashed migration leaves the original profile intact; start a fresh verified copy.
            AppLogger.Warn("Settings", "上次配置迁移意外中断，重新迁移并校验。");
        }

        try
        {
            var existing = Directory.Exists(settingsDir);
            if (existing && (new DirectoryInfo(settingsDir).Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("新配置目录是链接，请先将实际配置复制到 HongxueAssistant。");

            var legacy = new DirectoryInfo(Path.Combine(Path.GetFullPath(appDataDirectory), "ASPAssistant"));
            FileSystemInfo[] entries;
            try { entries = legacy.GetFileSystemInfos(); }
            catch (DirectoryNotFoundException)
            {
                Directory.CreateDirectory(settingsDir);
                return settingsDir;
            }
            if ((legacy.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("旧配置目录是链接，请先将实际配置复制到 HongxueAssistant。");

            var lineupsDirectory = Path.Combine(settingsDir, "lineups");
            var knownIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(lineupsDirectory))
            {
                if ((new DirectoryInfo(lineupsDirectory).Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("新阵容目录是链接，配置合并已停止。");
                var orderDirectory = new DirectoryInfo(Path.Combine(lineupsDirectory, "order"));
                if (orderDirectory.Exists && (orderDirectory.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("新阵容顺序目录是链接，配置合并已停止。");
                foreach (var file in new DirectoryInfo(lineupsDirectory).GetFiles("*.json"))
                {
                    if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("阵容文件是链接，配置合并已停止。");
                    knownIds.Add(ReadLineupId(file));
                }
            }
            var deleted = LineupStore.ReadDeletedIds(lineupsDirectory);

            var staging = Path.Combine(Path.GetFullPath(appDataDirectory), $"{FolderName}-migration-{Guid.NewGuid():N}");
            Directory.CreateDirectory(staging);
            var activated = false;
            try
            {
                var pending = new Queue<FileSystemInfo>(entries.Where(entry => entry is DirectoryInfo
                    ? entry.Name.Equals("lineups", StringComparison.OrdinalIgnoreCase)
                      || entry.Name.Equals("share-links", StringComparison.OrdinalIgnoreCase)
                    : SettingsFiles.Any(name => entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                        || entry.Name.StartsWith(name + ".", StringComparison.OrdinalIgnoreCase)
                           && entry.Name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
                      || entry.Name.StartsWith("tracking.corrupt-", StringComparison.OrdinalIgnoreCase)
                         && entry.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase));
                var copiedFiles = 0;
                while (pending.TryDequeue(out var entry))
                {
                    if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("旧配置含有文件或目录链接，迁移已停止，旧配置保持不变。");
                    var relative = Path.GetRelativePath(legacy.FullName, entry.FullName);
                    if (entry is DirectoryInfo directory)
                    {
                        Directory.CreateDirectory(Path.Combine(staging, relative));
                        foreach (var child in directory.GetFileSystemInfos().OrderBy(child => child.Name, StringComparer.OrdinalIgnoreCase))
                            pending.Enqueue(child);
                        continue;
                    }

                    if (relative.Equals(Path.Combine("lineups", "order", LineupStore.DeletedIdsFileName), StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (Path.GetDirectoryName(relative)!.Equals("lineups", StringComparison.OrdinalIgnoreCase)
                        && Path.GetExtension(entry.Name).Equals(".json", StringComparison.OrdinalIgnoreCase))
                    {
                        var id = ReadLineupId((FileInfo)entry);
                        if (deleted.Contains(id) || !knownIds.Add(id)) continue;
                        relative = Path.GetRelativePath(settingsDir, LineupStore.PathForId(lineupsDirectory, id));
                    }
                    var target = Path.Combine(settingsDir, relative);
                    for (var parent = Directory.GetParent(target); parent is not null
                         && (parent.FullName == settingsDir || parent.FullName.StartsWith(settingsDir + Path.DirectorySeparatorChar)); parent = parent.Parent)
                    {
                        if (parent.Exists && (parent.Attributes & FileAttributes.ReparsePoint) != 0)
                            throw new IOException("新配置的目标路径包含目录链接，配置合并已停止。");
                    }
                    if (File.Exists(target)) continue; // Existing values, including explicit empty tracking, win.
                    var destination = Path.Combine(staging, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

                    using var source = File.OpenRead(entry.FullName);
                    using (var output = File.Create(destination)) source.CopyTo(output);
                    source.Position = 0;
                    using var copied = File.OpenRead(destination);
                    if (!SHA256.HashData(source).AsSpan().SequenceEqual(SHA256.HashData(copied)))
                        throw new IOException("配置复制校验失败，迁移已停止，旧配置保持不变。");
                    copiedFiles++;
                }

                if (existing)
                {
                    // All source reads and hash checks finish before any missing file becomes visible.
                    foreach (var file in Directory.GetFiles(staging, "*", SearchOption.AllDirectories))
                    {
                        var target = Path.Combine(settingsDir, Path.GetRelativePath(staging, file));
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        File.Move(file, target); // A value created meanwhile must never be overwritten.
                    }
                    Directory.Delete(staging, recursive: true);
                }
                else Directory.Move(staging, settingsDir);
                activated = true;
                AppLogger.Info("Settings", $"启动配置合并完成，已校验并补入 {copiedFiles} 个缺失文件，旧目录只读保留。");
                return settingsDir;
            }
            finally
            {
                if (!activated) Directory.Delete(staging, recursive: true);
            }
        }
        finally { mutex.ReleaseMutex(); }
    }

    private static string ReadLineupId(FileInfo file)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file.FullName));
            if (document.RootElement.ValueKind == JsonValueKind.Object)
                foreach (var property in document.RootElement.EnumerateObject())
                    if (property.Name.Equals("id", StringComparison.OrdinalIgnoreCase)
                        && property.Value.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(property.Value.GetString()))
                        return property.Value.GetString()!;
        }
        catch (JsonException ex)
        {
            AppLogger.Warn("Settings", $"无法解析阵容ID，按文件名保留原文件：{file.Name}，{ex.Message}");
        }
        return Path.GetFileNameWithoutExtension(file.Name);
    }
}
