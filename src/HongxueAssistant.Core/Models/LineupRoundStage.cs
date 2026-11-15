using System.Text.Json.Serialization;

namespace HongxueAssistant.Core.Models;

/// <summary>An editable lineup for the author's actual round interval, separate from the final formation.</summary>
public sealed class LineupRoundStage
{
    [JsonPropertyName("startRound")]
    public int StartRound { get; set; }

    [JsonPropertyName("endRound")]
    public int EndRound { get; set; }

    /// <summary>Index into Lineup.Guide.Tabs; recommended equipment and source choices remain unassigned there.</summary>
    [JsonPropertyName("sourceTabIndex")]
    public int SourceTabIndex { get; set; } = -1;

    [JsonPropertyName("slots")]
    public List<LineupSlot> Slots { get; set; } = [];

    internal const string IncompleteSourcePlaceholder = "（来源条目不完整，请移除）";

    [JsonIgnore]
    public string DisplayTitle => StartRound == EndRound ? $"第 {StartRound} 回合" : $"第 {StartRound}–{EndRound} 回合";

    public LineupRoundStage Clone() => new()
    {
        StartRound = StartRound, EndRound = EndRound, SourceTabIndex = SourceTabIndex,
        Slots = (Slots ?? []).Select(slot => slot is null
            ? new LineupSlot { OperatorName = IncompleteSourcePlaceholder } : CloneSlot(slot)).ToList()
    };

    /// <summary>Migration for guides imported before editable round stages were supported.</summary>
    public static List<LineupRoundStage> FromGuide(LineupGuide? guide)
    {
        if (guide is null) return [];
        var snapshot = guide.Clone();
        return snapshot.Tabs.Select((tab, index) => (tab, index)).Where(pair => pair.tab.TabType == 2)
            .Select(pair => new LineupRoundStage
            {
                StartRound = pair.tab.StartRound, EndRound = pair.tab.EndRound, SourceTabIndex = pair.index,
                Slots = pair.tab.Operators.Select(op => new LineupSlot
                {
                    OperatorName = op.MergeLevel is not (0 or 1)
                        ? $"{op.OperatorName}（不支持的精锐等级 {op.MergeLevel}，请替换或移除）"
                        : op.OperatorName.Length > 0 ? op.OperatorName : op.CharId,
                    IsElite = op.MergeLevel == 1
                }).Concat(pair.tab.HasInvalidEntries
                    ? new[] { new LineupSlot { OperatorName = IncompleteSourcePlaceholder } } : Array.Empty<LineupSlot>()).ToList()
            }).ToList();
    }

    internal static LineupSlot CloneSlot(LineupSlot slot) => new()
    {
        OperatorName = slot.OperatorName ?? "", TransformedCovenant = slot.TransformedCovenant,
        IsElite = slot.IsElite, Equipments = [.. (slot.Equipments ?? [])],
        CoreEquipments = [.. (slot.CoreEquipments ?? [])], Tags = [.. (slot.Tags ?? [])]
    };
}
