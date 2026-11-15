// Added for ASP Assistant Arknights on 2026-11-13; see NOTICE.
using HongxueAssistant.Core.Models;
using HongxueAssistant.Core.Services;
using HongxueAssistant.Core.ViewModels;
using FluentAssertions;

namespace HongxueAssistant.Tests.ViewModels;

[Collection("Update environment")]
public class EditorPanelsTests
{
    private static readonly Operator[] Ops =
    [
        new() { Name = "冻河", CoreCovenants = ["乌萨斯"], AdditionalCovenants = ["迅捷"] },
        new() { Name = "青枳", AdditionalCovenants = ["坚守", "迅捷"] },
        new() { Name = "折桠", CoreCovenants = ["乌萨斯"], AdditionalCovenants = ["不屈"] },
    ];

    private static LineupEditorViewModel Create() => new(null, Ops, [], new CovenantCalculator(Ops,
    [
        new() { Name = "乌萨斯", BondType = "SEASON", ActivateCount = 3 }, new() { Name = "迅捷", BondType = "REGULAR", ActivateCount = 2 },
        new() { Name = "坚守", BondType = "REGULAR", ActivateCount = 2 }, new() { Name = "不屈", BondType = "REGULAR", ActivateCount = 2 },
    ], new Dictionary<string, string>()));

    [Fact]
    public void SearchMatchesTextOrInitials()
    {
        SearchMatch.Load(new Dictionary<string, string> { ["冻河"] = "dh", ["集结指示"] = "jjzs", ["杰西卡"] = "jxk" });
        SearchMatch.Matches("dh", "冻河").Should().BeTrue();
        SearchMatch.Matches("DH", "冻河").Should().BeTrue();
        SearchMatch.Matches("jj", "集结指示").Should().BeTrue();
        SearchMatch.Matches("zs", "集结指示").Should().BeFalse("initials match from the start");
        SearchMatch.Matches("jxk", "集结指示", "杰西卡").Should().BeTrue("any of the texts");
        SearchMatch.Matches("冻", "冻河").Should().BeTrue();
        SearchMatch.Matches("", "冻河").Should().BeTrue();
        SearchMatch.Matches("xx", "冻河").Should().BeFalse();
    }

    [Fact]
    public void CopySlotToAnotherPhase()
    {
        var vm = Create();
        vm.SelectPhase(LineupPhase.后期);
        vm.SetSlotOperator(0, "冻河");
        vm.ToggleSlotTag(0, LineupTag.输出);
        vm.SetSlotElite(0, true);
        vm.Slots[0].CandidateEquipments.Add(new() { Name = "集结指示", IsPriority = true });
        vm.CopySlotToPhase(0, LineupPhase.中期).Should().BeTrue();
        vm.CopySlotToPhase(0, LineupPhase.中期).Should().BeTrue("copying again overwrites the same operator");
        vm.SelectPhase(LineupPhase.中期);
        vm.Slots.Count(s => s.OperatorName == "冻河").Should().Be(1);
        vm.Slots.First(s => s.OperatorName == "冻河").Tags.Should().Equal(LineupTag.输出);
        vm.Slots.First(s => s.OperatorName == "冻河").IsElite.Should().BeTrue();
        vm.Slots.First(s => s.OperatorName == "冻河").CandidateEquipments.Single().IsPriority.Should().BeTrue();
        vm.Slots.First(s => s.OperatorName == "冻河").CandidateEquipments.Single().IsPriority = false;
        vm.SelectPhase(LineupPhase.后期);
        vm.Slots[0].CandidateEquipments.Single().IsPriority.Should().BeTrue("phase copies must not share mutable candidates");
        vm.SelectPhase(LineupPhase.中期);
        vm.CopySlotToPhase(0, LineupPhase.中期).Should().BeFalse("same phase");
    }

    [Fact]
    public void SetStrategiesPreservesTags()
    {
        var vm = Create();
        vm.SetStrategies([new() { StrategyId = "band_jesica", Tags = [StrategyTag.阵容核心, StrategyTag.优选] }, new() { StrategyId = "band_mess" }]);
        vm.StrategySlots.Should().HaveCount(LineupEditorViewModel.MaxSlots);
        vm.ToLineup().Strategies.Select(s => (s.StrategyId, s.Tags.Count)).Should().Equal(("band_jesica", 2), ("band_mess", 0));
        vm.SetStrategies([]);
        vm.ToLineup().Strategies.Should().BeEmpty();
    }

    [Fact]
    public void CountsWithACandidateInAPlace()
    {
        var vm = Create();
        vm.SelectPhase(LineupPhase.后期);
        vm.SetSlotOperator(0, "冻河");
        vm.SetSlotOperator(1, "折桠");
        vm.CountsWith(1, "").GetValueOrDefault("乌萨斯").Should().Be(1, "place 1 emptied");
        var with = vm.CountsWith(1, "青枳");
        with.GetValueOrDefault("迅捷").Should().Be(2);
        with.GetValueOrDefault("乌萨斯").Should().Be(1);
    }

    [Fact]
    public void CountsWithKeepsThePositionsEquipmentLikeReplacingTheOperator()
    {
        var covenants = new CovenantInfo[]
        {
            new() { Name = "乌萨斯", BondType = "SEASON", ActivateCount = 3 },
            new() { Name = "谢拉格", BondType = "SEASON", ActivateCount = 3 },
            new() { Name = "迅捷", ActivateCount = 2 }, new() { Name = "坚守", ActivateCount = 2 },
        };
        var calculator = new CovenantCalculator(Ops, covenants, new Dictionary<string, string> { ["谢拉格转职"] = "谢拉格" });
        var vm = new LineupEditorViewModel(new Lineup
        {
            Slots = [new() { OperatorName = "冻河", Equipments = [CovenantCalculator.ShapeshifterEquipment, "谢拉格转职"], IsElite = true }, new() { OperatorName = "折桠" }]
        }, Ops, [], calculator);
        var counts = vm.CountsWith(0, "青枳");
        counts.GetValueOrDefault("谢拉格").Should().Be(1);
        vm.Slots[0].OperatorName.Should().Be("冻河", "previewing a choice must not edit the lineup");
        vm.CountsWith(0, "").GetValueOrDefault("谢拉格").Should().Be(0);
        vm.SetSlotOperator(0, "青枳");
        vm.ActiveStats.ToDictionary(s => s.Name, s => s.Count).Should().BeEquivalentTo(counts);
    }
}
