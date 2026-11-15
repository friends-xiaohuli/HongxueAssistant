using System.Reflection;
using System.Drawing;
using HongxueAssistant.Core;
using HongxueAssistant.Core.GameModes.GarrisonProtocol;
using HongxueAssistant.Core.Models;
using HongxueAssistant.Core.Services;
using FluentAssertions;
using Moq;

namespace HongxueAssistant.Tests.Services;

[CollectionDefinition("Application logging", DisableParallelization = true)]
public class ApplicationLoggingCollection;

[Collection("Application logging")]
public class AppLoggerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"aspa_logging_{Guid.NewGuid():N}");
    private readonly FieldInfo _pathField = typeof(AppLogger).GetField("_logFilePath", BindingFlags.Static | BindingFlags.NonPublic)!;
    private readonly string? _previousPath;
    private readonly Func<bool> _previousPresenceCheck;
    private string LogPath => Directory.GetFiles(_directory, "aspa-*.log").Single();

    public AppLoggerTests()
    {
        _previousPath = (string?)_pathField.GetValue(null);
        _previousPresenceCheck = (Func<bool>)typeof(AppLogger).GetField("_isGamePresent", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        AppLogger.SetGamePresenceCheck(static () => true);
        AppLogger.Initialize(_directory);
    }

    [Fact]
    public void StartupInformationRemainsBeforeGamePresenceCheckIsInstalled()
    {
        AppLogger.Info("App", "Startup complete");
        AppLogger.SetGamePresenceCheck(static () => false);
        AppLogger.Info("ScreenCapture", "Arknights window not found");

        var text = File.ReadAllText(LogPath);
        text.Should().Contain("===== HongxueAssistant").And.Contain("Startup complete");
        text.Should().NotContain("Arknights window not found");
    }

    [Fact]
    public void MissingGameStopsRoutineFilesAndGameStartResumesThem()
    {
        var present = false;
        AppLogger.SetGamePresenceCheck(() => present);
        var before = File.ReadAllText(LogPath);

        Parallel.For(0, 100, _ =>
        {
            AppLogger.Info("Overlay", "idle update");
            AppLogger.Warn("ScreenCapture", "Arknights window not found");
        });
        AppLogger.SaveArtifact("absent", [1, 2, 3], ".png").Should().BeNull();
        File.ReadAllText(LogPath).Should().Be(before);
        Directory.GetFiles(_directory).Should().ContainSingle();

        present = true;
        AppLogger.Info("ShopScan", "game started");
        AppLogger.Warn("ScreenCapture", "foreground mismatch");
        var artifact = AppLogger.SaveArtifact("present", [1, 2, 3], ".png");
        File.ReadAllBytes(artifact!).Should().Equal(1, 2, 3);
        var activeText = File.ReadAllText(LogPath);
        activeText.Should().Contain("game started").And.Contain("foreground mismatch");

        present = false;
        AppLogger.Info("Overlay", "game exited");
        AppLogger.Warn("ScreenCapture", "Arknights window not found");
        AppLogger.SaveArtifact("exited", [4, 5], ".png").Should().BeNull();
        File.ReadAllText(LogPath).Should().Be(activeText);
        Directory.GetFiles(_directory).Should().HaveCount(2);

        present = true;
        AppLogger.Info("ShopScan", "game restarted");
        File.ReadAllText(LogPath).Should().Contain("game restarted");
    }

    [Fact]
    public void MissingGameStillRecordsErrorsAndExceptionDetails()
    {
        AppLogger.SetGamePresenceCheck(static () => false);

        AppLogger.Error("App", "critical failure");
        AppLogger.Error("OcrScanner", "scan failed", new InvalidOperationException("test exception details"));

        var text = File.ReadAllText(LogPath);
        text.Should().Contain("[ERROR] [App] critical failure");
        text.Should().Contain("[ERROR] [OcrScanner] scan failed: System.InvalidOperationException: test exception details");
    }

    [Fact]
    public void PurchaseDiagnosticsRecordChangedEvidenceWithoutRepeatingStableOrWaitingStates()
    {
        using var image = new Bitmap(960, 540);
        using (var graphics = Graphics.FromImage(image)) graphics.Clear(Color.LightGray);
        using var stream = new MemoryStream();
        image.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        var occupied = stream.ToArray();
        using (var graphics = Graphics.FromImage(image)) graphics.Clear(Color.FromArgb(15, 25, 40));
        stream.SetLength(0);
        image.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        var empty = stream.ToArray();
        ShopItem[] cards = [new() { Name = "坚守盾牌", OcrRegion = (.33, .94, .06, .025) }];
        var funds = new ShopEconomy(1, 2, 5, false, "规程等级7", true);
        var targets = new HashSet<string> { "坚守盾牌" };
        var detector = new ShopPurchaseDetector();
        detector.Observe(occupied, cards, true, funds, targets);
        detector.Observe(occupied, cards, true, funds, targets);
        var stable = File.ReadAllText(LogPath);
        stable.Split("Baseline established:").Should().HaveCount(2);
        stable.Split("Baseline stable:").Should().HaveCount(2);
        for (var i = 0; i < 1000; i++) detector.Observe(occupied, cards, true, funds, targets);
        File.ReadAllText(LogPath).Should().Be(stable);

        detector.Observe(empty, [], true, funds, targets).Should().BeEmpty();
        var waiting = File.ReadAllText(LogPath);
        waiting.Should().Contain("reason=funds-not-decreased").And.Contain("money=2->2").And.Contain("坚守盾牌@0.330");
        for (var i = 0; i < 100; i++) detector.Observe(empty, [], true, funds, targets).Should().BeEmpty();
        File.ReadAllText(LogPath).Should().Be(waiting);
        detector.Observe(occupied, [], true, funds with { Money = 1 }, targets).Should().BeEmpty();
        File.ReadAllText(LogPath).Should().Contain("reason=slot-not-empty");
        detector.Observe(empty, [], true, funds with { Money = 1 }, targets).Should().BeEmpty();
        detector.Observe(empty, [], true, funds with { Money = 1 }, targets).Should().Equal("坚守盾牌");
        File.ReadAllText(LogPath).Should().Contain("reason=awaiting-second-empty-frame").And.Contain("Confirmed [坚守盾牌], money=2->1");
        detector.Reset("source-changed");
        var reset = File.ReadAllText(LogPath);
        reset.Should().Contain("Baseline cleared: reason=source-changed");
        for (var i = 0; i < 100; i++) detector.Reset("source-changed");
        File.ReadAllText(LogPath).Should().Be(reset);
        Directory.GetFiles(_directory).Should().ContainSingle("diagnostics must not create screenshots");
    }

    [Theory]
    [InlineData("closed", "shop-closed")]
    [InlineData("missing", "hud-unavailable")]
    [InlineData("round", "round-unavailable")]
    [InlineData("preparation", "not-preparation")]
    public void PurchaseDiagnosticsExplainWhyTheBaselineWasDiscarded(string change, string reason)
    {
        using var image = new Bitmap(960, 540);
        using var stream = new MemoryStream(); image.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        var frame = stream.ToArray();
        ShopItem[] cards = [new() { Name = "坚守盾牌", OcrRegion = (.33, .94, .06, .025) }];
        var funds = new ShopEconomy(1, 2, 5, false, "规程等级7", true);
        var detector = new ShopPurchaseDetector();
        detector.Observe(frame, cards, true, funds, new HashSet<string> { "坚守盾牌" });
        var invalid = change switch { "missing" => null, "round" => funds with { Round = null },
            "preparation" => funds with { IsPreparation = false }, _ => funds };
        detector.Observe(frame, cards, change != "closed", invalid, new HashSet<string>()).Should().BeEmpty();
        var cleared = File.ReadAllText(LogPath);
        cleared.Should().Contain($"Baseline cleared: reason={reason}");
        detector.Observe(frame, cards, change != "closed", invalid, new HashSet<string>()).Should().BeEmpty();
        File.ReadAllText(LogPath).Should().Be(cleared);
    }

    [Fact]
    public async Task DisabledPurchasesDoNotReadTargetsOrProducePerScanDiagnostics()
    {
        using var image = new Bitmap(960, 540);
        using var stream = new MemoryStream(); image.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        var frame = stream.ToArray();
        var ocr = new Mock<IOcrEngine>();
        ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Array.Empty<OcrTextResult>());
        ocr.Setup(e => e.RecognizeLineAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Array.Empty<OcrTextResult>());
        var targetReads = 0;
        using var scanner = new OcrScannerService(() => frame, new GarrisonOcrStrategy(), new ShopReader(ocr.Object, ["坚守盾牌"]),
            ocr.Object, new(), ["坚守盾牌"])
        { PurchaseTargets = () => { targetReads++; return new HashSet<string> { "坚守盾牌" }; } };
        for (var i = 0; i < 5; i++) await scanner.ScanNowAsync();
        File.ReadAllText(LogPath).Should().NotContain("[ShopPurchase]").And.NotContain("[ERROR]");
        scanner.AutomaticPurchaseTracking = true;
        scanner.AutomaticPurchaseTracking = true;
        scanner.AutomaticPurchaseTracking = false;
        var disabled = File.ReadAllText(LogPath);
        disabled.Split("Automatic marking enabled=True").Should().HaveCount(2);
        disabled.Split("Automatic marking enabled=False").Should().HaveCount(2);
        for (var i = 0; i < 5; i++) await scanner.ScanNowAsync();
        File.ReadAllLines(LogPath).Where(line => line.Contains("[ShopPurchase]")).Should().HaveCount(2);
        targetReads.Should().Be(0);
        Directory.GetFiles(_directory).Should().ContainSingle();
    }

    public void Dispose()
    {
        AppLogger.SetGamePresenceCheck(_previousPresenceCheck);
        _pathField.SetValue(null, _previousPath);
        Path.GetDirectoryName(Path.GetFullPath(_directory)).Should().Be(Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\', '/'));
        Directory.Delete(_directory, true);
    }
}
