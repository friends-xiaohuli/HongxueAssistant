using System.Security.Cryptography;
using System.Text.Json;
using HongxueAssistant.Core.Data;
using HongxueAssistant.Core.Models;
using FluentAssertions;

namespace HongxueAssistant.Tests.Data;

public class UserSettingsDirectoryTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("Hongxue-ProfileTests-").FullName;
    private string Legacy => Path.Combine(_root, "ASPAssistant");
    private string Current => Path.Combine(_root, "HongxueAssistant");

    [Fact]
    public void NewInstall_UsesDedicatedDirectoryWithoutCreatingLegacyProfile()
    {
        UserSettingsDirectory.Prepare(_root, null).Should().Be(Current);
        Directory.Exists(Current).Should().BeTrue();
        Directory.Exists(Legacy).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Migration_PreservesCompleteLineupsOrderTrackingAndSettings(bool emptyDestination)
    {
        Directory.CreateDirectory(Legacy);
        if (emptyDestination) Directory.CreateDirectory(Current);
        var store = new LineupStore(Path.Combine(Legacy, "lineups"), EquipmentTestData.Rules);
        var first = new Lineup
        {
            Id = "first", Name = "完整阵容", SourceUrl = "https://example.invalid/lineup",
            Slots = [new() { OperatorName = "角峰", IsElite = true, Equipments = ["炎国短刀"],
                CoreEquipments = ["炎国短刀"], CandidateEquipments = [new() { Name = "不屈弹射器", IsPriority = true }],
                Tags = [LineupTag.阵容核心, LineupTag.输出] }],
            EarlySlots = [new() { OperatorName = "芬" }], MidSlots = [new() { OperatorName = "讯使" }],
            SupportSlots = [new() { OperatorName = "桃金娘" }],
            Strategies = [new() { StrategyId = "band_pith", Tags = [StrategyTag.优选] }],
            TargetCovenants = [new() { Name = "谢拉格", Tier = 2 }]
        };
        var second = new Lineup { Id = "second", Name = "另一阵容", Slots = [new() { OperatorName = "能天使" }] };
        await store.SaveAsync(first);
        await store.SaveAsync(second);
        await store.SaveOrderAsync([second.Id, first.Id]);
        File.Copy(Path.Combine(Legacy, "lineups", "first.json"), Path.Combine(Legacy, "lineups", "first.json.pre-equipment-example.bak"));
        var settings = new SettingsManager(Legacy);
        await settings.SaveTrackingStateAsync(new TrackingState
        {
            Entries = [new() { Name = "角峰", Type = TrackingType.Operator, IsElite = true,
                Equipments = ["炎国短刀"], CandidateEquipments = [new() { Name = "不屈弹射器", IsPriority = true }],
                Origins = [new() { LineupId = first.Id, Equipments = ["炎国短刀"] }] }],
            Lineups = [new(first.Id, first.Name, false)]
        });
        await settings.SaveGameShopThemeAsync(GameShopTheme.Terminal);
        await settings.SaveAutomaticGameThemeAsync(false);
        await settings.SaveAssistantHotkeyAsync(new(6, 0x41));
        await settings.SaveRecognitionHotkeyAsync(null);
        await settings.SaveCustomOperatorSelectionAsync(["缄默德克萨斯"]);
        await settings.SaveSkippedVersionAsync("v1.8.0");
        await File.WriteAllTextAsync(Path.Combine(Legacy, "tracking.json.pre-sources-example.bak"), "backup bytes");
        await File.WriteAllTextAsync(Path.Combine(Legacy, "tracking.corrupt-example.json"), "preserved diagnostic");
        Directory.CreateDirectory(Path.Combine(Legacy, "share-links"));
        await File.WriteAllTextAsync(Path.Combine(Legacy, "share-links", "cached.json"), "cached link bytes");
        var before = Snapshot(Legacy);

        UserSettingsDirectory.Prepare(_root, null).Should().Be(Current);
        Snapshot(Current).Should().BeEquivalentTo(before);
        Snapshot(Legacy).Should().BeEquivalentTo(before);
        var migrated = await new LineupStore(Path.Combine(Current, "lineups"), EquipmentTestData.Rules).LoadAllAsync();
        migrated.Select(l => l.Id).Should().Equal(second.Id, first.Id);
        migrated.Single(l => l.Id == first.Id).Should().BeEquivalentTo(first);
        var loaded = await new SettingsManager(Current).LoadTrackingStateAsync(EquipmentTestData.Rules);
        loaded.Entries.Single().Origins.Single().LineupId.Should().Be(first.Id);
        loaded.Entries.Single().CandidateEquipments.Single().IsPriority.Should().BeTrue();
        loaded.Lineups.Single().Highlighted.Should().BeFalse();
        (await new SettingsManager(Current).LoadRecognitionHotkeyAsync()).Should().BeNull();
        (await new SettingsManager(Current).LoadGameShopThemeAsync()).Should().Be(GameShopTheme.Terminal);
        (await new SettingsManager(Current).LoadAutomaticGameThemeAsync()).Should().BeFalse();
        (await new SettingsManager(Current).LoadAssistantHotkeyAsync()).Should().Be(new AssistantHotkey(6, 0x41));
        (await new SettingsManager(Current).LoadCustomOperatorSelectionAsync()).Should().Equal("缄默德克萨斯");
        (await new SettingsManager(Current).LoadSkippedVersionAsync()).Should().Be("v1.8.0");

        migrated[1].Name = "迁移后编辑";
        await new LineupStore(Path.Combine(Current, "lineups"), EquipmentTestData.Rules).SaveAsync(migrated[1]);
        (await new LineupStore(Path.Combine(Current, "lineups"), EquipmentTestData.Rules).LoadAllAsync())
            .Single(l => l.Id == first.Id).Name.Should().Be("迁移后编辑");
        await new SettingsManager(Current).SaveAutomaticGameThemeAsync(true);
        (await settings.LoadAutomaticGameThemeAsync()).Should().BeFalse();
        Snapshot(Legacy).Should().BeEquivalentTo(before);
    }

    [Fact]
    public void Migration_ExcludesOriginalOnlySettingsLogsAndUnfinishedRootWrites()
    {
        Directory.CreateDirectory(Path.Combine(Legacy, "logs"));
        foreach (var name in new[] { "ui.json", "bob-voice.json", "game-target.json", "marker-offset.json", "lineups.json", "tracking.json.write.tmp" })
            File.WriteAllText(Path.Combine(Legacy, name), "original-only");
        File.WriteAllText(Path.Combine(Legacy, "logs", "aspa.log"), "original log");
        File.WriteAllText(Path.Combine(Legacy, "appearance.json"), "project setting");

        UserSettingsDirectory.Prepare(_root, null);

        Directory.GetFileSystemEntries(Current).Select(Path.GetFileName).Should().Equal("appearance.json");
        Directory.GetFiles(Legacy).Should().HaveCount(7);
        File.Exists(Path.Combine(Legacy, "logs", "aspa.log")).Should().BeTrue();
    }

    [Fact]
    public void WindowsFolderNames_AreMatchedWithoutCaseSensitivity()
    {
        Directory.CreateDirectory(Path.Combine(Legacy, "LINEUPS", "ORDER"));
        File.WriteAllText(Path.Combine(Legacy, "TRACKING.JSON"), "[]");
        File.WriteAllText(Path.Combine(Legacy, "LINEUPS", "saved.json"), "saved lineup");
        File.WriteAllText(Path.Combine(Legacy, "LINEUPS", "ORDER", "lineups.json"), "saved order");
        var before = Snapshot(Legacy);

        UserSettingsDirectory.Prepare(_root, null);

        Snapshot(Current).Should().BeEquivalentTo(before);
    }

    [Fact]
    public void ExistingProfile_PreservesNewValuesAndFillsMissingSettings()
    {
        Directory.CreateDirectory(Legacy);
        Directory.CreateDirectory(Current);
        File.WriteAllText(Path.Combine(Legacy, "tracking.json"), "old");
        File.WriteAllText(Path.Combine(Legacy, "appearance.json"), "old-only");
        File.WriteAllText(Path.Combine(Current, "tracking.json"), "new");
        var before = Snapshot(Current);

        UserSettingsDirectory.Prepare(_root, null).Should().Be(Current);

        File.ReadAllText(Path.Combine(Current, "tracking.json")).Should().Be("new");
        Snapshot(Current)["tracking.json"].Should().Be(before["tracking.json"]);
        File.ReadAllText(Path.Combine(Current, "appearance.json")).Should().Be("old-only");
    }

    [Fact]
    public void LaterStartup_FillsMissingSettingsWithoutReopeningExistingLegacyValues()
    {
        Directory.CreateDirectory(Legacy);
        File.WriteAllText(Path.Combine(Legacy, "tracking.json"), "copied");
        File.WriteAllText(Path.Combine(Legacy, "appearance.json"), "copied appearance");
        UserSettingsDirectory.Prepare(_root, null);
        File.Delete(Path.Combine(Current, "tracking.json"));
        using var locked = new FileStream(Path.Combine(Legacy, "appearance.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        UserSettingsDirectory.Prepare(_root, null).Should().Be(Current);

        File.ReadAllText(Path.Combine(Current, "tracking.json")).Should().Be("copied");
    }

    [Fact]
    public async Task PartialProfiles_MergeByIdPreferCurrentAndPreserveNewOrderAndEmptyTracking()
    {
        var legacy = new LineupStore(Path.Combine(Legacy, "lineups"), EquipmentTestData.Rules);
        var current = new LineupStore(Path.Combine(Current, "lineups"), EquipmentTestData.Rules);
        await legacy.SaveAsync(new() { Id = "shared", Name = "旧版本" });
        await legacy.SaveAsync(new() { Id = "old-only", Name = "旧目录独有" });
        File.Move(Path.Combine(Legacy, "lineups", "shared.json"), Path.Combine(Legacy, "lineups", "alias.json"));
        File.Copy(Path.Combine(Legacy, "lineups", "old-only.json"), Path.Combine(Legacy, "lineups", "duplicate.json"));
        await current.SaveAsync(new() { Id = "shared", Name = "新版本" });
        await current.SaveAsync(new() { Id = "new-only", Name = "新增阵容" });
        await current.SaveOrderAsync(["new-only", "shared"]);
        File.WriteAllText(Path.Combine(Legacy, "tracking.json"), "[{\"name\":\"角峰\",\"type\":0}]");
        File.WriteAllText(Path.Combine(Current, "tracking.json"), "[]");
        var original = Snapshot(Legacy);
        var preferred = File.ReadAllBytes(Path.Combine(Current, "lineups", "shared.json"));

        UserSettingsDirectory.Prepare(_root, null);

        var merged = await current.LoadAllAsync();
        merged.Select(l => l.Id).Should().Equal("new-only", "shared", "old-only");
        merged.Single(l => l.Id == "shared").Name.Should().Be("新版本");
        File.ReadAllBytes(Path.Combine(Current, "lineups", "shared.json")).Should().Equal(preferred);
        (await new SettingsManager(Current).LoadTrackingStateAsync(EquipmentTestData.Rules)).Entries.Should().BeEmpty();
        File.Exists(Path.Combine(Current, "lineups", "duplicate.json")).Should().BeFalse();
        File.Exists(Path.Combine(Current, "lineups", "alias.json")).Should().BeFalse();
        Snapshot(Legacy).Should().BeEquivalentTo(original);
    }

    [Fact]
    public async Task DeletionMarkerPreventsLegacyResurrectionAndExplicitSaveRestoresTheId()
    {
        var legacy = new LineupStore(Path.Combine(Legacy, "lineups"), EquipmentTestData.Rules);
        var current = new LineupStore(Path.Combine(Current, "lineups"), EquipmentTestData.Rules);
        await legacy.SaveAsync(new() { Id = "saved", Name = "旧目录阵容" });
        File.Move(Path.Combine(Legacy, "lineups", "saved.json"), Path.Combine(Legacy, "lineups", "other-filename.json"));
        var original = Snapshot(Legacy);
        UserSettingsDirectory.Prepare(_root, null);
        await current.DeleteAsync("saved");
        await current.DeleteAsync("saved");

        UserSettingsDirectory.Prepare(_root, null);
        (await current.LoadAllAsync()).Should().BeEmpty();
        File.Exists(Path.Combine(Current, "lineups", "saved.json.deleted.bak")).Should().BeTrue();
        Snapshot(Legacy).Should().BeEquivalentTo(original);

        await current.SaveAsync(new() { Id = "saved", Name = "明确恢复的新版本" });
        UserSettingsDirectory.Prepare(_root, null);
        (await current.LoadAllAsync()).Should().ContainSingle().Which.Name.Should().Be("明确恢复的新版本");
        var deleted = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(Path.Combine(Current, "lineups", "order", "deleted-lineups.json")));
        deleted.Should().BeEmpty();
        Snapshot(Legacy).Should().BeEquivalentTo(original);
    }

    [Fact]
    public async Task LaterStartupImportsNewLegacyIdsButDoesNotReplaceCurrentEdits()
    {
        var legacy = new LineupStore(Path.Combine(Legacy, "lineups"), EquipmentTestData.Rules);
        var current = new LineupStore(Path.Combine(Current, "lineups"), EquipmentTestData.Rules);
        await legacy.SaveAsync(new() { Id = "shared", Name = "旧" });
        UserSettingsDirectory.Prepare(_root, null);
        await current.SaveAsync(new() { Id = "shared", Name = "在新目录编辑" });
        await legacy.SaveAsync(new() { Id = "shared", Name = "旧目录后来编辑" });
        await legacy.SaveAsync(new() { Id = "late", Name = "旧目录后来新增" });
        var original = Snapshot(Legacy);

        UserSettingsDirectory.Prepare(_root, null);

        var merged = await current.LoadAllAsync();
        merged.Should().HaveCount(2);
        merged.Single(l => l.Id == "shared").Name.Should().Be("在新目录编辑");
        merged.Single(l => l.Id == "late").Name.Should().Be("旧目录后来新增");
        Snapshot(Legacy).Should().BeEquivalentTo(original);
    }

    [Fact]
    public void FailedMergeLeavesTheExistingProfileUnchangedAndCanRetry()
    {
        Directory.CreateDirectory(Legacy);
        Directory.CreateDirectory(Current);
        File.WriteAllText(Path.Combine(Current, "appearance.json"), "new appearance");
        File.WriteAllText(Path.Combine(Legacy, "game-theme.json"), "0");
        File.WriteAllText(Path.Combine(Legacy, "tracking.json"), "[]");
        var original = Snapshot(Current);
        using (var locked = new FileStream(Path.Combine(Legacy, "tracking.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Action merge = () => UserSettingsDirectory.Prepare(_root, null);
            merge.Should().Throw<IOException>();
            Snapshot(Current).Should().BeEquivalentTo(original);
            Directory.GetDirectories(_root, "HongxueAssistant-migration-*").Should().BeEmpty();
        }
        UserSettingsDirectory.Prepare(_root, null);
        File.ReadAllText(Path.Combine(Current, "appearance.json")).Should().Be("new appearance");
        File.ReadAllText(Path.Combine(Current, "tracking.json")).Should().Be("[]");
    }

    [Fact]
    public async Task CorruptDeletionIndexStopsTheMergeInsteadOfRestoringLegacyData()
    {
        await new LineupStore(Path.Combine(Legacy, "lineups"), EquipmentTestData.Rules).SaveAsync(new() { Id = "deleted" });
        Directory.CreateDirectory(Path.Combine(Current, "lineups", "order"));
        File.WriteAllText(Path.Combine(Current, "lineups", "order", "deleted-lineups.json"), "{broken");
        var original = Snapshot(Current);
        Action merge = () => UserSettingsDirectory.Prepare(_root, null);
        merge.Should().Throw<JsonException>();
        Snapshot(Current).Should().BeEquivalentTo(original);
        File.Exists(Path.Combine(Current, "lineups", "deleted.json")).Should().BeFalse();
    }

    [Fact]
    public async Task Utf16LegacyAliasStillUsesItsIdAndCannotReplaceTheCurrentVersion()
    {
        Directory.CreateDirectory(Path.Combine(Legacy, "lineups"));
        File.WriteAllText(Path.Combine(Legacy, "lineups", "alias.json"),
            JsonSerializer.Serialize(new Lineup { Id = "shared", Name = "旧目录后来改动", UpdatedAt = new DateTime(2026, 1, 1) }),
            System.Text.Encoding.Unicode);
        var store = new LineupStore(Path.Combine(Current, "lineups"), EquipmentTestData.Rules);
        await store.SaveAsync(new() { Id = "shared", Name = "新目录优先" });
        var original = Snapshot(Legacy);
        UserSettingsDirectory.Prepare(_root, null);
        (await store.LoadAllAsync()).Should().ContainSingle().Which.Name.Should().Be("新目录优先");
        File.Exists(Path.Combine(Current, "lineups", "alias.json")).Should().BeFalse();
        Snapshot(Legacy).Should().BeEquivalentTo(original);
    }

    [Fact]
    public async Task ClearedTerminalLevelCannotBeRestoredFromTheLegacyProfile()
    {
        var legacy = new SettingsManager(Legacy);
        var current = new SettingsManager(Current);
        await legacy.SaveTerminalLevelAsync(6);
        await current.SaveTerminalLevelAsync(0);
        var original = Snapshot(Legacy);
        UserSettingsDirectory.Prepare(_root, null);
        (await current.LoadTerminalLevelAsync()).Should().BeNull();
        (await legacy.LoadTerminalLevelAsync()).Should().Be(6);
        Snapshot(Legacy).Should().BeEquivalentTo(original);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedCopy_DoesNotPublishPartialProfileAndCanRetry(bool nestedLineup)
    {
        Directory.CreateDirectory(Path.Combine(Legacy, "lineups"));
        File.WriteAllText(Path.Combine(Legacy, "appearance.json"), "copied before failure");
        var file = nestedLineup ? Path.Combine(Legacy, "lineups", "saved.json") : Path.Combine(Legacy, "tracking.json");
        File.WriteAllText(file, "locked source");
        var before = Snapshot(Legacy);
        using (var locked = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Action migrate = () => UserSettingsDirectory.Prepare(_root, null);
            migrate.Should().Throw<IOException>();
            Directory.Exists(Current).Should().BeFalse();
            Directory.GetDirectories(_root, "HongxueAssistant-migration-*").Should().BeEmpty();
        }
        Snapshot(Legacy).Should().BeEquivalentTo(before);

        UserSettingsDirectory.Prepare(_root, null);

        Snapshot(Current).Should().BeEquivalentTo(before);
    }

    [Fact]
    public void DestinationFile_IsPreservedAndMigrationFails()
    {
        Directory.CreateDirectory(Legacy);
        File.WriteAllText(Path.Combine(Legacy, "tracking.json"), "old");
        File.WriteAllText(Current, "destination file");

        Action migrate = () => UserSettingsDirectory.Prepare(_root, null);

        migrate.Should().Throw<IOException>();
        File.ReadAllText(Current).Should().Be("destination file");
        Directory.GetDirectories(_root, "HongxueAssistant-migration-*").Should().BeEmpty();
    }

    [Fact]
    public void ExplicitOverride_RemainsIsolatedAndDoesNotMigrateDefaultProfile()
    {
        Directory.CreateDirectory(Legacy);
        File.WriteAllText(Path.Combine(Legacy, "tracking.json"), "not a test fixture");
        var isolated = Path.Combine(_root, "isolated");
        using var locked = new FileStream(Path.Combine(Legacy, "tracking.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        UserSettingsDirectory.Prepare(_root, isolated).Should().Be(isolated);

        Directory.Exists(Current).Should().BeFalse();
        Directory.Exists(isolated).Should().BeFalse();
    }

    [Fact]
    public async Task ConcurrentStarts_PublishOneCompleteProfile()
    {
        Directory.CreateDirectory(Path.Combine(Legacy, "lineups", "order"));
        File.WriteAllText(Path.Combine(Legacy, "tracking.json"), "tracking bytes");
        File.WriteAllText(Path.Combine(Legacy, "lineups", "order", "lineups.json"), "order bytes");
        var expected = Snapshot(Legacy);

        var paths = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => UserSettingsDirectory.Prepare(_root, null))));

        paths.Should().OnlyContain(path => path == Current);
        Snapshot(Current).Should().BeEquivalentTo(expected);
        Directory.GetDirectories(_root, "HongxueAssistant-migration-*").Should().BeEmpty();
    }

    private static Dictionary<string, string> Snapshot(string directory) => Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(directory, path), path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));

    public void Dispose()
    {
        Path.GetDirectoryName(Path.GetFullPath(_root)).Should().Be(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar));
        Path.GetFileName(_root).Should().StartWith("Hongxue-ProfileTests-");
        Directory.Delete(_root, recursive: true);
    }
}
