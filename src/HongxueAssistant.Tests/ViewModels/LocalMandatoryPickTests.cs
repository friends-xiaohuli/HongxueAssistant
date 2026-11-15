using HongxueAssistant.Core.Models;
using HongxueAssistant.Core.Services;
using HongxueAssistant.Core.ViewModels;
using FluentAssertions;

namespace HongxueAssistant.Tests.ViewModels;

public class LocalMandatoryPickTests
{
    private static Operator Op(string name, params string[] core) => new() { Name = name, CoreCovenants = [.. core], IconPath = $"icons/operators/{name}.png" };

    private static (LineupPlannerViewModel Vm, BanViewModel Bans) Create()
    {
        var operators = new List<Operator>();
        for (var i = 1; i <= 6; i++) operators.Add(Op($"炎{i}", "炎"));
        for (var i = 1; i <= 6; i++) operators.Add(Op($"维{i}", "维多利亚"));
        operators.Add(new Operator { Name = "炎突", CoreCovenants = ["炎"], AdditionalCovenants = ["突袭"] });
        operators.Add(new Operator { Name = "维突", CoreCovenants = ["维多利亚"], AdditionalCovenants = ["突袭"] });
        var covenants = new[]
        {
            new CovenantInfo { Name = "炎", BondType = "SEASON", ActivateCount = 3 },
            new CovenantInfo { Name = "维多利亚", BondType = "SEASON", ActivateCount = 3 },
            new CovenantInfo { Name = "独行", BondType = "REGULAR", ActivateCount = 1 },
            new CovenantInfo { Name = "突袭", BondType = "REGULAR", ActivateCount = 2 },
        };
        var bans = new BanViewModel();
        var vm = new LineupPlannerViewModel(new LineupPlanner(operators, covenants), operators, bans, EquipmentTestData.Rules) { DebounceMilliseconds = 0 };
        return (vm, bans);
    }

    private static PlannerCovenantItem Item(LineupPlannerViewModel vm, string name) => vm.AllCovenants.Single(c => c.Name == name);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BansSuspendConflictingLocksAndRestoreThemWhenCleared(bool manual)
    {
        var (vm, bans) = Create();
        vm.IsActive = true;
        Item(vm, "炎").ToggleLevel1Command.Execute(null);
        vm.AddLocked("炎1");
        vm.AddLocked("维3");
        await vm.PendingSearch!;

        if (manual) bans.ToggleBan("炎1");
        else bans.SetBans(["炎1"]);
        await vm.PendingSearch!;

        vm.LockedOperators.Select(o => o.Name).Should().Equal("炎1", "维3");
        vm.LockedOperators.Single(o => o.Name == "炎1").IsUnavailable.Should().BeTrue();
        vm.ActiveLockedCount.Should().Be(1);
        vm.BuildRequest().LockedOperators.Should().Equal("维3");
        vm.HasError.Should().BeFalse();
        vm.VisibleLineups.Should().NotBeEmpty();
        vm.VisibleLineups.Should().OnlyContain(l =>
            l.Operators.All(o => o.Name != "炎1") && l.Operators.Any(o => o.Name == "维3" && o.IsLocked));

        if (manual) bans.ToggleBan("炎1");
        else bans.SetBans([]);
        await vm.PendingSearch!;

        vm.LockedOperators.Should().OnlyContain(o => !o.IsUnavailable);
        vm.ActiveLockedCount.Should().Be(2);
        vm.BuildRequest().LockedOperators.Should().BeEquivalentTo("炎1", "维3");
        vm.VisibleLineups.Should().NotBeEmpty();
        vm.VisibleLineups.Should().OnlyContain(l => l.Operators.Any(o => o.Name == "炎1" && o.IsLocked)
            && l.Operators.Any(o => o.Name == "维3" && o.IsLocked));
    }

    [Fact]
    public void UpstreamCanPreselectBannedOperatorsWithoutCountingThemInThisRun()
    {
        var (vm, bans) = Create();
        vm.LockSearchText = "炎";
        bans.SetBans(["炎1"]);
        vm.LockSuggestions.Should().Contain(o => o.Name == "炎1");
        vm.AddLocked("炎1").Should().BeTrue();
        vm.LockedOperators.Should().ContainSingle().Which.IsUnavailable.Should().BeTrue();
        vm.BuildRequest().LockedOperators.Should().BeEmpty();
        vm.TrackedOperatorsProvider = () => ["炎1", "维3"];
        vm.ImportTrackedCommand.Execute(null);
        vm.LockedOperators.Select(o => o.Name).Should().Equal("炎1", "维3");
        vm.BuildRequest().LockedOperators.Should().Equal("维3");
        bans.ClearBans();
        vm.ActiveLockedCount.Should().Be(2);
    }

