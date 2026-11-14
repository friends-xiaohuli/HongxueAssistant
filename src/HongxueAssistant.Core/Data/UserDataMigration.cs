namespace HongxueAssistant.Core.Data;

/// <summary>Copies the previous app data once; an existing new profile and the legacy profile are never overwritten.</summary>
public static class UserDataMigration
{
    public static string Resolve(string? overrideDirectory, string appDataRoot)
    {
        if (!string.IsNullOrWhiteSpace(overrideDirectory)) return overrideDirectory;
        var target = Path.GetFullPath(Path.Combine(appDataRoot, "HongxueAssistant"));
        var legacy = Path.GetFullPath(Path.Combine(appDataRoot, "ASPAssistant"));
        if (Directory.Exists(target) || !Directory.Exists(legacy)) return target;
        var stage = target + ".migrating-" + Guid.NewGuid().ToString("N");
        try
        {
            Copy(legacy, stage);
            Directory.Move(stage, target);
            return target;
        }
        finally
        {
            if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true);
        }
    }

    private static void Copy(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var path in Directory.EnumerateFileSystemEntries(source))
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("旧配置包含链接，迁移已停止，原配置保留。");
            var target = Path.Combine(destination, Path.GetFileName(path));
            if (Directory.Exists(path)) Copy(path, target);
            else File.Copy(path, target, overwrite: false);
        }
    }
}
