using HongxueAssistant.Core.Data;
using FluentAssertions;

namespace HongxueAssistant.Tests.Data;

public class UserDataMigrationTests
{
    [Fact]
    public void CopiesLegacySettingsAndLineupsWithoutChangingOriginalFiles()
    {
        var root = Directory.CreateTempSubdirectory("hongxue-migration-").FullName;
        try
        {
            var old = Path.Combine(root, "ASPAssistant");
            Directory.CreateDirectory(Path.Combine(old, "lineups"));
            File.WriteAllText(Path.Combine(old, "tracking.json"), "tracking");
            File.WriteAllText(Path.Combine(old, "lineups", "saved.json"), "lineup");
            var target = UserDataMigration.Resolve(null, root);
            target.Should().Be(Path.Combine(root, "HongxueAssistant"));
            File.ReadAllText(Path.Combine(target, "tracking.json")).Should().Be("tracking");
            File.ReadAllText(Path.Combine(target, "lineups", "saved.json")).Should().Be("lineup");
            File.ReadAllText(Path.Combine(old, "tracking.json")).Should().Be("tracking");
            File.WriteAllText(Path.Combine(target, "tracking.json"), "new");
            UserDataMigration.Resolve(null, root).Should().Be(target);
            File.ReadAllText(Path.Combine(target, "tracking.json")).Should().Be("new");
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ExplicitSettingsDirectoryDoesNotMigrateAnything()
    {
        var root = Directory.CreateTempSubdirectory("hongxue-override-").FullName;
        try
        {
            UserDataMigration.Resolve("isolated-test-settings", root).Should().Be(Path.GetFullPath("isolated-test-settings"));
            Directory.GetFileSystemEntries(root).Should().BeEmpty();
        }
        finally { Directory.Delete(root, true); }
    }
}
