using HongxueAssistant.Core.Models;
using HongxueAssistant.Core.Services;
using HongxueAssistant.Core.ViewModels;
using FluentAssertions;

namespace HongxueAssistant.Tests.ViewModels;

public class BossHpTests
{
    [Fact]
    public void ShopModeAndOpeningDifficultyUseTheSameHpSelection()
    {
        var vm = new EnemyViewModel();
        var boss = new BossInfo
        {
            Hp = [new() { Name = "规程等级7", Group = "single", Hp = 400000 },
                  new() { Name = "极速模拟", Group = "single", Hp = 100000 },
                  new() { Name = "标准模拟", Group = "multi", Hp = 200000 }]
        };
        vm.Update(new("假想敌：胄", [], []), boss, []);
        vm.CurrentMode = "规程等级7";
        vm.RegulationLevel.Should().Be(7);
        vm.HasCurrentBossHp.Should().BeTrue();
        vm.OtherBossHpSingle.Select(cell => cell.Name).Should().Equal("极速模拟");
        vm.OtherBossHpExpanded = true;
        vm.CurrentMode = "规程等级7";
        vm.OtherBossHpExpanded.Should().BeTrue();
        vm.CurrentMode = "标准模拟";
        vm.RegulationLevel.Should().BeNull();
        vm.HasCurrentBossHp.Should().BeFalse();
        vm.OtherBossHpExpanded.Should().BeTrue();
        vm.BossHpMulti.Single().IsCurrent.Should().BeTrue();
        vm.Update(new("假想敌：胄", [], []), boss, []);
        vm.CurrentMode.Should().Be("标准模拟");
        vm.BossHpMulti.Single().IsCurrent.Should().BeTrue();
        vm.CurrentMode = "极速模拟";
        vm.CurrentBossHp!.Hp.Should().Be("10 万");
        vm.OtherBossHpExpanded.Should().BeFalse();
        vm.RegulationLevel = 7;
        vm.CurrentMode.Should().Be("规程等级7");
        vm.CurrentBossHp!.Hp.Should().Be("40 万");
        vm.RegulationLevel = null;
        vm.CurrentMode.Should().BeNull();
        vm.BossHpMulti.Single().IsCurrent.Should().BeFalse();
    }

    [Fact]
    public void RecognizedDifficultyShowsItsHpAndCollapsesOnlyTheOtherLevels()
    {
        var vm = new EnemyViewModel { RegulationLevel = 7 };
        var boss = new BossInfo
        {
            Hp = [new() { Name = "规程等级7", Group = "single", Hp = 400000, HiddenHp = 800000 },
                  new() { Name = "规程等级8", Group = "single", Hp = 500000 }]
        };
        vm.Update(new("假想敌：胄", ["特训敌人·元素"], []), boss, [new()]);
        vm.CurrentBossHp.Should().Be(new BossHpCell("7", "规程等级7", "40 万", "80 万", true));
        vm.OtherBossHpExpanded.Should().BeFalse();
        vm.OtherBossHpSingle.Select(cell => cell.Label).Should().Equal("8");
        vm.OtherBossHpExpanded = true;
        vm.RegulationLevel = 8;
        vm.CurrentBossHp!.Hp.Should().Be("50 万");
        vm.OtherBossHpExpanded.Should().BeFalse();
        vm.OtherBossHpSingle.Select(cell => cell.Label).Should().Equal("7");
        vm.RegulationLevel = null;
        vm.HasCurrentBossHp.Should().BeFalse();
        vm.OtherBossHpExpanded.Should().BeTrue();
        vm.OtherBossHpSingle.Should().HaveCount(2);
    }

