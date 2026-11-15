using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using HongxueAssistant.Core.Data;
using HongxueAssistant.Core.Models;
using HongxueAssistant.Core.Services;
using FluentAssertions;
using Moq;

namespace HongxueAssistant.Tests.Services;

[Collection("Native OCR")]
public class ShopReaderTests(ShopReaderFixture fixture) : IClassFixture<ShopReaderFixture>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SingleLetterOperatorMustBeOnTheNameRow(bool realOperator)
    {
        using var bitmap = new Bitmap(1920, 1080);
        using var stream = new MemoryStream(); bitmap.Save(stream, ImageFormat.Png);
        OcrTextResult[] texts = [new("甲", (1300, 390, 60, 40)), new("乙", (1800, 390, 60, 40)),
            new("W", (1000, realOperator ? 390 : 450, 60, 40)), new("收起", (3500, 370, 160, 60))];
        var ocr = new Mock<IOcrEngine>();
        ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(texts);
        var reader = new ShopReader(ocr.Object, ["甲", "乙", "W"]);
        var cards = await reader.ReadAsync(stream.ToArray(), 1920, 1080, ["甲", "乙", "W"]);
        cards.Select(c => c.Name).Should().BeEquivalentTo(realOperator ? ["甲", "乙", "W"] : new[] { "甲", "乙" });
    }

    [Theory]
    [InlineData("shop-tier2-color.png", GameShopTheme.Closure)]
    [InlineData("shop-frontier-tier1-color.png", GameShopTheme.Ursus)]
    [InlineData("shop-terminal-tier1-color.png", GameShopTheme.Terminal)]
    public async Task CompleteFirstPassUsesOnlyOneShopOcr(string file, GameShopTheme theme)
    {
        var ocr = ForwardingEngine();
        var reader = new ShopReader(ocr.Object, fixture.Names) { Theme = theme };
        var bytes = File.ReadAllBytes(fixture.Sample(file));
        var cards = await reader.ReadAsync(bytes, 1920, 1080, fixture.Names);
        cards.Should().NotBeEmpty();
        ocr.Verify(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), 0, 0, 3840, 540), Times.Once);
    }

    [Fact]
    public async Task FirstPassMissingAnOccupiedSlotStillRunsRecovery()
    {
        var ocr = ForwardingEngine();
        var passes = 0;
        ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), 0, 0, 3840, 540))
            .Returns(async (byte[] bytes, int x, int y, int w, int h) =>
            {
                var texts = await fixture.Engine.RecognizeRegionAsync(bytes, x, y, w, h);
                return ++passes == 1 ? texts.Where(t => !t.Text.Contains("慕斯")).ToArray() : texts;
            });
        var reader = new ShopReader(ocr.Object, fixture.Names) { Theme = GameShopTheme.Terminal };
        var cards = await reader.ReadAsync(File.ReadAllBytes(fixture.Sample("shop-terminal-tier1-color.png")), 1920, 1080, fixture.Names);
        cards.Select(c => c.Name).Should().Equal("虹彩活血饮料", "卡达", "慕斯", "角峰");
        passes.Should().Be(2);
    }

    [Theory]
    [InlineData("shop-tier2-color.png", GameShopTheme.Closure)]
    [InlineData("shop-frontier-tier1-color.png", GameShopTheme.Ursus)]
    [InlineData("shop-terminal-tier1-color.png", GameShopTheme.Terminal)]
    public async Task LightweightRecheckRereadsDynamicNamesAndRejectsRefreshesAndMissingControl(string file, GameShopTheme theme)
    {
        var ocr = ForwardingEngine();
        var reader = new ShopReader(ocr.Object, fixture.Names) { Theme = theme };
        var bytes = File.ReadAllBytes(fixture.Sample(file));
        var cards = await reader.ReadAsync(bytes, 1920, 1080, fixture.Names);
        ocr.Invocations.Clear();
        (await reader.IsSameOpenShopAsync(bytes, bytes, cards)).Should().BeTrue();
        ocr.Verify(e => e.RecognizeLineAsync(It.IsAny<byte[]>(), 0, 0, It.Is<int>(w => w < 300), It.Is<int>(h => h < 160)), Times.Once);
        ocr.Verify(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        using var stream = new MemoryStream(bytes);
        using var changed = new Bitmap(stream);
        var card = cards[0];
        int x = (int)(card.OcrRegion.X * 1920) - 2, y = (int)(card.OcrRegion.Y * 1080) + 2;
        var old = changed.GetPixel(x, y);
        changed.SetPixel(x, y, Color.FromArgb(255 - old.R, 255 - old.G, 255 - old.B));
        using var output = new MemoryStream(); changed.Save(output, ImageFormat.Png);
        ocr.Invocations.Clear();
        (await reader.IsSameOpenShopAsync(bytes, output.ToArray(), cards)).Should().BeTrue("background animation must not reject a still-identical name");
        ocr.Verify(e => e.RecognizeLineAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()), Times.Exactly(2));
        ocr.Verify(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        (await reader.IsSameOpenShopAsync(bytes, bytes, [])).Should().BeFalse("an empty whole shop may be a refresh transition");
        var replacement = cards[1];
        using (var original = new Bitmap(new MemoryStream(bytes)))
        using (var graphics = Graphics.FromImage(changed))
        {
            var target = new Rectangle((int)(card.OcrRegion.X * 1920), (int)(card.OcrRegion.Y * 1080),
                (int)Math.Ceiling(card.OcrRegion.Width * 1920), (int)Math.Ceiling(card.OcrRegion.Height * 1080));
            graphics.FillRectangle(Brushes.White, target);
            graphics.DrawImage(original, target.X, target.Y, new Rectangle((int)(replacement.OcrRegion.X * 1920),
                (int)(replacement.OcrRegion.Y * 1080), (int)Math.Ceiling(replacement.OcrRegion.Width * 1920),
                (int)Math.Ceiling(replacement.OcrRegion.Height * 1080)), GraphicsUnit.Pixel);
        }
        output.SetLength(0); changed.Save(output, ImageFormat.Png);
        (await reader.IsSameOpenShopAsync(bytes, output.ToArray(), cards)).Should().BeFalse("a new identity at the same position is a refresh");
        using (var graphics = Graphics.FromImage(changed)) graphics.FillRectangle(Brushes.Black, 1680, 790, 240, 290);
        // The missing shop control must reject the frame even before changed names are read.
        changed.SetPixel(x, y, old); output.SetLength(0); changed.Save(output, ImageFormat.Png);
        (await reader.IsSameOpenShopAsync(bytes, output.ToArray(), cards)).Should().BeFalse();
    }

    private Mock<IOcrEngine> ForwardingEngine()
    {
        var ocr = new Mock<IOcrEngine>();
        ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns((byte[] image, int x, int y, int w, int h) => fixture.Engine.RecognizeRegionAsync(image, x, y, w, h));
        ocr.Setup(e => e.RecognizeLineAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns((byte[] image, int x, int y, int w, int h) => fixture.Engine.RecognizeLineAsync(image, x, y, w, h));
        return ocr;
    }

    [Theory]
    [InlineData("shop-frontier-tier1-color.png", GameShopTheme.Ursus)]
    [InlineData("shop-terminal-tier1-color.png", GameShopTheme.Terminal)]
    public async Task LightweightRecheckSupportsSmallCapturesAndRejectsResolutionChanges(string file, GameShopTheme theme)
    {
        using var original = new Bitmap(fixture.Sample(file));
        using var small = new Bitmap(1280, 720);
        using (var graphics = Graphics.FromImage(small))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(original, new Rectangle(0, 0, 1280, 720));
        }
        using var stream = new MemoryStream(); small.Save(stream, ImageFormat.Png);
        var bytes = stream.ToArray();
        var reader = new ShopReader(fixture.Engine, fixture.Names) { Theme = theme };
        var cards = await reader.ReadAsync(bytes, 1280, 720, fixture.Names);
        (await reader.IsSameOpenShopAsync(bytes, bytes, cards)).Should().BeTrue();
        (await reader.IsSameOpenShopAsync(bytes, File.ReadAllBytes(fixture.Sample(file)), cards)).Should().BeFalse();
    }

    [Theory]
    [InlineData("shop-terminal-tier1-color.png")]
    [InlineData("shop-terminal-tier4-frozen-gray.png")]
    [InlineData("shop-frontier-tier2-frozen-gray.png")]
    [InlineData("shop-tier6-frozen.png")]
    public async Task ExistingDarkAndFrozenCardsAreNotEmptyPurchaseSlots(string file)
    {
        fixture.SelectTheme(file);
        var bytes = File.ReadAllBytes(fixture.Sample(file));
        var cards = await fixture.Reader.ReadAsync(bytes, 1920, 1080, fixture.Names);
        fixture.Reader.IsShopOpen.Should().BeTrue();
        using var bitmap = new Bitmap(fixture.Sample(file));
        cards.Should().NotBeEmpty();
        foreach (var card in cards)
            ShopPurchaseDetector.IsEmpty(bitmap, card).Should().BeFalse($"{file}: {card.Name} is visibly still on sale");
    }

    // These run the real bundled PaddleOCR models against anonymized client crops.
    [Theory]
    [InlineData("shop-tier2-color.png", "聚合急救箱|渡桥|杰克|陨星|艾丝黛尔")]
    [InlineData("shop-tier4-gray.png", "卫戍推荐信|摆渡人|Miss.Christine|奥斯塔|空弦|山")]
    [InlineData("shop-tier6-frozen.png", "奥斯塔|拉普兰德|斥罪|鸿雪")]
    [InlineData("shop-tier6-glow-a.png", "突袭手雷|可露希尔|奥斯塔|拉普兰德|斥罪|鸿雪")]
    [InlineData("shop-tier6-glow-b.png", "突袭手雷|可露希尔|奥斯塔|拉普兰德|斥罪|鸿雪")]
    [InlineData("shop-tier6-frozen-gray-glow.png", "护盾无人机|奥斯塔|水灯心|忍冬|鸿雪|空弦")]
    [InlineData("shop-frontier-tier1-color.png", "坚守盾牌|野鬃|角峰|褐果")]
    [InlineData("shop-frontier-tier2-glow.png", "紧急调度券|承曦格雷伊|幽灵鲨|角峰|慕斯")]
    [InlineData("shop-frontier-tier2-frozen-gray.png", "卫戍推荐信|砾|哈洛德|雪猎|云迹")]
    [InlineData("shop-frontier-tier4-gray-glow.png", "伊比利亚骨刺刀|卡涅利安|极光|艾雅法拉|瑕光|云迹")]
    [InlineData("shop-terminal-tier1-color.png", "虹彩活血饮料|卡达|慕斯|角峰")]
    [InlineData("shop-terminal-tier2-duplicates.png", "源石溶剂|地灵|陨星|褐果|地灵")]
    [InlineData("shop-terminal-tier4-gray.png", "随身身份牌|斯卡蒂|深巡|复奏|风絮|耶拉")]
    [InlineData("shop-terminal-tier4-frozen-gray.png", "随身身份牌|斯卡蒂|深巡|复奏|风絮|耶拉")]
    public async Task RealShopSamples_ReadEquipmentAndOperators(string file, string expected)
    {
        fixture.SelectTheme(file);
        var items = await fixture.Reader.ReadAsync(File.ReadAllBytes(fixture.Sample(file)), 1920, 1080, fixture.Names);
        items.Select(i => i.Name).Should().Equal(expected.Split('|'));
        items.Select(i => i.Id).Should().OnlyHaveUniqueItems();
        items.Should().OnlyContain(i => i.OcrRegion.Y > .91 && i.OcrRegion.Y < .97
            && i.OcrRegion.X >= 0 && i.OcrRegion.X + i.OcrRegion.Width <= 1);
        if (file == "shop-tier6-frozen.png")
            items[0].OcrRegion.X.Should().BeGreaterThan(.40, "two sold slots must not shift the remaining names left");
        if (file.StartsWith("shop-frontier-", StringComparison.Ordinal) || file.StartsWith("shop-terminal-", StringComparison.Ordinal))
            for (int i = 0; i < items.Count; i++)
            {
                double left = 360 + (6 - items.Count + i) * 220;
                ((items[i].OcrRegion.X + items[i].OcrRegion.Width / 2) * 1920)
                    .Should().BeInRange(left, left + 220, "each marker must stay on its own card");
            }
    }

    [Theory]
    [InlineData("shop-tier6-glow-a.png", "突袭手雷|可露希尔|奥斯塔|拉普兰德|斥罪|鸿雪", 1280, 720, 0)]
    [InlineData("shop-tier6-glow-a.png", "突袭手雷|可露希尔|奥斯塔|拉普兰德|斥罪|鸿雪", 2560, 1080, 320)]
    [InlineData("shop-frontier-tier1-color.png", "坚守盾牌|野鬃|角峰|褐果", 1280, 720, 0)]
    [InlineData("shop-frontier-tier2-frozen-gray.png", "卫戍推荐信|砾|哈洛德|雪猎|云迹", 1280, 720, 0)]
    [InlineData("shop-frontier-tier2-frozen-gray.png", "卫戍推荐信|砾|哈洛德|雪猎|云迹", 2560, 1080, 320)]
    [InlineData("shop-terminal-tier4-frozen-gray.png", "随身身份牌|斯卡蒂|深巡|复奏|风絮|耶拉", 1280, 720, 0)]
    [InlineData("shop-terminal-tier4-frozen-gray.png", "随身身份牌|斯卡蒂|深巡|复奏|风絮|耶拉", 2560, 1080, 320)]
    public async Task SyntheticResizeAndLetterbox_KeepNamePositions(string file, string expected, int width, int height, int padding)
    {
        fixture.SelectTheme(file);
        using var source = new Bitmap(fixture.Sample(file));
        using var resized = new Bitmap(width, height);
        using (var g = Graphics.FromImage(resized))
        {
            g.Clear(Color.Black);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(source, new Rectangle(padding, 0, width - 2 * padding, height));
        }
        using var stream = new MemoryStream();
        resized.Save(stream, ImageFormat.Png);
        var items = await fixture.Reader.ReadAsync(stream.ToArray(), width, height, fixture.Names);
        items.Select(i => i.Name).Should().Equal(expected.Split('|'));
        double left = 360 + (6 - items.Count) * 220;
        ((items[0].OcrRegion.X + items[0].OcrRegion.Width / 2) * width)
            .Should().BeInRange(padding + left * height / 1080, padding + (left + 220) * height / 1080);
    }

    [Fact]
    public async Task SyntheticDuplicateCards_KeepTwoMarkersAfterMergingNameBands()
    {
        fixture.SelectTheme("shop-frontier-tier2-glow.png");
        using var original = new Bitmap(fixture.Sample("shop-frontier-tier2-glow.png"));
        using var shop = (Bitmap)original.Clone();
        using (var graphics = Graphics.FromImage(shop))
            graphics.DrawImage(original, new Rectangle(1020, 800, 220, 280), 1240, 800, 220, 280, GraphicsUnit.Pixel);
        using var stream = new MemoryStream();
        shop.Save(stream, ImageFormat.Png);
        var items = await fixture.Reader.ReadAsync(stream.ToArray(), 1920, 1080, ["角峰"]);
        items.Select(i => i.Id).Should().Equal("角峰@0", "角峰@1");
        (items[1].OcrRegion.X - items[0].OcrRegion.X).Should().BeGreaterThan(.08);
    }

    [Theory]
    [InlineData("shop-terminal-tier1-color.png")]
    [InlineData("shop-frontier-tier1-color.png")]
    [InlineData("shop-tier2-color.png")]
    public async Task NameStripWithoutShopControl_DoesNotProduceMarkers(string file)
    {
        fixture.SelectTheme(file);
        using var shop = new Bitmap(fixture.Sample(file));
        using (var graphics = Graphics.FromImage(shop))
            graphics.FillRectangle(Brushes.Black, 1680, 790, 240, 290);
        using var stream = new MemoryStream();
        shop.Save(stream, ImageFormat.Png);
        (await fixture.Reader.ReadAsync(stream.ToArray(), 1920, 1080, fixture.Names)).Should().BeEmpty();
    }

    [Fact]
    public async Task SyntheticTier1_FourSlots_AreNotTreatedAsFive()
    {
        fixture.SelectTheme("shop-tier2-color.png");
        using var source = new Bitmap(fixture.Sample("shop-tier2-color.png"));
        using var fourSlots = new Bitmap(1920, 1080);
        using (var g = Graphics.FromImage(fourSlots))
        {
            g.Clear(Color.Black);
            g.DrawImage(source, new Rectangle(1700, 990, 220, 50), 1700, 990, 220, 50, GraphicsUnit.Pixel);
            for (int slot = 0; slot < 4; slot++)
                g.DrawImage(source, new Rectangle(795 + slot * 220, 990, 220, 50),
                    575 + slot * 220, 990, 220, 50, GraphicsUnit.Pixel);
        }
        using var stream = new MemoryStream();
        fourSlots.Save(stream, ImageFormat.Png);
        var items = await fixture.Reader.ReadAsync(stream.ToArray(), 1920, 1080, fixture.Names);
        items.Select(i => i.Name).Should().Equal("聚合急救箱", "渡桥", "杰克", "陨星");
    }

    [Fact]
    public void Match_DuplicateNamesHaveSeparateStableIds()
    {
        var reader = new ShopReader(Mock.Of<IOcrEngine>(), ["奥斯塔"]);
        var items = reader.Match([
            new("奥斯塔", (1200, 390, 100, 40)),
            new("奥斯塔", (1640, 390, 100, 40)),
            new("收起", (3500, 370, 160, 60))], 1920, 1080, 810, ["奥斯塔"]);
        items.Should().HaveCount(2);
        items.Select(i => i.Id).Should().OnlyHaveUniqueItems();
        items[1].OcrRegion.X.Should().BeGreaterThan(items[0].OcrRegion.X);
    }

    [Fact]
    public void Match_UntrackedLongerEquipmentDoesNotMatchTrackedSuffix()
    {
        var reader = new ShopReader(Mock.Of<IOcrEngine>(), ["维式重锤", "终极维式重锤"]);
        OcrTextResult[] texts = [new("品终极维式重锤", (740, 390, 220, 40)),
            new("收起", (3500, 370, 160, 60))];
        reader.Match(texts, 1920, 1080, 810, ["维式重锤"]).Should().BeEmpty();
        reader.Match(texts, 1920, 1080, 810, ["维式重锤", "终极维式重锤"])
            .Select(i => i.Name).Should().Equal("终极维式重锤");
    }

    [Fact]
    public void Match_ClosedShopHasNoMarkers()
    {
        var reader = new ShopReader(Mock.Of<IOcrEngine>(), ["山"]);
        reader.Match([new("山", (740, 390, 30, 40))], 1920, 1080, 810, ["山"]).Should().BeEmpty();
    }
}

public sealed class ShopReaderFixture : IDisposable
{
    private readonly MaaOcrEngine _engine = new(Path.Combine(AppContext.BaseDirectory, "data"));
    public MaaOcrEngine Engine => _engine;
    public IReadOnlyList<string> Names { get; }
    public ShopReader Reader { get; }
    public void SelectTheme(string file) => Reader.Theme = file.StartsWith("shop-terminal-", StringComparison.Ordinal)
        ? GameShopTheme.Terminal : file.StartsWith("shop-frontier-", StringComparison.Ordinal) ? GameShopTheme.Ursus : GameShopTheme.Closure;
    public string Sample(string file) => Path.Combine(AppContext.BaseDirectory, "TestData", file);

    public ShopReaderFixture()
    {
        var store = new JsonDataStore(Path.Combine(AppContext.BaseDirectory, "TestData", "Regulation"));
        Names = store.LoadOperatorsAsync().GetAwaiter().GetResult().Select(o => o.Name)
            .Concat(store.LoadEquipmentAsync().GetAwaiter().GetResult().Equipment.Select(e => e.Name)).Distinct().ToList();
        Reader = new ShopReader(_engine, Names);
    }

    public void Dispose() => _engine.Dispose();
}
