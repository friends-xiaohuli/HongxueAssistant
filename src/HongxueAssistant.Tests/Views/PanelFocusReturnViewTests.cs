using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using FluentAssertions;
using HongxueAssistant.App.Windows;
using HongxueAssistant.Core.Data;
using HongxueAssistant.Core.Interop;
using HongxueAssistant.Core.Models;
using HongxueAssistant.Core.Services;
using HongxueAssistant.Core.ViewModels;

namespace HongxueAssistant.Tests.Views;

internal static class PanelFocusReturnViewTests
{
    public static void Verify(bool showInTaskbar)
    {
        var directory = Directory.CreateTempSubdirectory("hongxue-focus-return-").FullName;
        var calculator = new CovenantCalculator([], [], new Dictionary<string, string>());
        var panel = new SidePanelWindow(new(), new(), new(), new(), new(), new(),
            new LineupViewModel(new LineupStore(Path.Combine(directory, "lineups"), calculator.EquipmentRules), calculator),
            new LineupShareService(calculator.EquipmentRules, Path.Combine(directory, "shares")), [], [], [],
            new SettingsManager(directory), GameShopTheme.Terminal, new())
        { ShowActivated = false, ShowInTaskbar = showInTaskbar, Opacity = 0, Left = -20000, Top = -20000 };
        var game = Window();
        var other = Window();
        var editor = Window();
        var picker = Window();
        try
        {
            panel.Show(); game.Show(); other.Show(); Pump();
            editor.Owner = panel; editor.Show(); picker.Owner = editor; picker.Show(); Pump();
            var ph = Handle(panel); var gh = Handle(game); var oh = Handle(other);
            var eh = Handle(editor); var dh = Handle(picker);
            panel.UpdateTopmost(gh, oh); Check(false);
            // Direct return works without first refocusing the game.
            panel.UpdateTopmost(gh, ph); Check(true);
            panel.UpdateTopmost(gh, oh); Check(false); AssertBelowOther();
            panel.UpdateTopmost(gh, eh); Check(true);
            NativeTopmost(eh).Should().BeTrue("the returning editor itself must remain above other windows");
            panel.UpdateTopmost(gh, oh); Check(false); AssertBelowOther();
            panel.UpdateTopmost(gh, dh); Check(true);
            NativeTopmost(dh).Should().BeTrue("nested owned dialogs can also be reactivated directly");
            panel.UpdateTopmost(gh, ph); Check(true);
            // Native state may lag WPF during dialog/minimize/restore transitions.
            User32.SetWindowLayer(ph, false);
            panel.Topmost.Should().BeTrue(); NativeTopmost(ph).Should().BeFalse();
            panel.UpdateTopmost(gh, dh); Check(true); NativeTopmost(dh).Should().BeTrue();
            panel.UpdateTopmost(gh, oh); Check(false);
            NativeTopmost(eh).Should().BeFalse(); NativeTopmost(dh).Should().BeFalse();
            panel.IsAssistantHidden = true;
            panel.UpdateTopmost(gh, eh); Check(false);
            panel.IsAssistantHidden = false;
            panel.UpdateTopmost(gh, eh); Check(true);
            panel.UpdateTopmost(IntPtr.Zero, dh); Check(false);
            panel.UpdateTopmost(gh, ph); Check(true);
            panel.WindowState = WindowState.Minimized; Pump();
            panel.UpdateTopmost(gh, ph); Check(false);
            panel.WindowState = WindowState.Normal; Pump();
            panel.UpdateTopmost(gh, ph); Check(true);
            panel.ClearGamePosition(); Check(false);

            void Check(bool expected)
            {
                Pump();
                panel.Topmost.Should().Be(expected);
                NativeTopmost(ph).Should().Be(expected);
            }
            void AssertBelowOther()
            {
                for (var next = User32.GetWindow(oh, 2); next != IntPtr.Zero; next = User32.GetWindow(next, 2))
                    if (next == ph) return;
                throw new InvalidOperationException("Assistant was not lowered below the other application.");
            }
        }
        finally
        {
            picker.Close(); editor.Close(); other.Close(); game.Close(); panel.Close(); Pump();
            Directory.Delete(directory, true);
        }
    }

    private static Window Window() => new()
    { Width = 100, Height = 100, Left = -20000, Top = -20000, Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
    private static IntPtr Handle(Window window) => new WindowInteropHelper(window).Handle;
    private static bool NativeTopmost(IntPtr window) => (User32.GetWindowLong(window, User32.GWL_EXSTYLE) & 8) != 0;
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
}
