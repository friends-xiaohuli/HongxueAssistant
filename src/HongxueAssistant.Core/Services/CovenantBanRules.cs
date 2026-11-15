// Modified for ASP Assistant Arknights on 2026-11-11; see NOTICE.
using HongxueAssistant.Core.Models;

namespace HongxueAssistant.Core.Services;

/// <summary>
/// Which operators a run's covenant bans remove from the pool. Used by both the ban-screen analysis
/// (干员页的本局不可用) and the planner, so the two always agree.
/// </summary>
public static class CovenantBanRules
{
    /// <summary>
    /// All core covenants banned makes an operator unavailable, regardless of additional covenants.
    /// Operators without a core covenant are unavailable only when all their additional covenants are banned.
    /// </summary>
    public static bool IsExcluded(Operator op, IReadOnlySet<string> bannedCovenants)
        => IsExcluded(op.CoreCovenants, op.AdditionalCovenants, bannedCovenants);

    public static bool IsExcluded(IEnumerable<string> core, IEnumerable<string> additional, IReadOnlySet<string> bannedCovenants)
    {
        if (bannedCovenants.Count == 0) return false;
        var relevant = core.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.Ordinal).ToList();
        if (relevant.Count == 0)
            relevant = additional.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.Ordinal).ToList();
        return relevant.Count > 0 && relevant.All(bannedCovenants.Contains);
    }
}
