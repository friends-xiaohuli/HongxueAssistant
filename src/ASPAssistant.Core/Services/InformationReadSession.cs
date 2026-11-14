// Modified for ASP Assistant Arknights on 2026-11-12; see NOTICE.
using System.Text.Json;

namespace ASPAssistant.Core.Services;

public sealed record InformationPool(string Name, IReadOnlyList<string> Core, IReadOnlyList<string> Additional);

/// <summary>Collects a bounded same-source scroll pass. Hidden entries are never assumed enabled.</summary>
public sealed class InformationReadSession
{
    private readonly IReadOnlyList<InformationPool> _pools;
    private BanCovenantReadResult? _top;
    private InformationPool? _pool;
    private readonly Dictionary<string, CovenantBanState> _additional = new(StringComparer.Ordinal);
    private long _source;
    private string? _enemy;
    private DateTime _expires;
    public bool IsPending => _top is not null && DateTime.UtcNow < _expires;

    public InformationReadSession(IReadOnlyList<InformationPool>? pools = null)
        => _pools = pools ?? LoadPools();

    private static IReadOnlyList<InformationPool> LoadPools()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "data", "information_pools.json");
        if (!File.Exists(path)) throw new FileNotFoundException("缺少本局盟约池数据，不能安全判断识别完整性。", path);
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        return json.RootElement.GetProperty("pools").EnumerateArray().Select(p => new InformationPool(
            p.GetProperty("name").GetString()!,
            p.GetProperty("core").EnumerateArray().Select(n => n.GetString()!).ToArray(),
            p.GetProperty("additional").EnumerateArray().Select(n => n.GetString()!).ToArray())).ToList();
    }

    public void Reset() { _top = null; _pool = null; _additional.Clear(); _enemy = null; }

    public bool TryComplete(BanCovenantReadResult read, long source, string? enemy,
        out BanCovenantReadResult complete, out string reason)
    {
        complete = read;
        if (!IsPending || source != _source || (_enemy is not null && enemy is not null && _enemy != enemy)) Reset();
        var coreValid = read.SawCoreHeader && !read.HasIncompleteCoreRows
            && read.Core.Count >= 6 && read.UnnamedCore.Count == 0
            && (read.CoreSlots == 0 || read.CoreSlots == read.Core.Count)
            && read.Core.Select(c => c.Name).Distinct().Count() == read.Core.Count;
        var pool = coreValid ? _pools.FirstOrDefault(p => p.Core.ToHashSet().SetEquals(read.Core.Select(c => c.Name))) : null;
        if (coreValid && pool is null)
        {
            Reset();
            reason = "检测到未登记或不完整的盟约池，本次仅供预览，未更新 Ban；请核对模式和条目，已有结果保留。";
            return false;
        }
        if (pool is not null)
        {
            // A changed selected group must not reuse a scroll pass from the old selection.
            if (_top is not null && (!_top.Core.Where(c => c.IsBanned).Select(c => c.Name).ToHashSet()
                    .SetEquals(read.Core.Where(c => c.IsBanned).Select(c => c.Name)) || _pool != pool
                    || _top.SelectsCoreGroup != read.SelectsCoreGroup)) Reset();
            _top = read; _pool = pool; _source = source; _enemy = enemy;
            _expires = DateTime.UtcNow.AddSeconds(45);
        }
        if (_top is null || _pool is null)
        {
            reason = $"尚未取得完整核心盟约（核心 {read.Core.Count}、附加 {read.Additional.Count}），请先回到本局信息顶部录入；已有结果保留。";
            return false;
        }
        if (read.Additional.Any(c => !_pool.Additional.Contains(c.Name)))
        {
            Reset();
            reason = "附加盟约与当前模式池不一致，请回顶部重新录入，未替换现有 Ban。";
            return false;
        }
        if (read.Additional.Select(c => c.Name).Distinct().Count() != read.Additional.Count)
        {
            reason = "附加盟约名称重复，本次仅供预览，未更新 Ban；请保持名称完整可见后重试。";
            return false;
        }
        // Keep only named observations; a clipped row may contain unnamed slots.
        // The known pool, rather than this frame, decides when the session is complete.
        if (read.SawAdditionalHeader
            && (read.AdditionalSlots == 0 || read.AdditionalSlots == read.Additional.Count + read.UnnamedAdditional.Count))
        {
            foreach (var item in read.Additional.Where(c => _pool.Additional.Contains(c.Name)))
                _additional[item.Name] = item;
        }
        if (!_pool.Additional.ToHashSet().SetEquals(_additional.Keys))
        {
            reason = $"盟约尚未读全（核心 {_top.Core.Count}/{_pool.Core.Count}、附加 {_additional.Count}/{_pool.Additional.Count}），请手动向下滚动左侧盟约面板并再次录入；不会替换现有 Ban。";
            return false;
        }
        if (!coreValid)
        {
            reason = "附加盟约已补齐，请向上滚回核心盟约确认当前选择，再次录入；尚未替换现有 Ban。";
            return false;
        }
        complete = _top with { Additional = _additional.Values.ToList(), AdditionalSlots = _additional.Count,
            UnnamedAdditional = [], SawAdditionalHeader = true,
            HasIncompleteCoreRows = false, HasIncompleteAdditionalRows = false, NeedsScroll = false };
        Reset();
        reason = "";
        return true;
    }
}
