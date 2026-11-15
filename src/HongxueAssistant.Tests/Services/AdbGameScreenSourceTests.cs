// Modified for ASP Assistant Arknights on 2026-11-11; see NOTICE.
using System.Drawing;
using System.Drawing.Imaging;
using HongxueAssistant.Core.Models;
using HongxueAssistant.Core.Services;
using FluentAssertions;

namespace HongxueAssistant.Tests.Services;

public sealed class AdbGameScreenSourceTests
{
    private const string Activities = """
        Display #2 (activities from top to bottom):
          topResumedActivity=ActivityRecord{abc u0 com.hypergryph.arknights/com.u8.sdk.U8UnityContext t215}
        Display #0 (activities from top to bottom):
          topResumedActivity=ActivityRecord{def u0 bitpit.launcher/.ui.HomeActivity t2}
        Display #3 (activities from top to bottom):
          topResumedActivity=ActivityRecord{ghi u0 com.hypergryph.arknights.bilibili/com.u8.sdk.U8UnityContext t216}
          ResumedActivity: ActivityRecord{abc u0 com.hypergryph.arknights/com.u8.sdk.U8UnityContext t215}
        """;

    private const string Displays = """
          Display 0:
            mBaseDisplayInfo=DisplayInfo{"内置屏幕", displayId 0, uniqueId "local:4619827820427265280"}
          Display 2:
            mBaseDisplayInfo=DisplayInfo{"mumuscreen001", displayId 2, uniqueId "local:4619827767814508545"}
          Display 3:
            mBaseDisplayInfo=DisplayInfo{"mumuscreen002", displayId 3, uniqueId "local:4619826888814064386"}
        """;

    [Fact]
    public void MapsLogicalGameDisplaysToPhysicalIdsAndExcludesTheLauncher()
    {
        var displays = AdbGameScreens.Parse(Activities, Displays, "FocusedDisplayId: 3");
        displays.Select(d => d.DisplayId).Should().Equal(2, 3);
        displays[0].PhysicalDisplayId.Should().Be("4619827767814508545");
        displays[1].PhysicalDisplayId.Should().Be("4619826888814064386");
        displays[1].IsFocused.Should().BeTrue();
        displays[0].IsFocused.Should().BeFalse();
    }

    [Fact]
    public void ActivityFocusIsAFallbackWhenTheInputServiceHasNoDisplayId()
    {
        AdbGameScreens.Parse(Activities, Displays, "").Single(d => d.IsFocused).DisplayId.Should().Be(2);
    }

    [Fact]
    public void AutomaticModePrefersTheFocusedGameOverThePreviouslySavedGame()
    {
        var screens = AdbGameScreens.Parse(Activities, Displays, "FocusedDisplayId: 3");
        var selected = AdbGameScreens.Select(screens, new()
        {
            AndroidPackageName = "com.hypergryph.arknights", AndroidDisplayId = 2,
        });
        selected.DisplayId.Should().Be(3);
    }

    [Fact]
    public void LauncherFocusKeepsTheLastGameAndDoesNotSelectTheDesktop()
    {
        var screens = AdbGameScreens.Parse(Activities, Displays, "FocusedDisplayId: 0");
        screens.Should().OnlyContain(d => !d.IsFocused);
        AdbGameScreens.Select(screens, new() { AndroidPackageName = "com.hypergryph.arknights.bilibili" })
            .DisplayId.Should().Be(3);
    }

    [Fact]
    public void ManualModeKeepsTheChosenGameWhileAnotherGameHasFocus()
    {
        var screens = AdbGameScreens.Parse(Activities, Displays, "FocusedDisplayId: 3");
        AdbGameScreens.Select(screens, new()
        {
            AndroidPackageName = "com.hypergryph.arknights", AndroidDisplayId = 2, FollowFocusedGame = false,
        }).DisplayId.Should().Be(2);
    }

    [Fact]
    public void AGameRetainsItsIdentityWhenMuMuChangesItsDisplayId()
    {
        AdbGameScreens.Select([new(8, "999999999999999999", "com.hypergryph.arknights")], new()
        {
            AndroidPackageName = "com.hypergryph.arknights", AndroidDisplayId = 2, FollowFocusedGame = false,
        }).DisplayId.Should().Be(8);
    }

    [Fact]
    public void AmbiguousGamesWithoutFocusOrASavedChoiceRequireSelection()
    {
        var screens = AdbGameScreens.Parse(Activities, Displays, "FocusedDisplayId: 0");
        var act = () => AdbGameScreens.Select(screens, new());
        act.Should().Throw<IOException>().WithMessage("*多个*");
    }

    [Fact]
    public void NonDefaultDisplayWithoutAPhysicalIdIsNotUsedAsTheDefaultScreen()
    {
        var screens = AdbGameScreens.Parse(Activities, "", "");
        screens.Should().HaveCount(2);
        foreach (var screen in screens)
        {
            var capture = () => AdbGameScreens.ControllerConfig(screen);
            capture.Should().Throw<IOException>();
        }
    }

