using HongxueAssistant.Core.Models;
using HongxueAssistant.Core.Services;
using HongxueAssistant.Core.ViewModels;
using FluentAssertions;

namespace HongxueAssistant.Tests.ViewModels;

public class StrategySupportSearchTests
{
    [Theory]
    [InlineData("盟约·辅助干员")]
    [InlineData("盟约辅助干员")]
    [InlineData("盟约辅助")]
    [InlineData("辅助干员")]
    public void StrategyPieceIsFoundWithoutBecomingAnAdditionalLock(string query)
    {
        Operator[] operators = [new() { Name = PlannerStrategies.PithName, RequiredStrategyId = PlannerStrategies.PithId },
            new() { Name = "Pith(卫戍协议)", IsCustom = true, IsSelectedCustom = true }, new() { Name = "正常干员" }];
        var vm = new LineupPlannerViewModel(new LineupPlanner(operators, []), operators, new(), EquipmentTestData.Rules);
        vm.AddLocked("正常干员").Should().BeTrue();
        vm.LockSearchText = query;
        vm.LockSuggestions.Should().BeEmpty();
        vm.Strategy = PlannerStrategy.Pith;
        vm.LockSuggestions.Should().ContainSingle().Which.Name.Should().Be(PlannerStrategies.PithName);
        vm.AddLocked(query).Should().BeFalse();
        vm.AddLocked(PlannerStrategies.PithName).Should().BeFalse();
        vm.TrackedOperatorsProvider = () => [PlannerStrategies.PithName, "正常干员"];
        vm.ImportTrackedCommand.Execute(null);
        vm.LockedOperators.Select(o => o.Name).Should().Equal("正常干员");
        vm.BuildRequest().LockedOperators.Should().Equal("正常干员");
        vm.Strategy = PlannerStrategy.Other;
        vm.LockSuggestions.Should().BeEmpty();
        vm.AddLocked(query).Should().BeFalse();
        vm.LockedOperators.Select(o => o.Name).Should().Equal("正常干员");
        vm.Strategy = PlannerStrategy.Pith;
        vm.LockSuggestions.Should().ContainSingle().Which.Name.Should().Be(PlannerStrategies.PithName);
        vm.LockSearchText = "Pith";
        vm.LockSuggestions.Should().ContainSingle().Which.Name.Should().Be("Pith(卫戍协议)");
        SearchMatch.Matches("Pith", PlannerStrategies.PithName).Should().BeFalse();

        var browse = new OperatorBrowseViewModel { SearchText = query };
        browse.LoadOperators(operators.ToList());
        browse.FilteredOperators.Should().ContainSingle().Which.Name.Should().Be(PlannerStrategies.PithName);
    }

    [Fact]
    public async Task SwitchingAwayRemovesTheStrategyPieceFromResultsButPreservesASavedLineup()
    {
        Operator[] operators = [new() { Name = PlannerStrategies.PithName, RequiredStrategyId = PlannerStrategies.PithId, AdditionalCovenants = ["调和"] },
            .. Enumerable.Range(1, 3).Select(i => new Operator { Name = $"炎{i}", Tier = 1, CoreCovenants = ["炎"] })];
        CovenantInfo[] covenants = [new() { Name = "炎", BondType = "SEASON", ActivateCount = 3 },
            new() { Name = "调和", BondType = "REGULAR", ActivateCount = 1 }];
        var vm = new LineupPlannerViewModel(new LineupPlanner(operators, covenants), operators, new(), EquipmentTestData.Rules)
        { Strategy = PlannerStrategy.Pith, DebounceMilliseconds = 0 };
        vm.AllCovenants.Single(c => c.Name == "炎").ToggleLevel1Command.Execute(null);
        vm.IsActive = true;
        await vm.PendingSearch!;
        vm.VisibleLineups.Should().NotBeEmpty();
        vm.VisibleLineups.Should().OnlyContain(l => l.StrategyPiece!.Name == PlannerStrategies.PithName);
        Lineup? saved = null;
        vm.SaveRequested += lineup => saved = lineup;
        vm.SaveLineupCommand.Execute(vm.VisibleLineups[0]);
        saved!.Slots.Count(s => s.OperatorName == PlannerStrategies.PithName).Should().Be(1);
        var original = System.Text.Json.JsonSerializer.Serialize(saved);
        vm.Strategy = PlannerStrategy.Other;
        await vm.PendingSearch!;
        vm.VisibleLineups.Should().NotBeEmpty();
        vm.VisibleLineups.Should().OnlyContain(l => l.StrategyPiece == null && !l.Source.HasPith);
        System.Text.Json.JsonSerializer.Serialize(saved).Should().Be(original);
    }
}
