using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace HongxueAssistant.Core.Models;

/// <summary>Read-only source guide snapshot, independent from the editable lineup phases.</summary>
public sealed class LineupGuide
{
    public string GuideId { get; set; } = "";
    public string Title { get; set; } = "";
    public string SeasonId { get; set; } = "";
    public string VersionId { get; set; } = "";
    public List<string> StrategyIds { get; set; } = [];
    public List<LineupGuideTab> Tabs { get; set; } = [];

    public LineupGuide Clone() => new()
    {
        GuideId = GuideId ?? "", Title = Title ?? "", SeasonId = SeasonId ?? "", VersionId = VersionId ?? "",
        StrategyIds = (StrategyIds ?? []).Where(id => !string.IsNullOrEmpty(id)).ToList(),
        Tabs = (Tabs ?? []).Where(tab => tab is not null).Select(tab => tab.Clone()).ToList()
    };
}

public sealed class LineupGuideTab
{
    public int TabType { get; set; }
    public int StartRound { get; set; }
    public int EndRound { get; set; }
    public string OperationIdea { get; set; } = "";
    public List<LineupGuideOperator> Operators { get; set; } = [];
    public List<LineupGuideEquipment> Equipment { get; set; } = [];
    public List<LineupGuideBond> ActiveBonds { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool HasInvalidEntries { get; set; }

    /// <summary>Preserve source fields unknown to this version without storing the author's account.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? SourceData { get; set; }

    [JsonIgnore]
    public string DisplayTitle => TabType switch
    {
        1 => "大成阵容",
        2 => StartRound == EndRound ? $"第 {StartRound} 回合" : $"第 {StartRound}–{EndRound} 回合",
        _ => $"攻略页 {TabType}"
    };

    public LineupGuideTab Clone() => new()
    {
        TabType = TabType, StartRound = StartRound, EndRound = EndRound, OperationIdea = OperationIdea ?? "",
        HasInvalidEntries = HasInvalidEntries || (Operators?.Any(op => op is null) ?? false)
            || (Equipment?.Any(item => item is null) ?? false) || (ActiveBonds?.Any(bond => bond is null) ?? false),
        Operators = (Operators ?? []).Where(op => op is not null).Select(op => op with
        {
            CharId = op.CharId ?? "", OperatorName = op.OperatorName ?? op.CharId ?? "",
            SkillId = op.SkillId ?? "", SkillName = op.SkillName ?? "",
            ModuleId = op.ModuleId ?? "", ModuleName = op.ModuleName ?? ""
        }).ToList(),
        Equipment = (Equipment ?? []).Where(item => item is not null).Select(item => item with
        {
            ItemId = item.ItemId ?? "", Name = item.Name ?? item.ItemId ?? "", Description = item.Description ?? ""
        }).ToList(),
        ActiveBonds = (ActiveBonds ?? []).Where(bond => bond is not null).Select(bond => bond with
        {
            BondId = bond.BondId ?? "", Name = bond.Name ?? bond.BondId ?? ""
        }).ToList(),
        SourceData = SourceData is { ValueKind: not JsonValueKind.Undefined } data ? data.Clone() : null
    };
}

public sealed record LineupGuideOperator
{
    public string CharId { get; set; } = "";
    public string OperatorName { get; set; } = "";
    public int ShopLevel { get; set; }
    public int MergeLevel { get; set; }
    public string SkillId { get; set; } = "";
    public string SkillName { get; set; } = "";
    public string ModuleId { get; set; } = "";
    public string ModuleName { get; set; } = "";

    [JsonIgnore] public string EliteDisplay => MergeLevel == 1 ? "精锐" : MergeLevel == 0 ? "普通" : $"合成 {MergeLevel}";
    [JsonIgnore] public string SkillDisplay => !string.IsNullOrEmpty(SkillName) ? SkillName : !string.IsNullOrEmpty(SkillId) ? SkillId : "未指定";
    [JsonIgnore] public string ModuleDisplay => !string.IsNullOrEmpty(ModuleName) ? ModuleName : !string.IsNullOrEmpty(ModuleId) ? ModuleId : "未指定";
}

public sealed record LineupGuideEquipment
{
    public string ItemId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    [JsonIgnore] public string DisplayDescription => Regex.Replace(Description ?? "", "<[^>]*>", "");
}

public sealed record LineupGuideBond
{
    public string BondId { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsSeason { get; set; }
}
