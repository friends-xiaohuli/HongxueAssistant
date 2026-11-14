// Modified for ASP Assistant Arknights on 2026-11-12; see NOTICE.
using HongxueAssistant.Core.Models;
using HongxueAssistant.Core.Services;
using HongxueAssistant.Core.ViewModels;
using FluentAssertions;

namespace HongxueAssistant.Tests.Services;

public sealed class InformationReadSessionTests
{
    private static readonly string[] Core = Enumerable.Range(0, 6).Select(i => $"核心{i}").ToArray();
    private static readonly string[] Additional = Enumerable.Range(0, 4).Select(i => $"附加{i}").ToArray();
    private static InformationReadSession Session() => new([new("test", Core, Additional)]);
    private static BanCovenantReadResult Read(int additional = 4, bool coreVisible = true, bool selectedGroup = true)
        => new(coreVisible ? Core.Select((n, i) => new CovenantBanState(n, i >= 3, i)).ToList() : [],
            Additional.Take(additional).Select((n, i) => new CovenantBanState(n, i >= 2, i)).ToList(), [], [],
            CoreSlots: coreVisible ? 6 : 0, AdditionalSlots: additional,
            SawCoreHeader: coreVisible, SawAdditionalHeader: true, SelectsCoreGroup: selectedGroup);

    [Fact]
    public void VisibleSlotsAreNotProofThatTheWholePoolWasRead()
    {
        var session = Session();
        session.TryComplete(Read(2), 1, "boss", out _, out var reason).Should().BeFalse();
        reason.Should().Contain("2/4").And.Contain("向下滚动");
        session.IsPending.Should().BeTrue();
    }

    [Fact]
    public void ScrollCompletesAdditionalButRequiresTheCoreSelectionToBeConfirmedAgain()
    {
        var session = Session();
        session.TryComplete(Read(2), 1, "boss", out _, out _).Should().BeFalse();
        session.TryComplete(Read(coreVisible: false), 1, "boss", out _, out var reason).Should().BeFalse();
        reason.Should().Contain("向上滚回");
        session.TryComplete(Read(2), 1, "boss", out var complete, out _).Should().BeTrue();
        complete.Core.Should().HaveCount(6);
        complete.Additional.Should().HaveCount(4);
        complete.Additional.Where(c => c.IsBanned).Select(c => c.Name).Should().Equal("附加2", "附加3");
        session.IsPending.Should().BeFalse();
    }

    [Theory]
    [InlineData(2, "boss")]
    [InlineData(1, "another-boss")]
    public void AnotherSourceOrEnemyCannotCompleteThePreviousScrollPass(long source, string enemy)
    {
        var session = Session();
        session.TryComplete(Read(2), 1, "boss", out _, out _);
        session.TryComplete(Read(coreVisible: false), source, enemy, out _, out _).Should().BeFalse();
        session.IsPending.Should().BeFalse();
    }

    [Fact]
    public void ChangingCoreGroupsDiscardsAdditionalDataFromThePreviousChoice()
    {
        var session = Session();
        session.TryComplete(Read(2), 1, "boss", out _, out _);
        session.TryComplete(Read(coreVisible: false), 1, "boss", out _, out _);
        var changed = Read(2) with { Core = Core.Select((n, i) => new CovenantBanState(n, i < 3, i)).ToList() };
        session.TryComplete(changed, 1, "boss", out _, out var reason).Should().BeFalse();
        reason.Should().Contain("2/4");
    }

    [Fact]
    public void UnknownPoolsAndUnnamedIconsCannotPublish()
    {
        var session = Session();
        var unknown = Read() with { Core = Enumerable.Range(10, 6).Select(i => new CovenantBanState($"未知{i}", false, i)).ToList() };
        session.TryComplete(unknown, 1, "boss", out _, out var reason).Should().BeFalse();
        reason.Should().Contain("未登记").And.Contain("仅供预览");
        var unnamed = Read() with { UnnamedAdditional = [new(1, 1, true)] };
        session.TryComplete(unnamed, 1, "boss", out _, out _).Should().BeFalse();
        var mismatched = Read() with { Additional = [new("其他模式项", false, 0)] };
        session.TryComplete(mismatched, 1, "boss", out _, out reason).Should().BeFalse();
        reason.Should().Contain("模式池不一致");
    }

    [Fact]
    public void CompleteSingleFrameNeedsNoScrolling()
    {
        Session().TryComplete(Read(), 1, "boss", out var complete, out _).Should().BeTrue();
        complete.Additional.Should().HaveCount(4);
    }

