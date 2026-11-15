using System.Net.Http;
using System.Text.Json;
using HongxueAssistant.Core.Models;
using HongxueAssistant.Core.ViewModels;

namespace HongxueAssistant.Core.Services;

public sealed record SklandGuideImportResult(Lineup Lineup, string Notice);

public sealed class SklandGuideImporter(HttpClient http, IReadOnlyList<Operator> operators, IReadOnlyList<Strategy> strategies)
{
    public static bool IsOfficialGuideUrl(string input) => Uri.TryCreate(input.Trim(), UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && uri.IdnHost.Equals("game.skland.com", StringComparison.OrdinalIgnoreCase)
        && uri.IsDefaultPort && uri.UserInfo.Length == 0
        && uri.AbsolutePath.TrimEnd('/') == "/arknights/stronghold-guide/detail";

    public async Task<SklandGuideImportResult> ImportAsync(string input, CancellationToken ct = default)
    {
        if (!IsOfficialGuideUrl(input))
            throw new ArgumentException("请粘贴森空岛卫戍协议的具体攻略详情链接。");
        var uri = new Uri(input.Trim());
        var guideId = uri.Query.TrimStart('?').Split('&').Select(part => part.Split('=', 2))
            .Where(part => part.Length == 2 && part[0] == "guideId")
            .Select(part => Uri.UnescapeDataString(part[1])).FirstOrDefault();
        if (string.IsNullOrEmpty(guideId) || !guideId.All(char.IsAsciiDigit))
            throw new ArgumentException("攻略链接缺少有效的 guideId，请复制具体攻略详情链接。");

        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://zonai.skland.com/web/v1/game/garrison/guide/info?guideId={guideId}&hostNeedEditing=true");
        request.Headers.Referrer = new Uri("https://game.skland.com/");
        request.Headers.Add("Origin", "https://game.skland.com");
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.GetProperty("code").GetInt32() != 0)
                throw new InvalidDataException($"官方攻略读取失败：{root.GetProperty("message").GetString()}");
            var data = root.GetProperty("data");
            var item = data.GetProperty("guide");
            if (item.GetProperty("invalid").GetInt32() != 0)
                throw new InvalidDataException("这篇官方攻略已失效，未导入阵容。");
            var guide = item.GetProperty("guide");
            if (guide.GetProperty("guideId").GetString() != guideId)
                throw new InvalidDataException("官方返回的攻略与链接不匹配，未导入阵容。");
            var tabs = guide.GetProperty("tabs").EnumerateArray().ToArray();
            var formations = tabs.Where(tab => tab.GetProperty("tabType").GetInt32() == 1).ToArray();
            if (formations.Length != 1)
                throw new InvalidDataException("当前支持含一个大成阵容的攻略；这篇攻略没有大成阵容或包含多个方案，未导入。");
            var formation = formations[0];
            var chars = formation.GetProperty("recChars").EnumerateArray().ToArray();
            if (chars.Length == 0)
                throw new InvalidDataException("攻略没有推荐干员，未导入阵容。");
            var resource = data.GetProperty("seasonResourceMap").GetProperty(guide.GetProperty("seasonId").GetString()!)
                .GetProperty("versionResourceMap").GetProperty(guide.GetProperty("versionId").GetString()!);
            var lineup = new Lineup
            {
                Name = guide.GetProperty("title").GetString()!,
                SourceUrl = $"https://game.skland.com/arknights/stronghold-guide/detail?header=0&guideId={guideId}",
                Guide = new LineupGuide
                {
                    GuideId = guideId, Title = guide.GetProperty("title").GetString()!,
                    SeasonId = guide.GetProperty("seasonId").GetString()!,
                    VersionId = guide.GetProperty("versionId").GetString()!,
                    StrategyIds = guide.GetProperty("strategyIds").EnumerateArray().Select(id => id.GetString()!).ToList(),
                    Tabs = tabs.Select(tab => ReadGuideTab(tab, resource)).ToList()
                }
            };
            lineup.RoundStages = LineupRoundStage.FromGuide(lineup.Guide);
            var unselectedCustom = new List<string>();
            foreach (var entry in chars)
            {
                var info = resource.GetProperty("chars").GetProperty(entry.GetProperty("charId").GetString()!);
                var name = info.GetProperty("name").GetString()!;
                var op = operators.FirstOrDefault(op => op.Name == name);
                if (op is null)
                    throw new InvalidDataException($"本地尚未收录干员“{name}”，请更新干员数据后导入；未保存部分阵容。");
                if (!op.IsAvailableForSelection)
                    unselectedCustom.Add(name);
                var tier = info.GetProperty("shopLevel").GetInt32();
                if (!op.IsCustom && tier != op.Tier)
                    throw new InvalidDataException($"攻略中的“{name}”与本地等阶不同，请更新数据或选择与当前版本匹配的攻略；未导入阵容。");
                var elite = entry.GetProperty("mergeLevel").GetInt32();
                if (elite is not (0 or 1))
                    throw new InvalidDataException($"干员“{name}”的精锐等级暂不支持，未导入阵容。");
                lineup.Slots.Add(new LineupSlot { OperatorName = name, IsElite = elite == 1 });
            }
            if (lineup.Slots.Select(slot => slot.OperatorName).Distinct(StringComparer.Ordinal).Count() != lineup.Slots.Count)
                throw new InvalidDataException("攻略大成阵容包含重复干员，未导入阵容。");
            if (chars.Length > LineupEditorViewModel.MaxSlots)
            {
                var candidates = lineup.Slots;
                lineup.Slots = [];
                foreach (var slot in candidates)
                    (operators.First(op => op.Name == slot.OperatorName).Tier switch
                    {
                        1 or 2 => lineup.EarlySlots,
                        3 or 4 => lineup.MidSlots,
                        _ => lineup.Slots,
                    }).Add(slot);
            }
            foreach (var entry in guide.GetProperty("strategyIds").EnumerateArray())
            {
                var id = entry.GetString()!;
                if (!strategies.Any(strategy => strategy.Id == id))
                    throw new InvalidDataException($"本地尚未收录攻略策略“{resource.GetProperty("strategies").GetProperty(id).GetProperty("name").GetString()}”，请更新数据后导入。");
                lineup.Strategies.Add(new LineupStrategySlot { StrategyId = id });
            }
            var equips = formation.GetProperty("recItems").GetArrayLength();
            var rounds = tabs.Count(tab => tab.GetProperty("tabType").GetInt32() == 2);
            var people = chars.Length > LineupEditorViewModel.MaxSlots
                ? $"官方列出 {chars.Length} 名候选，已按本数分组并全部保留：1–2本前期 {lineup.EarlySlots.Count} 人、3–4本中期 {lineup.MidSlots.Count} 人、5–6本后期 {lineup.Slots.Count} 人。"
                : $"已读取大成阵容 {lineup.Slots.Count} 人。";
            if (new[] { lineup.EarlySlots, lineup.MidSlots, lineup.Slots }.Any(slots => slots.Count > LineupEditorViewModel.MaxSlots))
                people += $"单个阶段超过 {LineupEditorViewModel.MaxSlots} 人，请在草稿中调整后保存。";
            var customNotice = unselectedCustom.Count > 0
                ? $"尚未选入个人自选：{string.Join("、", unselectedCustom)}。使用前请到干员栏选择，本次不会修改自选名单。"
                : "";
            return new(lineup, $"{people}策略 {lineup.Strategies.Count} 项。{customNotice}已生成 {rounds} 个独立回合阶段，可分别编辑并保存；推荐装备 {equips} 件、回合讲解 {rounds} 段及总体思路已保留。装备未绑定到干员；技能与模组保留攻略原始选择，规划仍使用本地模式数据。检查并保存后才加入我的阵容，不自动追踪。");
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or JsonException)
        {
            throw new InvalidDataException("官方攻略数据格式无法识别，未导入阵容。", ex);
        }
    }

    private static LineupGuideTab ReadGuideTab(JsonElement tab, JsonElement resource)
    {
        var result = new LineupGuideTab
        {
            TabType = tab.GetProperty("tabType").GetInt32(),
            StartRound = ReadInt(tab, "startRound"), EndRound = ReadInt(tab, "endRound"),
            OperationIdea = ReadString(tab, "operationIdea"), SourceData = tab.Clone()
        };
        if (tab.TryGetProperty("recChars", out var chars))
            foreach (var entry in chars.EnumerateArray())
            {
                var id = entry.GetProperty("charId").GetString()!;
                var info = FindResource(resource, "chars", id);
                var skillId = ReadString(entry, "skillId");
                var moduleId = ReadString(entry, "moduleId");
                result.Operators.Add(new LineupGuideOperator
                {
                    CharId = id, OperatorName = ReadString(info, "name", id), ShopLevel = ReadInt(info, "shopLevel"),
                    MergeLevel = ReadInt(entry, "mergeLevel"), SkillId = skillId, ModuleId = moduleId,
                    SkillName = FindChoiceName(info, "skills", "skillId", skillId),
                    ModuleName = FindChoiceName(info, "modules", "moduleId", moduleId)
                });
            }
        if (tab.TryGetProperty("recItems", out var items))
            foreach (var entry in items.EnumerateArray())
            {
                var id = entry.GetProperty("itemId").GetString()!;
                var info = FindResource(resource, "items", id);
                result.Equipment.Add(new LineupGuideEquipment
                {
                    ItemId = id, Name = ReadString(info, "name", id), Description = ReadString(info, "effectDesc")
                });
            }
        if (tab.TryGetProperty("activeBonds", out var active))
            foreach (var (field, isSeason) in new[] { ("seasonBondIds", true), ("regularBondIds", false) })
                if (active.TryGetProperty(field, out var ids))
                    foreach (var entry in ids.EnumerateArray())
                    {
                        var id = entry.GetString()!;
                        result.ActiveBonds.Add(new LineupGuideBond
                        {
                            BondId = id, Name = ReadString(FindResource(resource, "bonds", id), "name", id), IsSeason = isSeason
                        });
                    }
        return result;
    }

    private static JsonElement FindResource(JsonElement resource, string group, string id)
        => resource.TryGetProperty(group, out var entries) && entries.TryGetProperty(id, out var info) ? info : default;

    private static string ReadString(JsonElement element, string property, string fallback = "")
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;

    private static int ReadInt(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
            && value.TryGetInt32(out var number) ? number : 0;

    private static string FindChoiceName(JsonElement info, string choices, string idProperty, string id)
        => info.ValueKind == JsonValueKind.Object && info.TryGetProperty(choices, out var entries)
            ? entries.EnumerateArray().Where(entry => ReadString(entry, idProperty) == id)
                .Select(entry => ReadString(entry, "name")).FirstOrDefault() ?? "" : "";

}
