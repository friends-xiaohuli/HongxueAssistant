using HongxueAssistant.Core.Models;
using HongxueAssistant.Core.Services;
using HongxueAssistant.Core.ViewModels;
using FluentAssertions;

namespace HongxueAssistant.Tests.ViewModels;

public class PlannerLockedBanTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BannedLockedOperatorIsRetainedAndPlanningResumesUntilUnbanned(bool covenantBan)
    {
        var (vm, bans, _, _) = Create();
        vm.AddLocked("必带").Should().BeTrue();
        var locked = vm.LockedOperators.Single();
        vm.IsActive = true;
        await vm.PendingSearch!;
        vm.VisibleLineups.Should().NotBeEmpty();
        vm.VisibleLineups.Should().OnlyContain(l => l.Source.Operators.Any(o => o.Name == "必带" && o.IsLocked));

        if (covenantBan) bans.SetCovenantBans(["炎"]);
        else bans.SetBans(["必带"]);
        await vm.PendingSearch!;

        vm.LockedOperators.Should().ContainSingle().Which.Should().BeSameAs(locked);
        locked.IsUnavailable.Should().BeTrue();
        vm.ActiveLockedCount.Should().Be(0);
        vm.BuildRequest().LockedOperators.Should().BeEmpty();
        vm.HasError.Should().BeFalse();
        vm.VisibleLineups.Should().NotBeEmpty();
        vm.VisibleLineups.Should().OnlyContain(l => l.Source.Operators.All(o => o.Name != "必带"));
        Lineup? saved = null;
        Lineup? tracked = null;
        vm.SaveRequested += lineup => saved = lineup;
        vm.TrackRequested += lineup => tracked = lineup;
        vm.SaveLineupCommand.Execute(vm.VisibleLineups[0]);
        vm.TrackLineupCommand.Execute(vm.VisibleLineups[0]);
        saved!.Slots.Should().NotContain(s => s.OperatorName == "必带");
        tracked!.Slots.Should().NotContain(s => s.OperatorName == "必带");

        if (covenantBan) bans.SetCovenantBans([]);
        else bans.SetBans([]);
        await vm.PendingSearch!;

        vm.LockedOperators.Should().ContainSingle().Which.Should().BeSameAs(locked);
        locked.IsUnavailable.Should().BeFalse();
        vm.ActiveLockedCount.Should().Be(1);
        vm.BuildRequest().LockedOperators.Should().Equal("必带");
        vm.VisibleLineups.Should().NotBeEmpty();
        vm.VisibleLineups.Should().OnlyContain(l => l.Source.Operators.Any(o => o.Name == "必带" && o.IsLocked));
    }

    [Theory]
    [InlineData("炎|谢拉格", "精准", "炎", false)]
    [InlineData("炎|谢拉格", "精准", "炎|谢拉格", true)]
    [InlineData("炎|谢拉格", "精准", "炎|精准", false)]
    [InlineData("炎", "精准", "精准", false)]
    [InlineData("", "精准|奥术", "精准", false)]
    [InlineData("", "精准|奥术", "精准|奥术", true)]
    public void LockedAvailabilityUsesTheSharedCovenantRules(string core, string additional, string banned, bool unavailable)
    {
        var (vm, bans, operators, _) = Create();
        operators[0].CoreCovenants = [.. core.Split('|', StringSplitOptions.RemoveEmptyEntries)];
        operators[0].AdditionalCovenants = [.. additional.Split('|')];
        vm.AddLocked("必带");

        bans.SetCovenantBans(banned.Split('|'));

        vm.LockedOperators.Single().IsUnavailable.Should().Be(unavailable);
        vm.ActiveLockedCount.Should().Be(unavailable ? 0 : 1);
        vm.BuildRequest().LockedOperators.Contains("必带").Should().Be(!unavailable);
    }

    [Fact]
    public void ImportedAndNewLocksReflectExistingBansAndCanStillBeRemoved()
    {
        var (vm, bans, _, _) = Create();
        bans.SetBans(["必带"]);
        vm.TrackedOperatorsProvider = () => ["必带", "候选1"];
        vm.ImportTrackedCommand.Execute(null);
        vm.LockedOperators.Select(l => l.Name).Should().Equal("必带", "候选1");
        vm.LockedOperators[0].IsUnavailable.Should().BeTrue();
        vm.ActiveLockedCount.Should().Be(1);
        vm.BuildRequest().LockedOperators.Should().Equal("候选1");
        vm.LockedOperators[0].RemoveCommand.Execute(null);
        vm.ActiveLockedCount.Should().Be(1);
        vm.AddLocked("必带").Should().BeTrue();
        vm.LockedOperators.Last().IsUnavailable.Should().BeTrue();
        vm.ActiveLockedCount.Should().Be(1);
        vm.ClearLockedCommand.Execute(null);
        vm.ActiveLockedCount.Should().Be(0);
        vm.HasLocked.Should().BeFalse();
    }

    [Fact]
    public void ClearingOneBanSourceDoesNotReactivateAnOperatorStillBannedByAnother()
    {
        var (vm, bans, _, _) = Create();
        vm.AddLocked("必带");
        bans.SetBans(["必带"]);
        bans.SetCovenantBans(["炎"]);
        bans.SetBans([]);
        vm.ActiveLockedCount.Should().Be(0);
        vm.LockedOperators.Single().IsUnavailable.Should().BeTrue();

        bans.SetCovenantBans([]);
        vm.ActiveLockedCount.Should().Be(1);
        vm.LockedOperators.Single().IsUnavailable.Should().BeFalse();
    }

    [Fact]
    public void SavedLockedListKeepsItsExistingLimitWhileOnlyAvailableLocksAreSubmitted()
    {
        var (vm, bans, operators, _) = Create();
        foreach (var op in operators.Take(LineupPlanner.MaxLockedOperators))
            vm.AddLocked(op.Name).Should().BeTrue();
        bans.SetBans(["必带"]);

        vm.LockedOperators.Should().HaveCount(9);
        vm.ActiveLockedCount.Should().Be(8);
        vm.BuildRequest().LockedOperators.Should().HaveCount(8);
        vm.AddLocked("候选9").Should().BeFalse();
        bans.SetBans([]);
        vm.ActiveLockedCount.Should().Be(9);
    }

    [Fact]
    public void DeselectingACustomOperatorStillRemovesItsLock()
    {
        var (vm, bans, operators, _) = Create();
        operators[0].IsCustom = true;
        operators[0].IsSelectedCustom = true;
        vm.AddLocked("必带");
        bans.SetBans(["必带"]);

        operators[0].IsSelectedCustom = false;
        vm.NotifyOperatorSelectionChanged();
        bans.SetBans([]);

        vm.LockedOperators.Should().BeEmpty();
        vm.ActiveLockedCount.Should().Be(0);
    }

    [Fact]
    public void PlannerStillRejectsANonexistentLockedName()
    {
        var (vm, _, _, planner) = Create();
        var result = planner.Plan(new PlannerRequest
        {
            Goals = vm.BuildRequest().Goals,
            LockedOperators = new HashSet<string> { "不存在的干员" }
        });

        result.Error.Should().Contain("不存在的干员");
        result.AllLineups.Should().BeEmpty();
    }

    private static (LineupPlannerViewModel Vm, BanViewModel Bans, List<Operator> Operators, LineupPlanner Planner) Create()
    {
        var operators = new List<Operator> { new() { Name = "必带", CoreCovenants = ["炎"] } };
        operators.AddRange(Enumerable.Range(1, 9).Select(i => new Operator { Name = $"候选{i}", CoreCovenants = ["维多利亚"] }));
        CovenantInfo[] covenants = [
            new() { Name = "炎", BondType = "SEASON", ActivateCount = 1 },
            new() { Name = "维多利亚", BondType = "SEASON", ActivateCount = 1 },
            new() { Name = "谢拉格", BondType = "SEASON", ActivateCount = 1 },
            new() { Name = "精准", BondType = "REGULAR", ActivateCount = 2 },
            new() { Name = "奥术", BondType = "REGULAR", ActivateCount = 2 }];
        var planner = new LineupPlanner(operators, covenants);
        var bans = new BanViewModel();
        var vm = new LineupPlannerViewModel(planner, operators, bans, EquipmentTestData.Rules) { DebounceMilliseconds = 0 };
        vm.AllCovenants.Single(c => c.Name == "维多利亚").ToggleLevel1Command.Execute(null);
        return (vm, bans, operators, planner);
    }
}
