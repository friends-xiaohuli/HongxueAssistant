using System.Drawing;
using System.Text.Json;
using HongxueAssistant.Core.Data;
using HongxueAssistant.Core.GameModes.GarrisonProtocol;
using HongxueAssistant.Core.Models;
using HongxueAssistant.Core.Services;
using FluentAssertions;
using FluentAssertions.Execution;
using Xunit.Abstractions;

namespace HongxueAssistant.Tests.Services;

[Collection("Native OCR")]
public class Ui90AspectRatioRecognitionTests(EnemyRecognitionFixture fixture, ITestOutputHelper output)
    : IClassFixture<EnemyRecognitionFixture>
{
    [Trait("Category", "LocalScreenshot")]
    [Theory]
    [InlineData("商店普通", false)]
    [InlineData("商店冻结", false)]
    [InlineData("金币花完商店普通", true)]
    [InlineData("金币花完商店冻结", true)]
    public async Task ShopAtUi90PreservesEconomyAndRecognizesThemeAndTerminal(string file, bool spent)
    {
        using var image = new Bitmap(Path.Combine(AppContext.BaseDirectory, "TestData", "ui90-1600", file + ".png"));
        var frame = ImageBytes.Encode(image);
        var store = new JsonDataStore(Path.Combine(AppContext.BaseDirectory, "data"));
        var operators = await store.LoadOperatorsAsync();
        var (equipment, _) = await store.LoadEquipmentAsync();
        var names = operators.Where(o => o.RequiredStrategyId.Length == 0).Select(o => o.Name)
            .Concat(equipment.Select(e => e.Name)).Distinct().ToList();
        var theme = new GameShopThemeDetector(Path.Combine(AppContext.BaseDirectory, "data", "shop_theme_refs.json")).Detect(frame);
        var terminal = await new TerminalLevelReader(fixture.Engine).ReadAsync(frame, image.Width, image.Height, GameShopTheme.Closure);
        var economy = await new ShopEconomyReader(fixture.Engine).ReadAsync(frame, image.Width, image.Height);
        var items = await new ShopReader(fixture.Engine, names) { Theme = GameShopTheme.Closure }
            .ReadAsync(frame, image.Width, image.Height, names);
        output.WriteLine(JsonSerializer.Serialize(new { image.Width, image.Height, theme, terminal, economy, items = items.Select(i => i.Name) }));
        using var assertions = new AssertionScope();
        theme.Should().Be(GameShopTheme.Closure);
        terminal.Should().Be(spent ? 4 : 5);
        economy.Round.Should().Be(spent ? 10 : null); // ?? is the hidden-boss round.
        economy.Mode.Should().Be("终极模拟");
        economy.Money.Should().Be(spent ? 0 : 15);
        economy.UpgradeCost.Should().Be(spent ? 12 : 9);
        items.Select(i => i.Name).Should().Equal(spent ? ["可靠怀表", "山", "至简", "蛇屠箱"]
            : new[] { "蜂鸣器", "锡人", "仇白", "Mon3tr", "W", "怒潮凛冬" });
        items.Select(i => i.Id).Should().OnlyHaveUniqueItems();
    }

    [Trait("Category", "LocalScreenshot")]
    [Theory]
    [InlineData("本局信息", true)]
    [InlineData("局内信息", false)]
    public async Task InformationPageIncludesLeaderAboveFortyPercentHeight(string file, bool opening)
    {
        using var image = new Bitmap(Path.Combine(AppContext.BaseDirectory, "TestData", "ui90-1600", file + ".png"));
        var frame = ImageBytes.Encode(image);
        var strategy = new GarrisonOcrStrategy();
        var detector = new InformationScreenDetector(fixture.Engine, strategy);
        (await detector.IsOpeningScreenAsync(frame, image.Width, image.Height)).Should().Be(opening);
        (await detector.IsInformationScreenAsync(frame, image.Width, image.Height)).Should().BeTrue();
        var store = new JsonDataStore(Path.Combine(AppContext.BaseDirectory, "data"));
        var (covenants, _) = await store.LoadCovenantsAsync();
        var bans = await BanCovenantReader.ReadAsync(fixture.Engine, strategy, frame, image.Width, image.Height,
            covenants.Where(c => c.BondType == "SEASON").Select(c => c.Name).ToList(),
            covenants.Where(c => c.BondType != "SEASON").Select(c => c.Name).ToList());
        bans.Core.Should().HaveCount(10);
        bans.Additional.Should().HaveCount(16);
        bans.Core.Where(c => c.IsBanned).Select(c => c.Name).Should().BeEquivalentTo("维多利亚", "谢拉格", "阿戈尔", "叙拉古", "卡西米尔");
        bans.Additional.Where(c => c.IsBanned).Select(c => c.Name).Should().BeEquivalentTo("远见", "奇迹", "不屈", "聚合", "奥术");
        var enemy = await new EnemyRecognizer(fixture.Engine, strategy, fixture.Bosses).RecognizeAsync(frame, image.Width, image.Height);
        output.WriteLine(JsonSerializer.Serialize(enemy));
        enemy.Should().NotBeNull();
        enemy!.BossName.Should().Be("假想敌：胄");
        fixture.Bosses.Lookup(enemy.BossName).Should().NotBeNull();
        enemy.EnemyTypes.Should().BeEquivalentTo("特训敌人·飞行", "特训敌人·折射", "特训敌人·致命");
    }
}