    [Fact]
    public void FrameMissingRowsDoesNotInvalidateACompletedScrollPass()
    {
        var session = Session();
        var top = Read(2) with { HasIncompleteAdditionalRows = true };
        session.TryComplete(top, 1, "boss", out _, out _).Should().BeFalse();
        var lower = Read(coreVisible: false) with { HasIncompleteAdditionalRows = true, HasIncompleteCoreRows = true };
        session.TryComplete(lower, 1, "boss", out _, out _).Should().BeFalse();
        session.TryComplete(top, 1, "boss", out var complete, out _).Should().BeTrue();
        complete.HasIncompleteRows.Should().BeFalse();
        complete.SelectsCoreGroup.Should().BeTrue();
        complete.Additional.Should().HaveCount(4);
    }

    [Fact]
    public void MissingCoreRowsCannotConfirmAnEarlierScrollPass()
    {
        var session = Session();
        session.TryComplete(Read(2), 1, "boss", out _, out _);
        session.TryComplete(Read(coreVisible: false), 1, "boss", out _, out _);
        var missingCore = Read() with { HasIncompleteAdditionalRows = true, HasIncompleteCoreRows = true };
        session.TryComplete(missingCore, 1, "boss", out _, out var reason).Should().BeFalse();
        reason.Should().Contain("向上滚回");
    }

    [Fact]
    public void UnknownCorePoolDiscardsPreviouslyAccumulatedEntries()
    {
        var session = Session();
        session.TryComplete(Read(2), 1, "boss", out _, out _);
        session.TryComplete(Read(coreVisible: false), 1, "boss", out _, out _);
        var unknown = Read() with { Core = Core.Select(n => new CovenantBanState(n + "新", false, 0)).ToList() };
        session.TryComplete(unknown, 1, "boss", out _, out _).Should().BeFalse();
        session.IsPending.Should().BeFalse();
        session.TryComplete(Read(2), 1, "boss", out _, out var reason).Should().BeFalse();
        reason.Should().Contain("2/4");
    }

    [Fact]
    public void DuplicateAdditionalNamesCannotSupplyMissingPoolEntries()
    {
        var read = Read() with { Additional = [new("附加0", false, 0), new("附加0", false, 1)], AdditionalSlots = 2 };
        Session().TryComplete(read, 1, "boss", out _, out var reason).Should().BeFalse();
        reason.Should().Contain("重复");
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void UnselectedCoreGroupsAndOrdinaryIncompleteRostersHaveDifferentBanRules(bool grouped, bool expectedBan)
    {
        var op = new Operator { Name = "核心禁用但附加可用", CoreCovenants = ["炎"], AdditionalCovenants = ["精准"] };
        CovenantBanRules.IsBanned(op, new HashSet<string> { "炎" }, new HashSet<string>(), grouped).Should().Be(expectedBan);
    }

    [Fact]
    public void OrdinaryRosterIntersectionStillBansWhenCoreAndAdditionalAreBothDisabled()
    {
        var op = new Operator { CoreCovenants = ["炎"], AdditionalCovenants = ["精准"] };
        CovenantBanRules.IsBanned(op, new HashSet<string> { "炎" }, new HashSet<string> { "精准" }, false).Should().BeTrue();
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void OperatorsWithoutCoreAreBannedWhenAllTheirAdditionalAffiliationsAreDisabled(bool grouped, bool expected)
    {
        var op = new Operator { Name = "霍尔海雅", AdditionalCovenants = ["奥术", "精准"] };
        CovenantBanRules.IsBanned(op, new HashSet<string>(), new HashSet<string> { "奥术", "精准" }, grouped).Should().Be(expected);
        CovenantBanRules.IsBanned(op, new HashSet<string>(), new HashSet<string> { "奥术" }, grouped).Should().BeFalse();
    }

    [Fact]
    public void ManualBanAndManualUnbanSurviveTheNextAutomaticResult()
    {
        var vm = new BanViewModel();
        vm.SetBans(["自动禁用"]);
        vm.SetManualBan("自动禁用", false);
        vm.SetManualBan("手动禁用", true);
        vm.SetBans(["自动禁用"]);
        vm.IsBanned("自动禁用").Should().BeFalse();
        vm.IsBanned("手动禁用").Should().BeTrue();
        vm.ClearBans();
        vm.SetBans(["自动禁用"]);
        vm.IsBanned("自动禁用").Should().BeTrue();
        vm.IsBanned("手动禁用").Should().BeFalse();
    }
}
