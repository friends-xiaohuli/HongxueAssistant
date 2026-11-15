using HongxueAssistant.Core.Data;
using HongxueAssistant.Core.Models;
using HongxueAssistant.Core.Services;
using HongxueAssistant.Core.ViewModels;
using FluentAssertions;

namespace HongxueAssistant.Tests.ViewModels;

public class LineupStateFilterTests
{
    [Fact]
    public void RunStatesAreDisjointAndKeepTheSavedOrder()
    {
        var (vm, _) = LineupStateFilterTestData.Create();
        var original = vm.Lineups.Select(l => l.Id).ToArray();
        vm.SelectedAvailabilityFilter = LineupAvailabilityFilter.Available;
        vm.FilteredLineups.Select(l => l.Id).Should().Equal("safe");
        vm.SelectedAvailabilityFilter = LineupAvailabilityFilter.Pending;
        vm.FilteredLineups.Select(l => l.Id).Should().Equal("fine", "repair", "fine2", "repair2", "repair3");
        vm.SelectedAvailabilityFilter = LineupAvailabilityFilter.Unavailable;
        vm.FilteredLineups.Select(l => l.Id).Should().Equal("broken");
        vm.SelectedAvailabilityFilter = LineupAvailabilityFilter.All;
        vm.FilteredLineups.Select(l => l.Id).Should().Equal(original);
        vm.Lineups.Select(l => l.Id).Should().Equal(original);
    }

    [Fact]
    public void SearchAndCovenantFiltersApplyInsideTheSelectedState()
    {
        var (vm, _) = LineupStateFilterTestData.Create();
        vm.SelectedAvailabilityFilter = LineupAvailabilityFilter.Pending;
        vm.SearchText = "安全";
        vm.FilteredLineups.Should().BeEmpty();
        vm.SearchText = "需替补";
        vm.FilteredLineups.Select(l => l.Id).Should().Equal("repair", "repair2", "repair3");
        vm.ToggleFilterCovenant("炎");
        vm.FilteredLineups.Should().BeEmpty();
        vm.SearchText = "";
        vm.FilteredLineups.Select(l => l.Id).Should().Equal("fine", "fine2");
        vm.ClearFilters();
        vm.FilteredLineups.Should().HaveCount(5);
    }

    [Fact]
    public void UnbanningReclassifiesEveryLineupAndZeroBansStillCountsAsKnownRun()
    {
        var (vm, bans) = LineupStateFilterTestData.Create();
        vm.SelectedAvailabilityFilter = LineupAvailabilityFilter.Pending;
        bans.ClearBans();
        vm.HasRunInfo.Should().BeTrue();
        vm.FilteredLineups.Should().BeEmpty();
        vm.SelectedAvailabilityFilter = LineupAvailabilityFilter.Available;
        vm.FilteredLineups.Should().HaveCount(7);

        vm.SetRunInfo(null, null);
        vm.HasRunInfo.Should().BeFalse();
        vm.SelectedAvailabilityFilter.Should().Be(LineupAvailabilityFilter.All);
        vm.Recommendations.Should().BeEmpty();
        vm.FilteredLineups.Should().HaveCount(7);
    }

    [Fact]
    public void UnreachedTargetNeedsConfirmationEvenWithoutABannedOperator()
    {
        var (vm, bans) = LineupStateFilterTestData.Create();
        bans.ClearBans();
        vm.Lineups.Add(new Lineup { Id = "short", Name = "目标未达成", Slots = [new() { OperatorName = "炎1" }],
            TargetCovenants = [new() { Name = "炎", Tier = 1 }] });
        vm.SelectedAvailabilityFilter = LineupAvailabilityFilter.Pending;
        vm.FilteredLineups.Should().ContainSingle().Which.Id.Should().Be("short");
        vm.RecommendationFor(vm.FilteredLineups[0])!.Kind.Should().Be(RecommendationKind.NeedsRepair);
    }
}

internal static class LineupStateFilterTestData
{
    public static (LineupViewModel Vm, BanViewModel Bans) Create()
    {
        var operators = new[] { "炎", "维多利亚", "乌萨斯", "谢拉格" }
            .SelectMany(core => Enumerable.Range(1, 4).Select(i => new Operator { Name = core + i, CoreCovenants = [core] })).ToList();
        var covenants = new[] { "炎", "维多利亚", "乌萨斯", "谢拉格" }
            .Select(name => new CovenantInfo { Name = name, BondType = "SEASON", ActivateCount = 3 }).ToList();
        var calculator = new CovenantCalculator(operators, covenants, new Dictionary<string, string>());
        var vm = new LineupViewModel(new LineupStore(Path.Combine(Path.GetTempPath(), "unused-lineup-state"), calculator.EquipmentRules), calculator);
        Lineup Make(string id, string name, string core, int count) => new() { Id = id, Name = name,
            Slots = [.. Enumerable.Range(1, count).Select(i => new LineupSlot { OperatorName = core + i })],
            TargetCovenants = [new() { Name = core, Tier = 1 }] };
        vm.LoadLineups([
            Make("broken", "目标盟约被禁", "乌萨斯", 3), Make("safe", "安全阵容", "谢拉格", 3),
            Make("fine", "目标保持一", "炎", 4), Make("repair", "需替补一", "维多利亚", 3),
            Make("fine2", "目标保持二", "炎", 4), Make("repair2", "需替补二", "维多利亚", 3),
            Make("repair3", "需替补三", "维多利亚", 3)]);
        var bans = new BanViewModel();
        bans.BansChanged += () =>
        {
            bool Unavailable(string name) => bans.IsBanned(name)
                || CovenantBanRules.IsExcluded(operators.Single(o => o.Name == name), bans.BannedCovenants);
            vm.SetRunInfo(Unavailable, new HashSet<string>(bans.BannedCovenants), operators.Where(o => Unavailable(o.Name)).Select(o => o.Name).ToList());
        };
        bans.SetBans(["炎4", "维多利亚1"]);
        bans.SetCovenantBans(["乌萨斯"]);
        return (vm, bans);
    }
}