    [Fact]
    public void CaptureFollowsFocusAndChangesRevisionSoOldRecognitionCanBeDiscarded()
    {
        var directory = Directory.CreateTempSubdirectory("asp-display-test-").FullName;
        try
        {
            var executable = Path.Combine(directory, "adb.exe");
            File.WriteAllText(executable, "test stub, never executed");
            var source = new FakeSource();
            using var connection = new GameConnectionService(new()
            {
                Source = GameCaptureSource.Adb, AdbPath = executable, AdbSerial = "emulator-5554",
            }, adbSource: source);
            connection.CaptureAdbScreen(1920).Should().NotBeNull();
            var revision = connection.CaptureRevision;
            source.Focused = 3;
            Thread.Sleep(1100);
            connection.CaptureAdbScreen(1920).Should().NotBeNull();
            source.Captured.Should().Equal(2, 3);
            connection.Settings.AndroidPackageName.Should().Be("com.hypergryph.arknights.bilibili");
            connection.CaptureRevision.Should().BeGreaterThan(revision);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void ScrollingRequiresTheSameGameTargetAsTheLastCapture()
    {
        var dir = Directory.CreateTempSubdirectory("asp-display-scroll-").FullName;
        try
        {
            var adb = Path.Combine(dir, "adb.exe"); File.WriteAllText(adb, "stub");
            var source = new FakeSource();
            using var connection = new GameConnectionService(new() { Source = GameCaptureSource.Adb, AdbPath = adb, AdbSerial = "test" }, adbSource: source);
            connection.CaptureAdbScreen(1920).Should().NotBeNull();
            connection.ScrollInformationPanelDown().Should().BeTrue();
            source.Swiped.Should().Equal(2);
            source.Focused = 3;
            connection.ScrollInformationPanelDown().Should().BeFalse();
            source.Swiped.Should().Equal(new[] { 2 }, "a changed target must be captured before any gesture");
            connection.CaptureAdbScreen(1920).Should().NotBeNull();
            connection.ScrollInformationPanelDown().Should().BeTrue();
            source.Swiped.Should().Equal(2, 3);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void PhysicalDisplayChangesInvalidateResultsAndBlockGesturesBeforeAnotherCapture()
    {
        var directory = Directory.CreateTempSubdirectory("hongxue-physical-display-").FullName;
        try
        {
            var adb = Path.Combine(directory, "adb.exe"); File.WriteAllText(adb, "never executed");
            var source = new FakeSource();
            using var connection = new GameConnectionService(new() { Source = GameCaptureSource.Adb,
                AdbPath = adb, AdbSerial = "isolated" }, adbSource: source);
            connection.CaptureAdbScreen(1920).Should().NotBeNull();
            var revision = connection.CaptureRevision;
            source.PhysicalOverride = "999999999999999999";
            connection.ScrollInformationPanelDown().Should().BeFalse();
            source.Swiped.Should().BeEmpty();
            connection.CaptureRevision.Should().BeGreaterThan(revision);
            connection.CaptureAdbScreen(1920).Should().NotBeNull();
            connection.CurrentDisplay!.PhysicalDisplayId.Should().Be(source.PhysicalOverride);
            connection.ScrollInformationPanelDown().Should().BeTrue();
            revision = connection.CaptureRevision;
            source.FailDiscovery = true;
            connection.ScrollInformationPanelDown().Should().BeFalse();
            Thread.Sleep(1100);
            connection.RefreshCaptureRevision().Should().BeGreaterThan(revision);
            source.Swiped.Should().Equal(2);
        }
        finally { Directory.Delete(directory, true); }
    }

    private sealed class FakeSource : IAdbGameScreenSource
    {
        public int Focused = 2;
        public string? PhysicalOverride;
        public bool FailDiscovery;
        public List<int> Captured { get; } = [];
        public List<int> Swiped { get; } = [];
        public bool Swipe(string adbPath, string serial, AdbGameDisplay display, int width, int height)
        { Swiped.Add(display.DisplayId); return true; }
        public IReadOnlyList<AdbGameDisplay> FindDisplays(string adbPath, string serial)
        {
            if (FailDiscovery) throw new IOException("isolated discovery failure");
            return AdbGameScreens.Parse(Activities, Displays, $"FocusedDisplayId: {Focused}")
                .Select(display => display.DisplayId == Focused && PhysicalOverride is not null
                    ? display with { PhysicalDisplayId = PhysicalOverride } : display).ToList();
        }

        public byte[] Capture(string adbPath, string serial, AdbGameDisplay display)
        {
            Captured.Add(display.DisplayId);
            using var bitmap = new Bitmap(1280, 720);
            using var buffer = new MemoryStream();
            bitmap.Save(buffer, ImageFormat.Png);
            return buffer.ToArray();
        }
    }
}
