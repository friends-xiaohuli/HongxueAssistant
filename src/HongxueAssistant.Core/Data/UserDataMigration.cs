namespace HongxueAssistant.Core.Data;

/// <summary>Compatibility entry point; all profile preparation follows the shared verified migration.</summary>
public static class UserDataMigration
{
    public static string Resolve(string? overrideDirectory, string appDataRoot)
        => UserSettingsDirectory.Prepare(appDataRoot, string.IsNullOrWhiteSpace(overrideDirectory) ? null : overrideDirectory);
}
