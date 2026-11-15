using System.Text.Json;
using HongxueAssistant.Core.Models;
using HongxueAssistant.Core.Services;

namespace HongxueAssistant.Core.Data;

/// <summary>
/// 阵容文件持久化：每个阵容一个 &lt;id&gt;.json 文件，存于
/// %AppData%\HongxueAssistant\lineups\，避免单文件并发风险。
/// </summary>
public class LineupStore
{
    internal const string DeletedIdsFileName = "deleted-lineups.json";
    private readonly string _dir;
    private readonly LineupEquipmentService _equipmentRules;
    private readonly SemaphoreSlim _orderLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public LineupStore(string lineupsDir, LineupEquipmentService equipmentRules)
    {
        _dir = lineupsDir;
        _equipmentRules = equipmentRules;
    }

    public async Task<List<Lineup>> LoadAllAsync()
    {
        if (!Directory.Exists(_dir))
            return [];

        var files = Directory.EnumerateFiles(_dir, "*.json");
        var deleted = ReadDeletedIds(_dir);
        var result = new Dictionary<string, Lineup>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            Lineup? lineup;
            try
            {
                var json = await File.ReadAllTextAsync(file);
                lineup = JsonSerializer.Deserialize<Lineup>(json, JsonOptions);
            }
            catch (JsonException ex)
            {
                AppLogger.Warn("LineupStore", $"Failed to load {file}: {ex.Message}");
                continue;
            }
            if (lineup is null) continue;
            if (string.IsNullOrEmpty(lineup.Id)) lineup.Id = Path.GetFileNameWithoutExtension(file);
            if (deleted.Contains(lineup.Id)) continue;
            if (_equipmentRules.Upgrade(lineup))
            {
                File.Copy(file, file + $".pre-equipment-{Guid.NewGuid():N}.bak");
                await WriteAsync(file, lineup);
            }
            if (!result.TryGetValue(lineup.Id, out var previous) || lineup.UpdatedAt > previous.UpdatedAt)
                result[lineup.Id] = lineup;
        }
        List<string> order;
        try
        {
            order = JsonSerializer.Deserialize<List<string>>(
                await File.ReadAllTextAsync(Path.Combine(_dir, "order", "lineups.json")))
                ?? throw new JsonException("阵容顺序不能为空");
        }
        catch (IOException ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // Existing profiles have no manual order until the first move or new lineup.
            return [.. result.Values.OrderByDescending(l => l.UpdatedAt)];
        }
        var ranks = order.Distinct(StringComparer.OrdinalIgnoreCase).Select((id, index) => (id, index))
            .ToDictionary(pair => pair.id, pair => pair.index, StringComparer.OrdinalIgnoreCase);
        return [.. result.Values.OrderBy(l => ranks.TryGetValue(l.Id, out var index) ? index : int.MaxValue)
            .ThenByDescending(l => l.UpdatedAt)];
    }

    public async Task SaveOrderAsync(IEnumerable<string> ids)
    {
        var json = JsonSerializer.Serialize(ids, JsonOptions);
        await _orderLock.WaitAsync();
        var directory = Path.GetFullPath(Path.Combine(_dir, "order"));
        var path = Path.Combine(directory, "lineups.json");
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(directory);
            try
            {
                await File.WriteAllTextAsync(temporary, json);
                try { File.Copy(path, path + ".bak", true); }
                catch (FileNotFoundException) { /* First saved order has no previous version. */ }
                File.Move(temporary, path, true);
            }
            finally { File.Delete(temporary); }
        }
        finally { _orderLock.Release(); }
    }

    public async Task SaveAsync(Lineup lineup)
    {
        Directory.CreateDirectory(_dir);
        if (string.IsNullOrEmpty(lineup.Id))
            lineup.Id = Guid.NewGuid().ToString("N");
        lineup.UpdatedAt = DateTime.UtcNow;

        var path = PathForId(_dir, lineup.Id);
        await _orderLock.WaitAsync();
        try
        {
            await WriteAsync(path, lineup);
            var deleted = ReadDeletedIds(_dir);
            if (deleted.Remove(lineup.Id)) await WriteDeletedIdsAsync(deleted);
        }
        finally { _orderLock.Release(); }
    }

    private static async Task WriteAsync(string path, Lineup lineup)
    {
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(lineup, JsonOptions));
            File.Move(temporary, path, true);
        }
        finally { File.Delete(temporary); }
    }

    internal static string PathForId(string directory, string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidDataException("阵容 ID 不能包含目录路径或无效文件名字符");
        return Path.GetFullPath(Path.Combine(directory, $"{id}.json"));
    }

    internal static HashSet<string> ReadDeletedIds(string directory)
    {
        string json;
        try { json = File.ReadAllText(Path.Combine(directory, "order", DeletedIdsFileName)); }
        catch (IOException ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }
        var ids = JsonSerializer.Deserialize<List<string>>(json)
            ?? throw new JsonException("阵容删除记录不能为空");
        foreach (var id in ids) PathForId(directory, id);
        return ids.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private async Task WriteDeletedIdsAsync(HashSet<string> ids)
    {
        var directory = Path.Combine(_dir, "order");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, DeletedIdsFileName);
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(ids.Order(StringComparer.OrdinalIgnoreCase), JsonOptions));
            try { File.Copy(path, path + ".bak", true); }
            catch (FileNotFoundException) { /* First deletion has no earlier record. */ }
            File.Move(temporary, path, true);
        }
        finally { File.Delete(temporary); }
    }

    public async Task DeleteAsync(string id)
    {
        var path = PathForId(_dir, id);
        await _orderLock.WaitAsync();
        try
        {
            Directory.CreateDirectory(_dir);
            try { File.Copy(path, path + ".deleted.bak", true); }
            catch (FileNotFoundException) { /* A repeated deletion must still suppress the legacy copy. */ }
            var deleted = ReadDeletedIds(_dir);
            if (deleted.Add(id)) await WriteDeletedIdsAsync(deleted);
            File.Delete(path);
        }
        finally { _orderLock.Release(); }
    }
}