    [Fact]
    public void ManualDifficultyReplacesTheCurrentLevelAndIncompleteEnemyRetryKeepsIt()
    {
        var vm = new EnemyViewModel { RegulationLevel = 8 };
        var boss = new BossInfo { Hp = [new() { Name = "规程等级7", Group = "single", Hp = 400000 }] };
        vm.Update(new("假想敌：胄", ["特训敌人·元素"], []) { RegulationLevel = 7 }, boss, [new()]);
        vm.RegulationLevel.Should().Be(7);
        vm.HasCurrentBossHp.Should().BeTrue();
        vm.Update(new("", [], []), null, []);
        vm.CurrentBossHp!.Hp.Should().Be("40 万");
        vm.RegulationLevel.Should().Be(7);
        vm.Update(new("假想敌：胄", ["特训敌人·元素"], []), boss, [new()]);
        vm.HasCurrentBossHp.Should().BeTrue("a missed difficulty label must keep this run's confirmed level");
    }

    [Fact]
    public void MissingHpForTheDetectedLevelKeepsTheFullTableOpen()
    {
        var vm = new EnemyViewModel();
        vm.Update(new("假想敌：胄", [], []) { RegulationLevel = 7 },
            new() { Hp = [new() { Name = "规程等级1", Group = "single", Hp = 100000 }] }, []);
        vm.HasCurrentBossHp.Should().BeFalse();
        vm.OtherBossHpExpanded.Should().BeTrue();
        vm.OtherBossHpSingle.Should().ContainSingle();
    }

    [Theory]
    [InlineData(283680, "28.4 万")]
    [InlineData(1182000, "118 万")]
    [InlineData(100000, "10 万")]
    [InlineData(9500, "9500")]
    public void HpIsShownInTenThousands(long hp, string text) => BossHpCell.FormatHp(hp).Should().Be(text);

    [Theory]
    [InlineData("规程等级12", "12")]
    [InlineData("极速模拟", "极速")]
    [InlineData("绝境模拟", "绝境")]
    public void LabelsAreShortened(string name, string label) => BossHpCell.ShortLabel(name).Should().Be(label);

    [Fact]
    public async Task EveryBossHasEachDifficulty_AndHiddenFormsCarryTheirOwnHp()
    {
        var bosses = new BossDataService();
        await bosses.LoadAsync(Path.Combine(AppContext.BaseDirectory, "TestData", "Regulation"));
        var stmk = bosses.Lookup("假想敌：胄")!;
        stmk.Hp.Should().HaveCount(25);
        stmk.Hp.Count(l => l.Group == "single").Should().Be(21);
        stmk.Hp.Should().Contain(l => l.Mode == "mode_single_1" && l.Hp == 283680 && l.HiddenHp == 492480);
        stmk.Hp.Select(l => l.Name).Should().StartWith(["极速模拟", "规程等级1"]);
        stmk.Hp.TakeLast(4).Select(l => l.Name).Should().Equal("标准模拟", "险境模拟", "绝境模拟", "终极模拟");
    }

    [Fact]
    public void UpdateSplitsSingleAndMultiAndClearResets()
    {
        var boss = new BossInfo
        {
            Name = "假想敌：胄",
            Hp =
            [
                new() { Mode = "mode_single_1", Name = "规程等级1", Group = "single", Hp = 283680, HiddenHp = 492480 },
                new() { Mode = "mode_multi_funny", Name = "标准模拟", Group = "multi", Hp = 283680 },
            ],
        };
        var vm = new EnemyViewModel();
        vm.Update(new("假想敌：胄", [], []), boss, []);
        vm.BossHpSingle.Should().ContainSingle().Which.Should().Be(new BossHpCell("1", "规程等级1", "28.4 万", "49.2 万"));
        vm.BossHpMulti.Should().ContainSingle().Which.Label.Should().Be("标准");
        vm.BossHasHiddenForm.Should().BeTrue();
        vm.CurrentMode = "规程等级1";
        vm.BossHpSingle.Single().IsCurrent.Should().BeTrue();
        vm.BossHpMulti.Single().IsCurrent.Should().BeFalse();
        vm.Clear();
        vm.BossHpSingle.Should().BeEmpty();
        vm.BossHasHiddenForm.Should().BeFalse();
    }
}
