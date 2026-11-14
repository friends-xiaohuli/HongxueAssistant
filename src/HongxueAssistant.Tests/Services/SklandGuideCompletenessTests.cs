using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using HongxueAssistant.Core.Data;
using HongxueAssistant.Core.Models;
using HongxueAssistant.Core.Services;
using HongxueAssistant.Core.ViewModels;
using FluentAssertions;

namespace HongxueAssistant.Tests.Services;

public class SklandGuideCompletenessTests
{
    private const string Url = "https://game.skland.com/arknights/stronghold-guide/detail?header=0&guideId=2121069219205484656";
    private static string Fixture => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "skland-guide-complete.json"));

    private static async Task<(List<Operator>, List<Strategy>)> CatalogAsync()
    {
        var store = new JsonDataStore(Path.Combine(AppContext.BaseDirectory, "data"));
        return (await store.LoadOperatorsAsync(), await store.LoadStrategiesAsync());
    }

    [Fact]
    public async Task EightFinalOperatorsAndAllFourRoundSnapshotsKeepOriginalChoicesAndUnassignedEquipment()
    {
        var (operators, strategies) = await CatalogAsync();
        using var http = new HttpClient(new GuideHandler(Fixture));
        var result = await new SklandGuideImporter(http, operators, strategies).ImportAsync(Url);
        result.Lineup.Slots.Should().HaveCount(8);
        result.Lineup.EarlySlots.Should().BeEmpty();
        result.Lineup.MidSlots.Should().BeEmpty();
        result.Lineup.Slots.Should().OnlyContain(slot => slot.Equipments.Count == 0);
        var guide = result.Lineup.Guide!;
        guide.GuideId.Should().Be("2121069219205484656");
        guide.SeasonId.Should().Be("act3autochess");
        guide.VersionId.Should().Be("V3_1");
        guide.StrategyIds.Should().Equal("band_damaztic");
        guide.Tabs.Select(tab => tab.Operators.Count).Should().Equal(8, 9, 9, 8, 8);
        guide.Tabs.Skip(1).Select(tab => (tab.StartRound, tab.EndRound)).Should().Equal((1, 4), (5, 7), (8, 11), (12, 13));
        guide.Tabs[0].Equipment.Should().HaveCount(7);
        guide.Tabs.Should().OnlyContain(tab => tab.OperationIdea.Contains("\n") && tab.OperationIdea.Contains("不按文字猜测佩戴者"));
        guide.Tabs.Should().OnlyContain(tab => tab.SourceData!.Value.GetProperty("operationIdea").GetString() == tab.OperationIdea);
        var heyak = guide.Tabs[0].Operators.Single(op => op.OperatorName == "霍尔海雅");
        heyak.SkillId.Should().Be("skchr_heyak_1");
        heyak.SkillName.Should().Be("但为求索");
        heyak.ModuleId.Should().Be("uniequip_002_heyak");
        heyak.ModuleName.Should().NotBeEmpty();
        guide.Tabs[0].ActiveBonds.Should().Contain(bond => bond.Name == "莱塔尼亚" && bond.IsSeason);
        result.Notice.Should().Contain("7 件").And.Contain("4 段").And.Contain("已保留").And.NotContain("未导入");
    }

    [Fact]
    public async Task FlowSurvivesPhaseCopyResetEditingStoreAndJsonSharingWithoutMutatingTheImportedDraft()
    {
        var (operators, strategies) = await CatalogAsync();
        using var http = new HttpClient(new GuideHandler(Fixture));
        var imported = (await new SklandGuideImporter(http, operators, strategies).ImportAsync(Url)).Lineup;
        var originalGuide = JsonSerializer.Serialize(imported.Guide);
        var calculator = new CovenantCalculator(operators, [], new Dictionary<string, string>());
        var editor = new LineupEditorViewModel(imported, operators, [], calculator);
        editor.Guide.Should().NotBeSameAs(imported.Guide);
        editor.ApplyGuideTab(2, LineupPhase.中期).Should().BeEmpty();
        editor.FilledCount(LineupPhase.中期).Should().Be(9);
        editor.ToLineup().MidSlots.Select(slot => slot.OperatorName).Should().Equal(imported.Guide!.Tabs[2].Operators.Select(op => op.OperatorName));
        editor.ToLineup().MidSlots.Should().OnlyContain(slot => slot.Equipments.Count == 0);
        imported.MidSlots.Should().BeEmpty();
        JsonSerializer.Serialize(imported.Guide).Should().Be(originalGuide);
        editor.Reset();
        editor.FilledCount(LineupPhase.中期).Should().Be(0);
        JsonSerializer.Serialize(editor.Guide).Should().Be(originalGuide);
        editor.ApplyGuideTab(2, LineupPhase.中期).Should().BeEmpty();
        editor.Name = "编辑后的阵容";
        var saved = editor.ToLineup();
        saved.Guide.Should().NotBeSameAs(editor.Guide);
        JsonSerializer.Serialize(saved.Guide).Should().Be(originalGuide);

        var folder = Path.Combine(Path.GetTempPath(), "skland-complete-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var store = new LineupStore(Path.Combine(folder, "lineups"), calculator.EquipmentRules);
            await store.SaveAsync(saved);
            var reloaded = (await store.LoadAllAsync()).Single();
            JsonSerializer.Serialize(reloaded.Guide).Should().Be(originalGuide);
            var share = new LineupShareService(calculator.EquipmentRules, Path.Combine(folder, "share"), http);
            var shared = await share.ImportAsync(JsonSerializer.Serialize(reloaded));
            JsonSerializer.Serialize(shared.Guide).Should().Be(originalGuide);
            shared.MidSlots.Should().HaveCount(9);
            var tracking = new TrackingViewModel();
            tracking.TrackLineup(shared);
            tracking.TrackedOperators.Select(op => op.Name).Should().Contain("车尔尼");
            tracking.TrackedEquipment.Should().BeEmpty("recommended items have no assigned owner");
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task UnknownTransitionOperatorStaysInSourceAndCannotSilentlyReplaceADraftPhase()
    {
        var (operators, strategies) = await CatalogAsync();
        var fixture = JsonNode.Parse(Fixture)!;
        fixture["data"]!["guide"]!["guide"]!["tabs"]![1]!["recChars"]![0]!["charId"] = "future-character";
        using var http = new HttpClient(new GuideHandler(fixture.ToJsonString()));
        var imported = (await new SklandGuideImporter(http, operators, strategies).ImportAsync(Url)).Lineup;
        imported.Guide!.Tabs[1].Operators[0].CharId.Should().Be("future-character");
        imported.Guide.Tabs[1].Operators[0].OperatorName.Should().Be("future-character");
        var editor = new LineupEditorViewModel(imported, operators, [], new CovenantCalculator(operators, [], new Dictionary<string, string>()));
        editor.ApplyGuideTab(1, LineupPhase.后期).Should().Contain("未收录");
        editor.ToLineup().Slots.Select(slot => slot.OperatorName).Should().Equal(imported.Slots.Select(slot => slot.OperatorName));
    }

    [Theory]
    [InlineData("tier")]
    [InlineData("elite")]
    [InlineData("strategy")]
    public void IncompatibleSnapshotCannotPartiallyReplaceADraft(string mismatch)
    {
        var operators = new[] { new Operator { Name = "甲", Tier = 3, RequiredStrategyId = mismatch == "strategy" ? "required" : "" },
            new Operator { Name = "乙", Tier = 2 } };
        var source = new Lineup
        {
            Slots = [new() { OperatorName = "乙" }],
            Guide = new LineupGuide { Tabs = [new() { TabType = 2, Operators =
                [new() { OperatorName = "甲", ShopLevel = mismatch == "tier" ? 4 : 3, MergeLevel = mismatch == "elite" ? 2 : 0 }] }] }
        };
        var editor = new LineupEditorViewModel(source, operators, [], new CovenantCalculator(operators, [], new Dictionary<string, string>()));
        editor.ApplyGuideTab(0, LineupPhase.后期).Should().Contain("草稿未修改");
        editor.ToLineup().Slots.Should().ContainSingle().Which.OperatorName.Should().Be("乙");
        source.Guide.Tabs[0].Operators.Should().ContainSingle().Which.OperatorName.Should().Be("甲");
    }

    [Fact]
    public void NullGuideFieldsAndEntriesAreSafeToReadButCannotReplaceAValidPhase()
    {
        const string json = """
        { "name": "恶形分享数据", "slots": [{ "operatorName": "乙" }], "guide": {
            "Title": null, "StrategyIds": null, "Tabs": [null, {
                "TabType": 2, "StartRound": 1, "EndRound": 4, "OperationIdea": null,
                "Operators": [null, { "OperatorName": "甲", "ShopLevel": 2, "SkillName": null, "SkillId": null, "ModuleName": null, "ModuleId": null }],
                "Equipment": [null, { "ItemId": "item-a", "Name": null, "Description": null }],
                "ActiveBonds": [null, { "BondId": "bond-a", "Name": null }]
            }] } }
        """;
        var imported = JsonSerializer.Deserialize<Lineup>(json)!;
        var operators = new[] { new Operator { Name = "甲", Tier = 2 }, new Operator { Name = "乙", Tier = 3 } };
        var editor = new LineupEditorViewModel(imported, operators, [], new CovenantCalculator(operators, [], new Dictionary<string, string>()));
        editor.Guide!.Title.Should().BeEmpty();
        var tab = editor.Guide.Tabs.Should().ContainSingle().Which;
        tab.HasInvalidEntries.Should().BeTrue();
        tab.OperationIdea.Should().BeEmpty();
        tab.Operators.Should().ContainSingle().Which.SkillDisplay.Should().Be("未指定");
        tab.Operators[0].ModuleDisplay.Should().Be("未指定");
        tab.Equipment.Should().ContainSingle().Which.Name.Should().Be("item-a");
        tab.Equipment[0].DisplayDescription.Should().BeEmpty();
        tab.ActiveBonds.Should().ContainSingle().Which.Name.Should().Be("bond-a");
        editor.ApplyGuideTab(0, LineupPhase.后期).Should().Contain("无效条目").And.Contain("草稿未修改");
        editor.ToLineup().Slots.Should().ContainSingle().Which.OperatorName.Should().Be("乙");
        editor.Guide.Clone().Tabs[0].HasInvalidEntries.Should().BeTrue();
    }

    [Fact]
    public async Task FutureSourceFieldsArePreservedAndDoNotDependOnTheDisposedJsonDocument()
    {
        var (operators, strategies) = await CatalogAsync();
        var fixture = JsonNode.Parse(Fixture)!;
        fixture["data"]!["guide"]!["guide"]!["tabs"]!.AsArray().Add(new JsonObject
        {
            ["tabType"] = 9, ["operationIdea"] = "未来攻略页", ["futureLayout"] = new JsonArray(1, 2, 3)
        });
        using var http = new HttpClient(new GuideHandler(fixture.ToJsonString()));
        var imported = (await new SklandGuideImporter(http, operators, strategies).ImportAsync(Url)).Lineup;
        var copy = imported.Guide!.Clone();
        copy.Tabs.Should().HaveCount(6);
        copy.Tabs[5].SourceData!.Value.GetProperty("futureLayout").GetArrayLength().Should().Be(3);
        JsonSerializer.Deserialize<Lineup>(JsonSerializer.Serialize(imported))!.Guide!.Tabs[5]
            .SourceData!.Value.GetProperty("operationIdea").GetString().Should().Be("未来攻略页");
    }

    private sealed class GuideHandler(string response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response) });
    }
}
