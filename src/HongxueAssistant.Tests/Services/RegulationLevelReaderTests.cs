using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using HongxueAssistant.Core.GameModes.GarrisonProtocol;
using HongxueAssistant.Core.Services;
using FluentAssertions;
using Moq;

namespace HongxueAssistant.Tests.Services;

[Collection("Native OCR")]
public class RegulationLevelReaderTests
{
    [Theory]
    [InlineData("规程等级7", 7)]
    [InlineData("规 程 等 级 ２０", 20)]
    [InlineData("规程等级1", 1)]
    [InlineData("规程等级0", null)]
    [InlineData("规程等级21", null)]
    [InlineData("规程等级100", null)]
    [InlineData("规程等级", null)]
    [InlineData("终端等级7", null)]
    [InlineData("标准模拟", null)]
    [InlineData("12ms", null)]
    public async Task OnlyAnExplicitSinglePlayerLabelProducesALevel(string text, int? expected)
    {
        var ocr = new Mock<IOcrEngine>();
        ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), 134, 0, 480, 129))
            .ReturnsAsync([new(text, (160, 40, 150, 25))]);
        (await new RegulationLevelReader(ocr.Object).ReadAsync([], 1920, 1080)).Should().Be(expected);
    }

    [Fact]
    public async Task SplitDigitsJoinTheLabelWithoutJoiningTheLatencyOnAnotherRow()
    {
        var ocr = new Mock<IOcrEngine>();
        ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync([
                new("12ms", (160, 80, 60, 20)),
                new("7", (290, 40, 15, 25)),
                new("规程等级", (160, 39, 120, 25))]);
        (await new RegulationLevelReader(ocr.Object).ReadAsync([], 1920, 1080)).Should().Be(7);
    }

    [Trait("Category", "LocalScreenshot")]
    [Fact]
    public async Task ManualEnemyRecognitionCarriesTheOpeningDifficulty()
    {
        using var ocr = new MaaOcrEngine(Path.Combine(AppContext.BaseDirectory, "data"));
        var bosses = new BossDataService();
        await bosses.LoadAsync(Path.Combine(AppContext.BaseDirectory, "data"));
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "regulation-level-opening-1449x815.png"));
        var (width, height) = ImageBytes.GetDimensions(bytes);
        var result = await new EnemyRecognizer(ocr, new GarrisonOcrStrategy(), bosses).RecognizeAsync(bytes, width, height);
        result.Should().NotBeNull();
        result!.RegulationLevel.Should().Be(7);
    }

    [Trait("Category", "LocalScreenshot")]
    [Theory]
    [InlineData("regulation-level-opening-1449x815.png")]
    [InlineData("regulation-level-shop-1449x815.png")]
    public async Task LocalOpeningAndShopScreensReadLevelSevenAtCaptureScales(string file)
    {
        using var ocr = new MaaOcrEngine(Path.Combine(AppContext.BaseDirectory, "data"));
        var reader = new RegulationLevelReader(ocr);
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", file));
        var (width, height) = ImageBytes.GetDimensions(bytes);
        (await reader.ReadAsync(bytes, width, height)).Should().Be(7);
        using var original = new Bitmap(new MemoryStream(bytes));
        using var resized = new Bitmap(1280, 720);
        using (var graphics = Graphics.FromImage(resized))
        {
            graphics.InterpolationMode = InterpolationMode.Bilinear;
            graphics.DrawImage(original, 0, 0, 1280, 720);
        }
        using var stream = new MemoryStream();
        resized.Save(stream, ImageFormat.Png);
        (await reader.ReadAsync(stream.ToArray(), 1280, 720)).Should().Be(7);
    }
}
