// Modified for ASP Assistant Arknights on 2026-11-11; see NOTICE.
using System.Buffers.Binary;
using HongxueAssistant.Core.Data;
using HongxueAssistant.Core.GameModes.GarrisonProtocol;
using HongxueAssistant.Core.Models;
using HongxueAssistant.Core.Services;
using FluentAssertions;

namespace HongxueAssistant.Tests.Services;

[Collection("Native OCR")]
public sealed class MuMuInformationLayoutTests : IDisposable
{
    private readonly MaaOcrEngine _ocr = new(Path.Combine(AppContext.BaseDirectory, "data"));

    [Fact]
    public async Task NativeScrollPassDoesNotPublishUntilHiddenEntriesAndCoreConfirmationAreComplete()
    {
        var top = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "TestData", "mumu-opening-grouped-1919x1080.png"));
        // Constructed from the supplied top and overview images to emulate a scrolled left panel.
        var lower = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "TestData", "mumu-scrolled-synthetic-1919x1080.png"));
        var frame = top;
        var store = new JsonDataStore(Path.Combine(AppContext.BaseDirectory, "data"));
        var (covenants, _) = await store.LoadCovenantsAsync();
        var strategy = new GarrisonOcrStrategy();
        var bosses = new BossDataService();
        await bosses.LoadAsync(Path.Combine(AppContext.BaseDirectory, "data"));
        var analyzer = new BanScreenAnalyzer(() => Task.FromResult<byte[]?>(frame), _ocr, strategy,
            new EnemyRecognizer(_ocr, strategy, bosses), await store.LoadOperatorsAsync(),
            covenants.Where(c => c.BondType == "SEASON").Select(c => c.Name).ToList(),
            covenants.Where(c => c.BondType != "SEASON").Select(c => c.Name).ToList());
        var published = new List<BanScreenResult>();
        var bans = new List<IReadOnlyList<string>>();
        string? message = null;
        analyzer.AnalysisCompleted += published.Add;
        analyzer.AutoBanListComputed += bans.Add;
        analyzer.AnalysisFailed += m => message = m;
        await analyzer.RunAnalysisAsync();
        published.Should().BeEmpty();
        bans.Should().BeEmpty();
        message.Should().Contain("10/16");
        frame = lower;
        await analyzer.RunAnalysisAsync();
        published.Should().BeEmpty();
        bans.Should().BeEmpty();
        message.Should().Contain("向上滚回");
        frame = top;
        await analyzer.RunAnalysisAsync();
        published.Should().ContainSingle();
        published[0].CoreCovenants.Should().HaveCount(10);
        published[0].AdditionalCovenants.Should().HaveCount(16);
        published[0].SelectsCoreGroup.Should().BeTrue();
        published[0].AdditionalCovenants.Where(c => c.IsBanned).Select(c => c.Name)
            .Should().BeEquivalentTo("精准", "迅捷", "坚守", "奇迹", "奥术");
        bans.Should().ContainSingle();
    }

    [Theory]
    [InlineData("mumu-ingame-1919x1080.png", false, 16)]
    [InlineData("mumu-opening-grouped-1919x1080.png", true, 10)]
    public async Task RealScreensReadBothCoreRowsAndEveryVisibleAdditionalCovenant(string file, bool opening, int visibleAdditional)
    {
        var png = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "TestData", file));
        var width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4));
        var strategy = new GarrisonOcrStrategy();
        var detector = new InformationScreenDetector(_ocr, strategy);
        (await detector.IsInformationScreenAsync(png, width, height)).Should().BeTrue();
        (await detector.IsOpeningScreenAsync(png, width, height)).Should().Be(opening);
        var store = new JsonDataStore(Path.Combine(AppContext.BaseDirectory, "data"));
        var (covenants, _) = await store.LoadCovenantsAsync();
        var read = await BanCovenantReader.ReadAsync(_ocr, strategy, png, width, height,
            covenants.Where(c => c.BondType == "SEASON").Select(c => c.Name).ToList(),
            covenants.Where(c => c.BondType != "SEASON").Select(c => c.Name).ToList());
        read.SawCoreHeader.Should().BeTrue();
        read.SawAdditionalHeader.Should().BeTrue();
        read.Core.Should().HaveCount(10);
        read.Core.Select(c => c.Name).Should().OnlyHaveUniqueItems();
        read.Core.Where(c => c.IsBanned).Select(c => c.Name).Should().BeEquivalentTo(
            "萨尔贡", "谢拉格", "卡西米尔", "乌萨斯", "莱塔尼亚");
        read.Additional.Should().HaveCount(visibleAdditional);
        read.UnnamedCore.Should().BeEmpty();
        read.UnnamedAdditional.Should().BeEmpty();
        if (opening) read.Additional.Should().OnlyContain(c => !c.IsBanned);
        else read.Additional.Where(c => c.IsBanned).Select(c => c.Name).Should().BeEquivalentTo(
            "精准", "迅捷", "坚守", "奇迹", "奥术");

        var bosses = new BossDataService();
        await bosses.LoadAsync(Path.Combine(AppContext.BaseDirectory, "data"));
        var analyzer = new BanScreenAnalyzer(() => Task.FromResult<byte[]?>(png), _ocr, strategy,
            new EnemyRecognizer(_ocr, strategy, bosses), await store.LoadOperatorsAsync(),
            covenants.Where(c => c.BondType == "SEASON").Select(c => c.Name).ToList(),
            covenants.Where(c => c.BondType != "SEASON").Select(c => c.Name).ToList());
        string? failure = null;
        BanScreenResult? recorded = null;
        EnemyResult? enemy = null;
        IReadOnlyList<string>? autoBans = null;
        analyzer.AnalysisFailed += message => failure = message;
        analyzer.AnalysisCompleted += result => recorded = result;
        analyzer.EnemyDetected += result => enemy = result;
        analyzer.AutoBanListComputed += result => autoBans = result;
        await analyzer.RunAnalysisAsync();
        if (opening)
        {
            failure.Should().Contain("10/16");
            recorded.Should().BeNull("six hidden covenants must never be assumed available");
            analyzer.HasPendingRead.Should().BeTrue();
            return;
        }
        failure.Should().BeNull();
        recorded.Should().NotBeNull();
        recorded!.CoreCovenants.Should().HaveCount(10);
        recorded.AdditionalCovenants.Should().HaveCount(visibleAdditional);
        enemy.Should().NotBeNull();
        enemy!.BossName.Should().Contain("萨米的意志");
        enemy.EnemyTypes.Should().BeEquivalentTo("特训敌人·持续", "特训敌人·元素");
        autoBans.Should().Contain("霍尔海雅", "both 奥术 and 精准 are disabled in this real overview image");
    }

    public void Dispose() => _ocr.Dispose();
}