    [Fact]
    public void CovenantBansSuspendOnlyUnavailableLockedOperators()
    {
        var (vm, bans) = Create();
        vm.AddLocked("炎1");
        vm.AddLocked("炎突");
        vm.AddLocked("维3");

        bans.SetCovenantBans(["炎"]);

        vm.LockedOperators.Select(o => o.Name).Should().Equal("炎1", "炎突", "维3");
        vm.LockedOperators.Single(o => o.Name == "炎1").IsUnavailable.Should().BeTrue();
        vm.BuildRequest().LockedOperators.Should().BeEquivalentTo("维3");
        vm.ActiveLockedCount.Should().Be(1);
        vm.AddLocked("炎1").Should().BeFalse();
        bans.SetCovenantBans(["炎", "突袭"]);
        vm.LockedOperators.Should().HaveCount(3);
        vm.LockedOperators.Single(o => o.Name == "炎突").IsUnavailable.Should().BeTrue();
        vm.BuildRequest().LockedOperators.Should().Equal("维3");
        vm.ActiveLockedCount.Should().Be(1);
        vm.AddLocked("炎突").Should().BeFalse();
        bans.SetCovenantBans([]);
        vm.BuildRequest().LockedOperators.Should().BeEquivalentTo("炎1", "炎突", "维3");
        vm.LockedOperators.Should().OnlyContain(o => !o.IsUnavailable);
    }

    [Fact]
    public void BanningTheLastLockedOperatorPreservesTheChipButNotTheActiveCount()
    {
        var (vm, bans) = Create();
        vm.AddLocked("炎1");
        vm.HasLocked.Should().BeTrue();
        var activeCountNotified = false;
        vm.PropertyChanged += (_, e) => activeCountNotified |= e.PropertyName == nameof(vm.ActiveLockedCount);

        bans.SetBans(["炎1"]);

        vm.LockedOperators.Should().ContainSingle().Which.IsUnavailable.Should().BeTrue();
        vm.HasLocked.Should().BeTrue();
        vm.ActiveLockedCount.Should().Be(0);
        vm.ActiveLockedCount.Should().Be(0);
        activeCountNotified.Should().BeTrue();
        vm.BuildRequest().LockedOperators.Should().BeEmpty();
        bans.ClearBans();
        vm.LockedOperators.Should().ContainSingle().Which.IsUnavailable.Should().BeFalse();
        vm.BuildRequest().LockedOperators.Should().Equal("炎1");
        vm.ActiveLockedCount.Should().Be(1);
        vm.ActiveLockedCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void UpstreamLimitsTheRetainedListWhileBannedPicksDoNotCountInTheRequest()
    {
        var (vm, bans) = Create();
        foreach (var name in Enumerable.Range(1, 6).Select(i => $"炎{i}")
                     .Concat(Enumerable.Range(1, 3).Select(i => $"维{i}")))
            vm.AddLocked(name).Should().BeTrue();
        bans.SetBans(["炎1"]);

        vm.AddLocked("维4").Should().BeFalse();
        vm.LockedOperators.Should().HaveCount(9);
        vm.ActiveLockedCount.Should().Be(8);
        vm.BuildRequest().LockedOperators.Should().HaveCount(8).And.NotContain("炎1");
        vm.AddLocked("维5").Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitlyRemovingASuspendedPickPreventsItsRestoration(bool clearAll)
    {
        var (vm, bans) = Create();
        vm.AddLocked("炎1");
        bans.SetBans(["炎1"]);
        if (clearAll) vm.ClearLockedCommand.Execute(null);
        else vm.LockedOperators.Single().RemoveCommand.Execute(null);
        bans.ClearBans();

        vm.LockedOperators.Should().BeEmpty();
        vm.HasLocked.Should().BeFalse();
        vm.ActiveLockedCount.Should().Be(0);
        vm.BuildRequest().LockedOperators.Should().BeEmpty();
    }

}
