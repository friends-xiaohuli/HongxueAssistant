using System.Drawing;
using System.Drawing.Imaging;
using HongxueAssistant.Core.GameModes.GarrisonProtocol;
using HongxueAssistant.Core.GameState;
using HongxueAssistant.Core.Models;
using HongxueAssistant.Core.Services;
using HongxueAssistant.Core.ViewModels;
using FluentAssertions;
using Moq;

namespace HongxueAssistant.Tests.Services;

public class OcrScannerServiceTests
{
    [Fact]
    public async Task ShopEconomyPublishesOnlyConfirmedValuesAndResetClearsThem()
    {
        using var scanner = CreateScanner(() => _screenshot, new(), economyReader: CreateEconomyReader());
        var updates = new List<ShopEconomy?>();
        scanner.EconomyChanged += updates.Add;
        await scanner.ScanNowAsync();
        scanner.CurrentEconomy.Should().BeNull();
        await Task.Delay(TimeSpan.FromSeconds(2.1));
        await scanner.ScanNowAsync();
        scanner.CurrentEconomy.Should().Be(new ShopEconomy(1, 4, 5, false, "规程等级7", true));
        updates.Should().ContainSingle();
        scanner.ResetEconomy();
        scanner.CurrentEconomy.Should().BeNull();
        updates.Last().Should().BeNull();
        await scanner.ScanNowAsync();
        scanner.CurrentEconomy.Should().BeNull("the previous run's confirmed values must not act as candidates");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateEconomyReadCannotSurviveARunResetOrConnectionSwitch(bool switchConnection)
    {
        using var previous = new GameConnectionService();
        using var next = new GameConnectionService(new() { Source = GameCaptureSource.Adb });
        var connection = previous;
        var economyReader = CreateEconomyReader();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<IReadOnlyList<OcrTextResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), 0, 0, 1152, 97))
            .Returns(() => { entered.TrySetResult(); return release.Task; });
        using var scanner = CreateScanner(() => _screenshot, new(), () => connection, economyReader);
        var scan = scanner.ScanNowAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (switchConnection) connection = next;
            else scanner.ResetEconomy();
        }
        finally
        {
            release.TrySetResult([new("回合1", (600, 20, 80, 25)), new("规程等级7", (160, 40, 150, 25))]);
            await scan;
        }
        scanner.CurrentEconomy.Should().BeNull();
        await scanner.ScanNowAsync();
        scanner.CurrentEconomy.Should().BeNull("the stale frame must not count as the first matching read");
    }

    [Fact]
    public async Task DifficultyIsReadOnTheOpeningEvenWhenTheShopScanIsSuppressed()
    {
        _ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), 480, 0, 960, 108))
            .ReturnsAsync([new("确认本局信息", (600, 20, 200, 30))]);
        _ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), 38, 388, 1344, 367))
            .ReturnsAsync([new("附加盟约", (100, 440, 200, 30))]);
        _ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), 134, 0, 480, 129))
            .ReturnsAsync([new("规程等级7", (160, 40, 150, 25))]);
        using var scanner = CreateScanner(() => _screenshot, new());
        await scanner.ScanNowAsync();
        scanner.RegulationLevel.Should().Be(7);
        _ocr.Verify(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), 0, 0, It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task DifficultySurvivesCaptureLossAndResetsBeforeTheNextRun()
    {
        _ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), 134, 0, 480, 129))
            .ReturnsAsync([new("规程等级7", (160, 40, 150, 25))]);
        byte[]? shot = _screenshot;
        using var scanner = CreateScanner(() => shot, new());
        var levels = new List<int?>();
        scanner.RegulationLevelChanged += () => levels.Add(scanner.RegulationLevel);
        await scanner.ScanNowAsync();
        shot = null;
        await scanner.ScanNowAsync();
        scanner.RegulationLevel.Should().Be(7);
        scanner.ResetRegulationLevel();
        scanner.RegulationLevel.Should().BeNull();
        shot = _screenshot;
        await scanner.ScanNowAsync();
        levels.Should().Equal(7, null, 7);
        _ocr.Verify(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), 134, 0, 480, 129), Times.Exactly(2));
    }

    [Fact]
    public async Task PeriodicDifficultyCorrectsADifferentManualReadWithoutRecollapsingAnUnchangedLevel()
    {
        _ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), 134, 0, 480, 129))
            .ReturnsAsync([new("规程等级7", (160, 40, 150, 25))]);
        using var scanner = CreateScanner(() => _screenshot, new());
        var vm = new EnemyViewModel();
        vm.Update(new("假想敌：胄", [], []),
            new() { Hp = [new() { Name = "规程等级7", Group = "single", Hp = 400000 }] }, []);
        scanner.RegulationLevelChanged += () => vm.RegulationLevel = scanner.RegulationLevel;
        await scanner.ScanNowAsync();
        vm.OtherBossHpExpanded = true;
        await Task.Delay(TimeSpan.FromSeconds(3.1));
        await scanner.ScanNowAsync();
        vm.OtherBossHpExpanded.Should().BeTrue("an unchanged confirmed level must preserve manual expansion");
        vm.RegulationLevel = 8;
        await Task.Delay(TimeSpan.FromSeconds(3.1));
        await scanner.ScanNowAsync();
        vm.RegulationLevel.Should().Be(7);
        vm.HasCurrentBossHp.Should().BeTrue();
    }

    [Fact]
    public async Task NewRunRejectsADifficultyReadStartedBeforeTheReset()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<IReadOnlyList<OcrTextResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), 134, 0, 480, 129))
            .Returns(() => { entered.TrySetResult(); return release.Task; });
        using var scanner = CreateScanner(() => _screenshot, new());
        var scan = scanner.ScanNowAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            scanner.ResetRegulationLevel();
        }
        finally
        {
            release.TrySetResult([new("规程等级7", (160, 40, 150, 25))]);
            await scan;
        }
        scanner.RegulationLevel.Should().BeNull();
        await scanner.ScanNowAsync();
        scanner.RegulationLevel.Should().Be(7);
    }

    [Fact]
    public async Task NewRunRejectsTheFrameCapturedBeforeOpeningDetectionCompletes()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<IReadOnlyList<OcrTextResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), 480, 0, 960, 108))
            .Returns(() => { entered.TrySetResult(); return release.Task; });
        _ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), 134, 0, 480, 129))
            .ReturnsAsync([new("规程等级7", (160, 40, 150, 25))]);
        using var scanner = CreateScanner(() => _screenshot, new());
        var scan = scanner.ScanNowAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            scanner.ResetRegulationLevel();
        }
        finally
        {
            release.TrySetResult([]);
            await scan;
        }
        scanner.RegulationLevel.Should().BeNull();
        _ocr.Verify(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), 134, 0, 480, 129), Times.Never);
        await scanner.ScanNowAsync();
        scanner.RegulationLevel.Should().Be(7);
    }

    [Fact]
    public async Task SwitchingCaptureConnectionRejectsThePreviousDifficulty()
    {
        using var previous = new GameConnectionService();
        using var next = new GameConnectionService(new() { Source = GameCaptureSource.Adb });
        var connection = previous;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<IReadOnlyList<OcrTextResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), 134, 0, 480, 129))
            .Returns(() => { entered.TrySetResult(); return release.Task; });
        using var scanner = CreateScanner(() => _screenshot, new(), () => connection);
        var scan = scanner.ScanNowAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            connection = next;
        }
        finally
        {
            release.TrySetResult([new("规程等级7", (160, 40, 150, 25))]);
            await scan;
        }
        scanner.RegulationLevel.Should().BeNull();
        await scanner.ScanNowAsync();
        scanner.RegulationLevel.Should().Be(7);
    }

    [Fact]
    public async Task FirstAutomaticScanKeepsTheRunAlreadyStartedByManualRecording()
    {
        using var connection = new GameConnectionService();
        using var scanner = CreateScanner(() => _screenshot, new(), () => connection);
        scanner.TryStartNewRun(false).Should().BeTrue();
        scanner.MarkInformationRecorded();
        scanner.SetBanScreenDetectionPaused(true);
        await scanner.ScanNowAsync();
        scanner.TryStartNewRun(false).Should().BeFalse("the first scan is still using the same capture connection");
    }

    private readonly byte[] _screenshot;
    private readonly string[] _names = ["角峰", "隐现", "坚守盾牌"];
    private readonly Mock<IOcrEngine> _ocr = new();
    private IReadOnlyList<OcrTextResult> _shopTexts =
    [
        new("角峰", (1000, 390, 100, 40)),
        new("隐现", (1500, 390, 100, 40)),
        new("坚守盾牌", (2000, 390, 200, 40)),
        new("收起", (3500, 370, 160, 60))
    ];

    public OcrScannerServiceTests()
    {
        using var bitmap = new Bitmap(1920, 1080);
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        _screenshot = stream.ToArray();
        _ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns((byte[] _, int x, int y, int _, int _) => Task.FromResult<IReadOnlyList<OcrTextResult>>(
                x == 0 && y == 0 ? _shopTexts : []));
    }

    [Fact]
    public async Task CachedShopCanSwitchLineupsAndEquipmentWithoutAnotherCapture()
    {
        var captures = 0;
        var state = new HongxueAssistant.Core.GameState.GameState();
        var tracking = new TrackingViewModel();
        using var scanner = CreateScanner(() => { captures++; return _screenshot; }, state);
        await scanner.ScanNowAsync();
        state.ShopItems.Select(i => i.Name).Should().Equal(_names);
        state.ShopItems.Should().OnlyContain(i => !i.IsTracked);
        var positions = state.ShopItems.Select(i => i.OcrRegion).ToArray();

        tracking.TrackLineup(new Lineup { Slots = [new() { OperatorName = "角峰" }] });
        GameStateUpdater.UpdateShopTracking(state.ShopItems, tracking.AllEntries.ToList());
        state.ShopItems.Where(i => i.IsTracked).Select(i => i.Name).Should().Equal("角峰");
        tracking.TrackLineup(new Lineup { Slots = [new() { OperatorName = "隐现", Equipments = ["坚守盾牌"] }] });
        GameStateUpdater.UpdateShopTracking(state.ShopItems, tracking.AllEntries.ToList());
        state.ShopItems.Where(i => i.IsTracked).Select(i => i.Name).Should().Equal("隐现", "坚守盾牌");
        tracking.ClearAllTracking();
        GameStateUpdater.UpdateShopTracking(state.ShopItems, tracking.AllEntries.ToList());

        state.ShopItems.Should().OnlyContain(i => !i.IsTracked);
        state.ShopItems.Select(i => i.OcrRegion).Should().Equal(positions);
        captures.Should().Be(1);
    }

    [Fact]
    public async Task TemporaryCaptureFailureKeepsCacheButActualClosedShopClearsIt()
    {
        byte[]? capture = _screenshot;
        var state = new HongxueAssistant.Core.GameState.GameState();
        using var scanner = CreateScanner(() => capture, state);
        var updates = 0;
        scanner.GameStateUpdated += _ => updates++;
        await scanner.ScanNowAsync();
        updates.Should().Be(1);

        capture = null;
        await scanner.ScanNowAsync();
        await scanner.ScanNowAsync();
        state.ShopItems.Select(i => i.Name).Should().Equal(_names);
        updates.Should().Be(1);

        capture = _screenshot;
        await scanner.ScanNowAsync();
        updates.Should().Be(2, "returning to the game must publish a fresh result even if its names are unchanged");
        _shopTexts = [];
        await scanner.ScanNowAsync();
        await scanner.ScanNowAsync();
        state.ShopItems.Should().BeEmpty("a successfully captured closed shop must remove old highlights");
    }

    [Fact]
    public async Task RepeatedImmediateRequestsShareTheRunningScan()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<IReadOnlyList<OcrTextResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), 0, 0, It.IsAny<int>(), It.IsAny<int>()))
            .Returns(() => { entered.TrySetResult(); return release.Task; });
        var captures = 0;
        var state = new HongxueAssistant.Core.GameState.GameState();
        using var scanner = CreateScanner(() => { Interlocked.Increment(ref captures); return _screenshot; }, state);
        var tracking = new TrackingViewModel();
        scanner.GameStateUpdated += result => GameStateUpdater.UpdateShopTracking(result.ShopItems, tracking.AllEntries.ToList());
        var first = scanner.ScanNowAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            tracking.TrackLineup(new Lineup { Slots = [new() { OperatorName = "隐现", Equipments = ["坚守盾牌"] }] });
            await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => scanner.ScanNowAsync()));
            captures.Should().Be(1);
        }
        finally
        {
            release.TrySetResult(_shopTexts);
            await first;
        }
        state.ShopItems.Should().HaveCount(3);
        state.ShopItems.Where(i => i.IsTracked).Select(i => i.Name).Should().Equal("隐现", "坚守盾牌");
        await scanner.ScanNowAsync();
        captures.Should().Be(2, "the scan gate must be released after completion");
    }

    [Fact]
    public async Task InformationOverlaySuppressesCachedShopHighlights()
    {
        var state = new HongxueAssistant.Core.GameState.GameState();
        using var scanner = CreateScanner(() => _screenshot, state);
        await scanner.ScanNowAsync();
        scanner.SetBanScreenDetectionPaused(true);
        await scanner.ScanNowAsync();
        await scanner.ScanNowAsync();
        state.ShopItems.Should().BeEmpty();
    }

    [Fact]
    public async Task AdbCaptureFailureClearsCachedShop()
    {
        using var connection = new GameConnectionService(new() { Source = GameCaptureSource.Adb });
        byte[]? capture = _screenshot;
        var state = new HongxueAssistant.Core.GameState.GameState();
        using var scanner = CreateScanner(() => capture, state, () => connection);
        await scanner.ScanNowAsync();
        state.ShopItems.Select(i => i.Name).Should().Equal(_names);

        capture = null;
        await scanner.ScanNowAsync();
        await scanner.ScanNowAsync();

        state.ShopItems.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SwitchingConnectionDiscardsInFlightShopResult(bool switchDisplay)
    {
        using var previous = new GameConnectionService();
        using var next = new GameConnectionService(new() { Source = GameCaptureSource.Adb });
        var connection = previous;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<IReadOnlyList<OcrTextResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), 0, 0, It.IsAny<int>(), It.IsAny<int>()))
            .Returns(() => { entered.TrySetResult(); return release.Task; });
        var state = new HongxueAssistant.Core.GameState.GameState();
        using var scanner = CreateScanner(() => _screenshot, state, () => connection);
        var scan = scanner.ScanNowAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (switchDisplay)
                typeof(GameConnectionService).GetField("_captureRevision", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .SetValue(previous, previous.CaptureRevision + 10000);
            else connection = next;
        }
        finally
        {
            release.TrySetResult(_shopTexts);
            await scan;
        }

        state.ShopItems.Should().BeEmpty();
        await scanner.ScanNowAsync();
        state.ShopItems.Select(i => i.Name).Should().Equal(_names);
    }

    [Theory]
    [InlineData("角峰", true, false)]
    [InlineData("坚守盾牌", true, false)]
    [InlineData("坚守盾牌", false, false)]
    [InlineData("坚守盾牌", true, true)]
    [InlineData("坚守盾牌", false, true)]
    public async Task PurchasePipelineChecksFreshFundsOnlyWhenCardsChange(string purchased, bool eligible, bool unreliableFunds)
    {
        var reader = CreateEconomyReader();
        var money = 20;
        _ocr.Setup(e => e.RecognizeLineAsync(It.IsAny<byte[]>(), It.Is<int>(x => x > 1700), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns((byte[] _, int x, int y, int _, int _) => Task.FromResult<IReadOnlyList<OcrTextResult>>([new(money.ToString(), (x, y, 20, 30))]));
        using var scanner = new OcrScannerService(() => _screenshot, new GarrisonOcrStrategy(),
            new ShopReader(_ocr.Object, _names) { Theme = GameShopTheme.Closure }, _ocr.Object, new(), _names)
        {
            EconomyReader = reader,
            PurchaseTargets = () => eligible ? new HashSet<string> { purchased } : new HashSet<string>(),
            AutomaticPurchaseTracking = true,
        };
        var results = new List<string>();
        scanner.PurchasesDetected += (names, _, _) => results.AddRange(names);
        await scanner.ScanNowAsync();
        await scanner.ScanNowAsync();
        _ocr.Verify(e => e.RecognizeLineAsync(It.IsAny<byte[]>(), It.Is<int>(x => x > 1700), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()), Times.Exactly(3));
        _shopTexts = _shopTexts.Where(t => t.Text != purchased).ToList();
        if (unreliableFunds)
        {
            money = 26;
            await scanner.ScanNowAsync();
            results.Should().BeEmpty();
            await Task.Delay(2100);
        }
        money = 19;
        await scanner.ScanNowAsync();
        results.Should().BeEmpty();
        await scanner.ScanNowAsync();
        results.Should().Equal(eligible ? new[] { purchased } : []);
        _ocr.Verify(e => e.RecognizeLineAsync(It.IsAny<byte[]>(), It.Is<int>(x => x > 1700), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()),
            Times.Exactly(unreliableFunds ? 9 : 6));
    }

    [Theory]
    [InlineData("none")]
    [InlineData("run")]
    [InlineData("connection")]
    public async Task PurchasePipelineKeepsFundsThatDropBeforeCardDisappearance(string reset)
    {
        using var previous = new GameConnectionService();
        using var next = new GameConnectionService();
        var connection = previous;
        var reader = CreateEconomyReader();
        var money = 3;
        _ocr.Setup(e => e.RecognizeLineAsync(It.IsAny<byte[]>(), It.Is<int>(x => x > 1700), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns((byte[] _, int x, int y, int _, int _) => Task.FromResult<IReadOnlyList<OcrTextResult>>([new(money.ToString(), (x, y, 20, 30))]));
        using var scanner = new OcrScannerService(() => _screenshot, new GarrisonOcrStrategy(),
            new ShopReader(_ocr.Object, _names) { Theme = GameShopTheme.Closure }, _ocr.Object, new(), _names, getConnection: () => connection)
        {
            EconomyReader = reader,
            PurchaseTargets = () => new HashSet<string> { "坚守盾牌" },
            AutomaticPurchaseTracking = true,
        };
        var results = new List<string>();
        scanner.PurchasesDetected += (names, _, _) => results.AddRange(names);
        await scanner.ScanNowAsync();
        await scanner.ScanNowAsync();
        money = 1;
        await Task.Delay(2100);
        await scanner.ScanNowAsync();
        results.Should().BeEmpty();
        if (reset == "run") scanner.ResetEconomy();
        if (reset == "connection") connection = next;
        _shopTexts = _shopTexts.Where(t => t.Text != "坚守盾牌").ToList();
        await scanner.ScanNowAsync();
        results.Should().BeEmpty();
        await scanner.ScanNowAsync();
        results.Should().Equal(reset == "none" ? new[] { "坚守盾牌" } : []);
        await scanner.ScanNowAsync();
        _ocr.Verify(e => e.RecognizeLineAsync(It.IsAny<byte[]>(), It.Is<int>(x => x > 1700), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()), Times.Exactly(9));
    }

    [Fact]
    public async Task AutomaticPurchasesDefaultOffAndTogglingRequiresFreshEvidence()
    {
        var reader = CreateEconomyReader();
        var money = 3;
        var targetsRead = 0;
        _ocr.Setup(e => e.RecognizeLineAsync(It.IsAny<byte[]>(), It.Is<int>(x => x > 1700), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns((byte[] _, int x, int y, int _, int _) => Task.FromResult<IReadOnlyList<OcrTextResult>>([new(money.ToString(), (x, y, 20, 30))]));
        using var scanner = new OcrScannerService(() => _screenshot, new GarrisonOcrStrategy(),
            new ShopReader(_ocr.Object, _names) { Theme = GameShopTheme.Closure }, _ocr.Object, new(), _names)
        {
            EconomyReader = reader,
            PurchaseTargets = () => { targetsRead++; return new HashSet<string> { "坚守盾牌" }; },
        };
        var results = new List<string>();
        scanner.PurchasesDetected += (names, _, _) => results.AddRange(names);
        var original = _shopTexts.ToList();
        scanner.AutomaticPurchaseTracking.Should().BeFalse();
        await scanner.ScanNowAsync(); await scanner.ScanNowAsync();
        _shopTexts = original.Where(t => t.Text != "坚守盾牌").ToList(); money = 1;
        await scanner.ScanNowAsync(); await scanner.ScanNowAsync();
        results.Should().BeEmpty(); targetsRead.Should().Be(0);
        _ocr.Verify(e => e.RecognizeLineAsync(It.IsAny<byte[]>(), It.Is<int>(x => x > 1700), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()), Times.Exactly(3));
        scanner.AutomaticPurchaseTracking = true;
        await scanner.ScanNowAsync(); await scanner.ScanNowAsync();
        results.Should().BeEmpty("enabling must not apply purchases made while disabled");
        _shopTexts = original; money = 3;
        await scanner.ScanNowAsync(); await scanner.ScanNowAsync();
        _shopTexts = original.Where(t => t.Text != "坚守盾牌").ToList(); money = 1;
        await scanner.ScanNowAsync();
        results.Should().BeEmpty();
        var revision = scanner.RunRevision;
        var economy = scanner.CurrentEconomy;
        scanner.AutomaticPurchaseTracking = false;
        scanner.AutomaticPurchaseTracking = true;
        scanner.RunRevision.Should().BeGreaterThan(revision, "queued purchase events must become stale");
        scanner.CurrentEconomy.Should().BeSameAs(economy, "the toggle does not clear the displayed economy");
        await scanner.ScanNowAsync(); await scanner.ScanNowAsync();
        results.Should().BeEmpty("the partly confirmed purchase must not survive off/on");
    }

    [Fact]
    public async Task DisablingAutomaticPurchasesRejectsAnInFlightFundsRead()
    {
        using var scanner = new OcrScannerService(() => _screenshot, new GarrisonOcrStrategy(),
            new ShopReader(_ocr.Object, _names) { Theme = GameShopTheme.Closure }, _ocr.Object, new(), _names)
        {
            EconomyReader = CreateEconomyReader(), AutomaticPurchaseTracking = true,
            PurchaseTargets = () => new HashSet<string> { "坚守盾牌" },
        };
        var results = new List<string>();
        scanner.PurchasesDetected += (names, _, _) => results.AddRange(names);
        await scanner.ScanNowAsync(); await scanner.ScanNowAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<IReadOnlyList<OcrTextResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ocr.Setup(e => e.RecognizeLineAsync(It.IsAny<byte[]>(), It.Is<int>(x => x > 1700), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(() => { entered.TrySetResult(); return release.Task; });
        _shopTexts = _shopTexts.Where(t => t.Text != "坚守盾牌").ToList();
        var scan = scanner.ScanNowAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            scanner.AutomaticPurchaseTracking = false;
        }
        finally { release.TrySetResult([new("1", (1800, 807, 20, 30))]); await scan; }
        await scanner.ScanNowAsync();
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task PendingFastFundsReadsDoNotPostponePeriodicHudCalibration()
    {
        var reader = CreateEconomyReader();
        var money = 2;
        _ocr.Setup(e => e.RecognizeLineAsync(It.IsAny<byte[]>(), It.Is<int>(x => x > 1700), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns((byte[] _, int x, int y, int _, int _) => Task.FromResult<IReadOnlyList<OcrTextResult>>([new(money.ToString(), (x, y, 20, 30))]));
        using var scanner = new OcrScannerService(() => _screenshot, new GarrisonOcrStrategy(),
            new ShopReader(_ocr.Object, _names) { Theme = GameShopTheme.Closure }, _ocr.Object, new(), _names)
        {
            EconomyReader = reader,
            PurchaseTargets = () => new HashSet<string> { "坚守盾牌" },
            AutomaticPurchaseTracking = true,
        };
        await scanner.ScanNowAsync();
        await scanner.ScanNowAsync();
        _shopTexts = _shopTexts.Where(t => t.Text != "坚守盾牌").ToList();
        money = 16;
        for (int i = 0; i < 8; i++)
        {
            await scanner.ScanNowAsync();
            await Task.Delay(300);
        }
        _ocr.Verify(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), 0, 0, 1152, 97), Times.AtLeast(2));
    }

    [Fact]
    public async Task PendingPurchaseCapturesFreshEvidenceWithoutAnotherWholeShopPass()
    {
        var money = 2;
        var reader = CreateEconomyReader();
        _ocr.Setup(e => e.RecognizeLineAsync(It.IsAny<byte[]>(), It.Is<int>(x => x > 1700), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns((byte[] _, int x, int y, int _, int _) => Task.FromResult<IReadOnlyList<OcrTextResult>>([new(money.ToString(), (x, y, 20, 30))]));
        _ocr.Setup(e => e.RecognizeLineAsync(It.IsAny<byte[]>(), 0, 0, It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync([new("收起", (0, 0, 80, 30))]);
        var captures = 0;
        using var scanner = new OcrScannerService(() => { captures++; return _screenshot; }, new GarrisonOcrStrategy(),
            new ShopReader(_ocr.Object, _names) { Theme = GameShopTheme.Closure }, _ocr.Object, new(), _names)
        { EconomyReader = reader, AutomaticPurchaseTracking = true, PurchaseTargets = () => new HashSet<string> { "坚守盾牌" } };
        var acquired = new List<string>(); scanner.PurchasesDetected += (names, _, _) => acquired.AddRange(names);
        await scanner.ScanNowAsync(); await scanner.ScanNowAsync();
        _shopTexts = _shopTexts.Where(t => t.Text != "坚守盾牌").ToArray(); money = 1;
        await scanner.ScanNowAsync();
        acquired.Should().Equal("坚守盾牌");
        captures.Should().Be(4, "baseline scans plus the missing frame and an independent confirmation frame");
        _ocr.Verify(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), 0, 0, 3840, 540), Times.Exactly(6));
        _ocr.Verify(e => e.RecognizeLineAsync(It.IsAny<byte[]>(), It.Is<int>(x => x > 1700), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()), Times.Exactly(9));
    }

    [Theory]
    [InlineData("closed")]
    [InlineData("round")]
    [InlineData("funds")]
    [InlineData("refresh")]
    [InlineData("prefix-refresh")]
    [InlineData("preparation")]
    [InlineData("connection")]
    [InlineData("display")]
    [InlineData("disabled")]
    [InlineData("unavailable")]
    public async Task LightweightConfirmationRejectsNewOrInvalidEvidence(string change)
    {
        using var previous = new GameConnectionService(); using var next = new GameConnectionService();
        var connection = previous;
        var money = 2; var fresh = false; var buying = false; var postChangeCaptures = 0;
        var reader = CreateEconomyReader();
        _ocr.Setup(e => e.RecognizeLineAsync(It.IsAny<byte[]>(), It.Is<int>(x => x > 1700), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns((byte[] _, int x, int y, int _, int _) => Task.FromResult<IReadOnlyList<OcrTextResult>>([new((fresh && change == "funds" ? 2 : money).ToString(), (x, y, 20, 30))]));
        _ocr.Setup(e => e.RecognizeLineAsync(It.IsAny<byte[]>(), 0, 0, It.IsAny<int>(), It.IsAny<int>()))
            .Returns(() => Task.FromResult<IReadOnlyList<OcrTextResult>>(change == "closed" ? [] : [new("收起", (0, 0, 80, 30))]));
        _ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), 0, 0, 1152, 97))
            .Returns(() => Task.FromResult<IReadOnlyList<OcrTextResult>>([new(fresh && change == "round" ? "回合2" : "回合1", (600, 20, 80, 25)),
                new("规程等级7", (160, 40, 150, 25)), new(fresh && change == "preparation" ? "战斗中" : "休息一下", (700, 20, 150, 25))]));
        using var changed = new Bitmap(1920, 1080);
        changed.SetPixel(change == "prefix-refresh" ? 804 : 750, 1005, Color.White);
        using var changedStream = new MemoryStream(); changed.Save(changedStream, ImageFormat.Png);
        OcrScannerService? active = null;
        using var scanner = new OcrScannerService(() =>
        {
            if (!buying || ++postChangeCaptures != 2) return _screenshot;
            fresh = true;
            if (change == "connection") connection = next;
            if (change == "display")
                typeof(GameConnectionService).GetField("_captureRevision", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .SetValue(previous, previous.CaptureRevision + 1);
            if (change == "disabled") active!.AutomaticPurchaseTracking = false;
            return change == "unavailable" ? null : change is "refresh" or "prefix-refresh" ? changedStream.ToArray() : _screenshot;
        }, new GarrisonOcrStrategy(), new ShopReader(_ocr.Object, _names) { Theme = GameShopTheme.Closure }, _ocr.Object, new(), _names,
            getConnection: () => connection)
        { EconomyReader = reader, AutomaticPurchaseTracking = true, PurchaseTargets = () => new HashSet<string> { "坚守盾牌" } };
        active = scanner;
        var acquired = new List<string>(); scanner.PurchasesDetected += (names, _, _) => acquired.AddRange(names);
        await scanner.ScanNowAsync(); await scanner.ScanNowAsync();
        _shopTexts = _shopTexts.Where(t => t.Text != "坚守盾牌").ToArray(); money = 1; buying = true;
        await scanner.ScanNowAsync();
        postChangeCaptures.Should().Be(2);
        acquired.Should().BeEmpty();
    }

    private ShopEconomyReader CreateEconomyReader()
    {
        _ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), 0, 0, 1152, 97))
            .ReturnsAsync([new("回合1", (600, 20, 80, 25)), new("规程等级7", (160, 40, 150, 25)), new("休息一下", (700, 20, 150, 25))]);
        _ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), 0, 896, 1152, 184))
            .ReturnsAsync([new("升级", (500, 979, 100, 30))]);
        _ocr.Setup(e => e.RecognizeLineAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns((byte[] _, int x, int _, int _, int _) =>
                Task.FromResult<IReadOnlyList<OcrTextResult>>([new(x > 1700 ? "4" : "5", (x, 807, 20, 30))]));
        return new ShopEconomyReader(_ocr.Object);
    }

    private OcrScannerService CreateScanner(Func<byte[]?> capture, HongxueAssistant.Core.GameState.GameState state,
        Func<GameConnectionService>? getConnection = null, ShopEconomyReader? economyReader = null) => new(
        capture, new GarrisonOcrStrategy(), new ShopReader(_ocr.Object, _names) { Theme = GameShopTheme.Closure },
        _ocr.Object, state, _names, getConnection: getConnection) { EconomyReader = economyReader };
}
