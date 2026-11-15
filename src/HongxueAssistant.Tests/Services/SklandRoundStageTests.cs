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

public class SklandRoundStageTests
{
    private const string Url = "https://game.skland.com/arknights/stronghold-guide/detail?header=0&guideId=2121069219205484656";

    private static async Task<(Lineup Lineup, List<Operator> Operators)> ImportAsync()
    {
        var store = new JsonDataStore(Path.Combine(AppContext.BaseDirectory, "data"));
        var operators = await store.LoadOperatorsAsync();
        using var http = new HttpClient(new FixtureHandler(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "TestData", "skland-guide-complete.json"))));
        var lineup = (await new SklandGuideImporter(http, operators, await store.LoadStrategiesAsync()).ImportAsync(Url)).Lineup;
        return (lineup, operators);
    }

    private static LineupEditorViewModel Editor(Lineup lineup, IEnumerable<Operator> operators)
        => new(lineup, operators, [], new CovenantCalculator(operators, [], new Dictionary<string, string>()));

    [Fact]
    public async Task EveryAuthorRoundIsImmediatelyAnIndependentEditableStage()
    {
        var (lineup, operators) = await ImportAsync();
        lineup.Slots.Should().HaveCount(8);
        lineup.RoundStages.Select(stage => (stage.StartRound, stage.EndRound))
            .Should().Equal((1, 4), (5, 7), (8, 11), (12, 13));
        lineup.RoundStages.Select(stage => stage.Slots.Count).Should().Equal(9, 9, 8, 8);
        lineup.RoundStages.Select(stage => stage.SourceTabIndex).Should().Equal(1, 2, 3, 4);
        var editor = Editor(lineup, operators);
        editor.HasRoundStages.Should().BeTrue();
        editor.CurrentStageTitle.Should().Be("成型阵容");
        for (var index = 0; index < 4; index++)
        {
            editor.SelectRoundStage(index);
            editor.CurrentRoundStageIndex.Should().Be(index);
            editor.CurrentGuideTabIndex.Should().Be(index + 1);
            editor.FilledSlotCount.Should().Be(lineup.RoundStages[index].Slots.Count);
            editor.Slots.Where(slot => slot.OperatorName.Length > 0).Select(slot => slot.OperatorName)
                .Should().Equal(lineup.Guide!.Tabs[index + 1].Operators.Select(op => op.OperatorName));
            editor.ValidationError.Should().BeEmpty();
        }
        editor.SelectPhase(LineupPhase.后期);
        editor.CurrentRoundStageIndex.Should().Be(-1);
        editor.CurrentStageTitle.Should().Be("成型阵容");
        editor.FilledSlotCount.Should().Be(8);
    }

    [Fact]
    public async Task RoundEditsNeverLeakToFinalOtherRoundsOrTheOriginalGuideAndResetRestoresAll()
    {
        var (lineup, operators) = await ImportAsync();
        var original = JsonSerializer.Serialize(lineup);
        var editor = Editor(lineup, operators);
        editor.SelectRoundStage(0);
        editor.SetSlotElite(0, true);
        editor.ToggleSlotEquipment(0, "前期装备");
        editor.ToggleSlotTag(0, LineupTag.经济);
        editor.SelectRoundStage(1);
        editor.Slots[0].IsElite.Should().BeFalse();
        editor.Slots[0].Equipments.Should().BeEmpty();
        editor.ToggleSlotEquipment(0, "中期装备");
        var saved = editor.ToLineup();
        saved.RoundStages[0].Slots[0].Should().Match<LineupSlot>(slot => slot.IsElite
            && slot.Equipments.SequenceEqual(new[] { "前期装备" }) && slot.Tags.Contains(LineupTag.经济));
        saved.RoundStages[1].Slots[0].Equipments.Should().Equal("中期装备");
        saved.Slots.Should().OnlyContain(slot => slot.Equipments.Count == 0);
        JsonSerializer.Serialize(lineup).Should().Be(original);
        JsonSerializer.Serialize(saved.Guide).Should().Be(JsonSerializer.Serialize(lineup.Guide));
        editor.Reset();
        editor.CurrentRoundStageIndex.Should().Be(-1);
        editor.ToLineup().RoundStages.Should().BeEquivalentTo(lineup.RoundStages);
    }

    [Fact]
    public async Task GuideOnlyAlpha2LineupsGainRoundsWithoutOverwritingManualWorkOrRefillingClearedStages()
    {
        var (lineup, operators) = await ImportAsync();
        lineup.RoundStages.Clear();
        lineup.EarlySlots.Add(new() { OperatorName = "车尔尼", Equipments = ["已有装备"] });
        var sourceJson = JsonSerializer.Serialize(lineup);
        var legacyTracking = new TrackingViewModel();
        legacyTracking.TrackLineup(lineup);
        legacyTracking.TrackedOperators.Should().HaveCount(17, "alpha2 guide-only lineups also retain transition targets");
        JsonSerializer.Serialize(lineup).Should().Be(sourceJson);
        var editor = Editor(lineup, operators);
        editor.RoundStages.Should().HaveCount(4);
        editor.ToLineup().EarlySlots.Single().Equipments.Should().Equal("已有装备");
        JsonSerializer.Serialize(lineup).Should().Be(sourceJson);
        editor.SelectRoundStage(0);
        editor.Clear();
        var saved = editor.ToLineup();
        saved.RoundStages.Should().HaveCount(4);
        saved.RoundStages[0].Slots.Should().BeEmpty();
        var reopened = Editor(saved, operators);
        reopened.FilledRoundCount(0).Should().Be(0);
        reopened.FilledRoundCount(1).Should().Be(9);
        reopened.SelectRoundStage(0);
        reopened.Clear();
        reopened.Reset();
        reopened.FilledRoundCount(0).Should().Be(0, "an intentionally cleared saved round is authoritative");
    }

    [Fact]
    public async Task SavingReopeningJsonSharingAndTrackingKeepAllFourRounds()
    {
        var (lineup, operators) = await ImportAsync();
        var editor = Editor(lineup, operators);
        editor.SelectRoundStage(2);
        editor.ToggleSlotEquipment(0, "回合专用装备");
        var saved = editor.ToLineup();
        var directory = Path.Combine(Path.GetTempPath(), "round-stage-roundtrip-" + Guid.NewGuid().ToString("N"));
        var rules = new LineupEquipmentService(new Dictionary<string, string>());
        try
        {
            var store = new LineupStore(directory, rules);
            await store.SaveAsync(saved);
            var loaded = (await store.LoadAllAsync()).Single();
            loaded.RoundStages.Should().BeEquivalentTo(saved.RoundStages);
            var share = new LineupShareService(rules, Path.Combine(directory, "cache"));
            var imported = await share.ImportAsync(JsonSerializer.Serialize(loaded));
            imported.RoundStages.Should().BeEquivalentTo(saved.RoundStages);
            Editor(imported, operators).FilledRoundCount(3).Should().Be(8);
            var tracking = new TrackingViewModel { DetectedTerminalLevel = 6 };
            tracking.TrackLineup(imported);
            tracking.TrackedOperators.Should().HaveCount(17);
            tracking.TrackedOperators.Count(entry => entry.Role == TrackingRole.Final).Should().Be(8);
            tracking.TrackedOperators.Single(entry => entry.Name == "复奏").Stages.Should().Contain("1").And.Contain("4");
            tracking.TrackedOperators.Single(entry => entry.Name == "车尔尼").Stages.Should().Contain("1").And.Contain("11");
            tracking.TrackedOperators.Should().OnlyContain(entry => entry.ExpiresAt == 0 && !entry.IsExpired,
                "author rounds are not shop terminal levels");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void RoundTrackingUsesLatestRoundRequirementsAndFinalOperatorsRemainFinal()
    {
        var first = new LineupRoundStage
        {
            StartRound = 1, EndRound = 4, Slots = [new() { OperatorName = "大成", Equipments = ["较早装备"] },
                new() { OperatorName = "过渡", Equipments = ["早期装备"] }]
        };
        var second = new LineupRoundStage
        {
            StartRound = 5, EndRound = 7,
            Slots = [new() { OperatorName = "过渡", IsElite = true, Equipments = ["后续装备"] }]
        };
        var tracking = new TrackingViewModel { DetectedTerminalLevel = 6 };
        tracking.TrackLineup(new Lineup
        {
            Slots = [new() { OperatorName = "大成", IsElite = true, Equipments = ["最终装备"] }],
            RoundStages = [first, second]
        });
        var final = tracking.TrackedOperators.Single(entry => entry.Name == "大成");
        final.Role.Should().Be(TrackingRole.Final);
        final.Equipments.Should().Equal("最终装备");
        var transition = tracking.TrackedOperators.Single(entry => entry.Name == "过渡");
        transition.Role.Should().Be(TrackingRole.Transition);
        transition.IsElite.Should().BeTrue();
        transition.Stages.Should().Be(first.DisplayTitle + " · " + second.DisplayTitle);
        transition.Equipments.Should().Equal("后续装备");
        transition.IsExpired.Should().BeFalse();
    }

    [Fact]
    public void RoundSpecificLegacyTransformationsUpgradeWhenImportingJson()
    {
        var rules = new LineupEquipmentService(new Dictionary<string, string> { ["莱塔尼亚小鼓"] = "莱塔尼亚" });
        var lineup = new Lineup { RoundStages = [new() { StartRound = 1, EndRound = 4,
            Slots = [new() { OperatorName = "霍尔海雅", TransformedCovenant = "莱塔尼亚" }] }] };
        rules.Upgrade(lineup).Should().BeTrue();
        var slot = lineup.RoundStages[0].Slots.Single();
        slot.TransformedCovenant.Should().BeNull();
        slot.Equipments.Should().Equal("变形同构体", "莱塔尼亚小鼓");
    }

    [Fact]
    public void AnOverfullRoundCannotBeSavedAndClearingItDoesNotTouchAnotherRound()
    {
        var operators = Enumerable.Range(0, 10).Select(i => new Operator { Name = "候选" + i }).ToArray();
        var lineup = new Lineup { RoundStages =
        [
            new() { StartRound = 1, EndRound = 4, Slots = operators.Select(op => new LineupSlot { OperatorName = op.Name }).ToList() },
            new() { StartRound = 5, EndRound = 7, Slots = [new() { OperatorName = operators[0].Name }] }
        ] };
        var editor = Editor(lineup, operators);
        editor.ValidationError.Should().Contain(lineup.RoundStages[0].DisplayTitle).And.Contain("9");
        editor.SelectRoundStage(0);
        editor.Clear();
        editor.ValidationError.Should().BeEmpty();
        editor.FilledRoundCount(1).Should().Be(1);
        editor.Reset();
        editor.FilledRoundCount(0).Should().Be(10);
    }

    [Fact]
    public void ExplicitSourceMechIsRetainedWhileAutomaticRoundMechsCanBeRemovedWithTheirStrategy()
    {
        var operators = new[] { new Operator { Name = LineupEquipmentService.MechName,
            RequiredStrategyId = LineupEquipmentService.MechStrategy }, new Operator { Name = "甲" } };
        var lineup = new Lineup { RoundStages =
        [
            new() { StartRound = 1, EndRound = 4, Slots = [new() { OperatorName = LineupEquipmentService.MechName }] },
            new() { StartRound = 5, EndRound = 7, Slots = [new() { OperatorName = "甲" }] }
        ] };
        var editor = Editor(lineup, operators);
        editor.ToLineup().RoundStages[0].Slots.Should().ContainSingle().Which.OperatorName.Should().Be(LineupEquipmentService.MechName);
        editor.ValidationError.Should().Contain("策略");
        editor.SetStrategy(0, LineupEquipmentService.MechStrategy);
        editor.ValidationError.Should().BeEmpty();
        editor.SelectRoundStage(1);
        editor.MechSlotIndex.Should().BeGreaterThanOrEqualTo(0);
        editor.ClearStrategies();
        editor.ToLineup().RoundStages[0].Slots.Should().ContainSingle().Which.OperatorName.Should().Be(LineupEquipmentService.MechName);
        editor.ToLineup().RoundStages[1].Slots.Should().ContainSingle().Which.OperatorName.Should().Be("甲");
        editor.Reset();
        editor.ToLineup().RoundStages.Should().BeEquivalentTo(lineup.RoundStages);
    }

    [Fact]
    public void InvalidSourceRoundEntryIsVisibleAndCanBeRemovedWithoutDiscardingTheRound()
    {
        var operators = new[] { new Operator { Name = "甲" } };
        var lineup = new Lineup { Guide = new LineupGuide { Tabs = [new()
        {
            TabType = 2, StartRound = 1, EndRound = 4, HasInvalidEntries = true,
            Operators = [new() { OperatorName = "甲" }]
        }] } };
        var editor = Editor(lineup, operators);
        editor.RoundStages.Should().ContainSingle();
        editor.SelectRoundStage(0);
        editor.ValidationError.Should().Contain("替换或移除");
        var invalid = editor.Slots.ToList().FindIndex(slot => slot.OperatorName.Contains("来源条目不完整"));
        invalid.Should().BeGreaterThanOrEqualTo(0);
        editor.SetSlotOperator(invalid, "");
        editor.ValidationError.Should().BeEmpty();
        editor.ToLineup().RoundStages[0].Slots.Should().ContainSingle().Which.OperatorName.Should().Be("甲");
    }

    [Theory]
    [InlineData("round")]
    [InlineData("operator")]
    [InlineData("equipment")]
    [InlineData("elite")]
    [InlineData("tag")]
    [InlineData("remove")]
    public async Task ChangesToEditableRoundsInvalidateTheShareCache(string change)
    {
        var folder = Path.Combine(Path.GetTempPath(), "round-stage-cache-" + Guid.NewGuid().ToString("N"));
        var lineup = new Lineup { Name = "独立回合", RoundStages = [new() { StartRound = 1, EndRound = 4,
            Slots = [new() { OperatorName = "甲" }] }] };
        using var handler = new LinkHandler();
        using var http = new HttpClient(handler);
        var share = new LineupShareService(new LineupEquipmentService(new Dictionary<string, string>()), folder, http);
        try
        {
            var originalJson = JsonSerializer.Serialize(lineup);
            var first = await share.ExportAsync(lineup);
            var stage = lineup.RoundStages[0];
            switch (change)
            {
                case "round": stage.EndRound = 5; break;
                case "operator": stage.Slots[0].OperatorName = "乙"; break;
                case "equipment": stage.Slots[0].Equipments.Add("阶段装备"); break;
                case "elite": stage.Slots[0].IsElite = true; break;
                case "tag": stage.Slots[0].Tags.Add(LineupTag.经济); break;
                case "remove": stage.Slots.Clear(); break;
            }
            (await share.ExportAsync(lineup)).Should().NotBe(first);
            (await share.ExportAsync(JsonSerializer.Deserialize<Lineup>(originalJson)!)).Should().Be(first);
            handler.Calls.Should().Be(2);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    private sealed class LinkHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("https://dpaste.com/ROUND" + Calls) });
        }
    }

    private sealed class FixtureHandler(string response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response) });
    }
}
