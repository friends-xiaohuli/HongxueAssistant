using HongxueAssistant.Core.Data;
using HongxueAssistant.Core.GameState;
using HongxueAssistant.Core.Models;
using HongxueAssistant.Core.Services;
using HongxueAssistant.Core.ViewModels;
using FluentAssertions;

namespace HongxueAssistant.Tests.ViewModels;

public class OperatorCatalogueLinkageTests
{
    private readonly JsonDataStore _store = new(Path.Combine(AppContext.BaseDirectory, "data"));

    [Theory]
    [InlineData("焰影苇草", "协防干员")]
    [InlineData("缄默德克萨斯", "叙拉古")]
    [InlineData("能天使", "协防干员")]
    [InlineData("新约能天使", "协防干员")]
    [InlineData("莫斯提马", "协防干员")]
    [InlineData("死芒", "协防干员")]
    public async Task CorrectedCustomCovenantFlowsIntoBrowseAndLineupCounts(string name, string covenant)
    {
        var custom = await _store.LoadCustomOperatorsAsync();
        var op = custom.Single(o => o.Name == name);
        op.IsSelectedCustom = true;
        op.CoreCovenants.Concat(op.AdditionalCovenants).Should().Equal(covenant);
        op.Normal.Skill.Should().BeNull();
        op.Elite.Skill.Should().BeNull();
        var browse = new OperatorBrowseViewModel { SelectedOperatorSource = OperatorSourceFilter.Custom };
        browse.LoadOperators(custom);
        browse.FilteredOperators.Should().Equal(op);
        var (covenants, equipment) = await _store.LoadCovenantsAsync();
        var calculator = new CovenantCalculator(custom, covenants, equipment);
        var stats = calculator.Compute(new Lineup { Slots = [new() { OperatorName = name }] });
        stats.Single(s => s.Name == covenant).Count.Should().Be(1);
        stats.Where(s => s.Count > 0).Should().ContainSingle();
    }

    [Fact]
    public async Task SupportProfileIsBrowsableAndRequiresItsOwnStrategyInEditor()
    {
        var operators = await _store.LoadOperatorsAsync();
        var op = operators.Single(o => o.Name == PlannerStrategies.PithName);
        op.Profile!.Profession.Should().Be("辅助");
        op.Profile.SubProfession.Should().Be("巫役");
        op.Normal.Skill!.Name.Should().Be("战术咏唱·双型");
        op.Normal.Skill.SpCost.Should().Be(20);
        op.Elite.Skill!.SpCost.Should().Be(17);
        op.RequiredStrategyId.Should().Be(PlannerStrategies.PithId);
        var browse = new OperatorBrowseViewModel { SelectedOperatorSource = OperatorSourceFilter.Default, SearchText = op.Name };
        browse.LoadOperators(operators);
        browse.FilteredOperators.Should().Equal(op);
        var (covenants, equipment) = await _store.LoadCovenantsAsync();
        var calculator = new CovenantCalculator(operators, covenants, equipment);
        var editor = new LineupEditorViewModel(null, operators, [], calculator);
        editor.CanSelectOperator(op).Should().BeFalse();
        editor.SetStrategy(0, PlannerStrategies.PithId);
        editor.CanSelectOperator(op).Should().BeTrue();
        editor.SetSlotOperator(0, op.Name);
        editor.ValidationError.Should().BeEmpty();
        editor.SetStrategy(0, "");
        editor.ValidationError.Should().Contain("优等生").And.NotContain("森蚺");
    }

    [Fact]
    public async Task PithPlanSavesReloadsAndTracksOneRealSupportWithoutDoubleCountingHarmony()
    {
        var support = (await _store.LoadOperatorsAsync()).Single(o => o.Name == PlannerStrategies.PithName);
        Operator[] operators = [new() { Name = "炎一", CoreCovenants = ["炎"] },
            new() { Name = "炎二", CoreCovenants = ["炎"] }, support];
        CovenantInfo[] covenants = [new() { Name = "炎", BondType = "SEASON", ActivateCount = 3 },
            new() { Name = "调和", BondType = "REGULAR", ActivateCount = 1 }];
        var calculator = new CovenantCalculator(operators, covenants, new Dictionary<string, string>());
        var planner = new LineupPlanner(operators, covenants);
        var request = new PlannerRequest { Strategy = PlannerStrategy.Pith, PeopleOptions = [3], TransformOptions = [0],
            Goals = [new("炎", 1, 3, false, true)] };
        var plan = planner.Solve(request).Single();
        plan.Operators.Should().HaveCount(2).And.NotContain(o => o.Name == support.Name);
        plan.Counts["炎"].Should().Be(3);
        var lineup = new PlannerLineupItem { Source = plan }.ToLineup(calculator.EquipmentRules);
        lineup.Slots.Should().HaveCount(3).And.ContainSingle(s => s.OperatorName == support.Name);
        calculator.Compute(lineup).Single(c => c.Name == "炎").Count.Should().Be(3);
        var folder = Path.Combine(Path.GetTempPath(), "operator-catalogue-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LineupStore(folder, calculator.EquipmentRules);
            await store.SaveAsync(lineup);
            var reloaded = (await store.LoadAllAsync()).Single();
            calculator.Compute(reloaded).Single(c => c.Name == "炎").Count.Should().Be(3);
            new LineupEditorViewModel(reloaded, operators, [], calculator).ValidationError.Should().BeEmpty();
            var tracking = new TrackingViewModel();
            tracking.TrackLineup(reloaded);
            tracking.TrackedOperators.Should().ContainSingle(t => t.Name == support.Name);
            tracking.IsTracked(support.Name).Should().BeTrue();
            var shop = new List<ShopItem> { new() { Name = support.Name } };
            GameStateUpdater.UpdateShopTracking(shop, tracking.ActiveEntries.ToList());
            shop[0].IsTracked.Should().BeTrue();
        }
        finally { Directory.Delete(folder, true); }
    }
}
