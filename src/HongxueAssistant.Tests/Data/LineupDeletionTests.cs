using System.Text.Json;
using HongxueAssistant.Core.Data;
using HongxueAssistant.Core.Models;
using FluentAssertions;

namespace HongxueAssistant.Tests.Data;

public class LineupDeletionTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("Hongxue-DeletionTests-").FullName;

    [Theory]
    [InlineData("../outside")]
    [InlineData("folder/name")]
    [InlineData("stream:other")]
    [InlineData("")]
    public async Task DeletionRejectsIdsThatCannotBeFileNames(string id)
    {
        var store = new LineupStore(_root, EquipmentTestData.Rules);
        var delete = () => store.DeleteAsync(id);
        await delete.Should().ThrowAsync<InvalidDataException>();
        Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public async Task LockedLineupIsNotRemovedOrMarkedDeletedWhenItsBackupFails()
    {
        var store = new LineupStore(_root, EquipmentTestData.Rules);
        await store.SaveAsync(new() { Id = "saved", Name = "保持可用" });
        var file = Path.Combine(_root, "saved.json");
        using (var locked = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var delete = () => store.DeleteAsync("saved");
            await delete.Should().ThrowAsync<IOException>();
        }
        (await store.LoadAllAsync()).Should().ContainSingle();
        File.Exists(Path.Combine(_root, "order", "deleted-lineups.json")).Should().BeFalse();
    }

    [Fact]
    public async Task DuplicateCurrentIdsHaveOneVisibleLatestVersion()
    {
        File.WriteAllText(Path.Combine(_root, "older.json"), JsonSerializer.Serialize(new Lineup
            { Id = "same", Name = "旧副本", UpdatedAt = new DateTime(2025, 1, 1) }));
        File.WriteAllText(Path.Combine(_root, "newer.json"), JsonSerializer.Serialize(new Lineup
            { Id = "same", Name = "新副本", UpdatedAt = new DateTime(2026, 1, 1) }));
        await new LineupStore(_root, EquipmentTestData.Rules).SaveOrderAsync(["SAME", "same"]);
        var loaded = await new LineupStore(_root, EquipmentTestData.Rules).LoadAllAsync();
        loaded.Should().ContainSingle().Which.Name.Should().Be("新副本");
        var store = new LineupStore(_root, EquipmentTestData.Rules);
        await store.DeleteAsync("same");
        (await store.LoadAllAsync()).Should().BeEmpty("one deletion must suppress every filename for the same ID");
        await store.SaveAsync(new() { Id = "same", Name = "明确恢复" });
        (await store.LoadAllAsync()).Should().ContainSingle().Which.Name.Should().Be("明确恢复");
    }

    [Fact]
    public async Task ConcurrentSavesAndDeletesKeepTheDeletionIndexConsistent()
    {
        var store = new LineupStore(_root, EquipmentTestData.Rules);
        var ids = Enumerable.Range(0, 20).Select(i => "lineup-" + i).ToList();
        await Task.WhenAll(ids.Select(id => store.SaveAsync(new() { Id = id })));
        await Task.WhenAll(ids.SelectMany(id => new[] { store.DeleteAsync(id), store.SaveAsync(new() { Id = id, Name = "恢复" }) }));
        var deleted = JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(Path.Combine(_root, "order", "deleted-lineups.json")))!;
        foreach (var id in ids)
            deleted.Contains(id).Should().Be(!File.Exists(Path.Combine(_root, id + ".json")));
        Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories).Should().BeEmpty();
    }

    public void Dispose()
    {
        Path.GetDirectoryName(Path.GetFullPath(_root)).Should().Be(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar));
        Path.GetFileName(_root).Should().StartWith("Hongxue-DeletionTests-");
        Directory.Delete(_root, true);
    }
}
