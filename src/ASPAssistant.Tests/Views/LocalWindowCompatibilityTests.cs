using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ASPAssistant.App.Windows;
using ASPAssistant.App.Views;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Interop;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.Views;

internal static class LocalWindowCompatibilityTests
{
    public static void Verify()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hongxue-window-" + Guid.NewGuid().ToString("N"));
        var rules = new LineupEquipmentService(new Dictionary<string, string>());
        var calc = new CovenantCalculator([], [], new Dictionary<string, string>());
        var panel = new SidePanelWindow(new(), new(), new(), new(), new(), new(),
            new LineupViewModel(new LineupStore(directory, rules), calc),
            new LineupShareService(rules, Path.Combine(directory, "shares")), [], [], [],
            new SettingsManager(directory), GameShopTheme.Terminal, new());
        var game = new Window { Width = 200, Height = 100, ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
        var other = new Window { Width = 200, Height = 100, ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
        var dialog = new Window { Width = 100, Height = 100, ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
        try
        {
            panel.ShowActivated = false; panel.ShowInTaskbar = false; panel.Opacity = 0;
            panel.Show(); game.Show(); other.Show(); dialog.Owner = panel; dialog.Show();
            Pump();
            var ph = new WindowInteropHelper(panel).Handle;
            var gh = new WindowInteropHelper(game).Handle;
            var dh = new WindowInteropHelper(dialog).Handle;
            var oh = new WindowInteropHelper(other).Handle;
            panel.Topmost.Should().BeFalse();
            panel.UpdateTopmost(gh, ph); panel.Topmost.Should().BeFalse();
            panel.UpdateTopmost(gh, gh); Check(true);
            panel.UpdateTopmost(gh, dh); Check(true);
            panel.UpdateTopmost(gh, oh); Check(false);
            var below = false;
            for (var h = User32.GetWindow(oh, 2); h != IntPtr.Zero; h = User32.GetWindow(h, 2))
                if (h == ph) { below = true; break; }
            below.Should().BeTrue("losing game focus lowers the assistant");
            panel.UpdateTopmost(gh, ph); Check(false);
            panel.UpdateTopmost(gh, gh); Check(true);
            panel.ClearGamePosition(); Check(false);
            var before = User32.ReadWindowRect(ph);
            var work = User32.GetMonitorGeometry(before.Left, before.Top).WorkArea;
            var moved = new RECT { Left = work.Left + 4, Top = work.Top + 4, Right = work.Left + 4 + before.Width, Bottom = work.Top + 4 + before.Height };
            User32.SendMessage(ph, User32.WM_ENTERSIZEMOVE, IntPtr.Zero, IntPtr.Zero);
            User32.SetWindowBounds(ph, moved);
            User32.SendMessage(ph, User32.WM_EXITSIZEMOVE, IntPtr.Zero, IntPtr.Zero);
            Pump();
            var snapped = User32.ReadWindowRect(ph);
            snapped.Left.Should().Be(work.Left); snapped.Top.Should().Be(work.Top);
            snapped.Width.Should().Be(before.Width); snapped.Height.Should().Be(before.Height);

            Operator[] ops = [new() { Name = "空弦", CoreCovenants = ["炎"] }, new() { Name = "霍尔海雅", AdditionalCovenants = ["精准"] }];
            var bans = new BanViewModel();
            var vm = new LineupPlannerViewModel(new LineupPlanner(ops, []), ops, bans, rules);
            vm.AddLocked("空弦"); vm.AddLocked("霍尔海雅"); bans.SetBans(["空弦"]);
            var view = new LineupPlannerView { DataContext = vm };
            view.Measure(new Size(310, 900)); view.Arrange(new Rect(0, 0, 310, 900)); view.UpdateLayout(); Pump();
            var chip = Children<Border>(view).Single(b => b.Tag is Operator op && op.Name == "空弦" && b.DataContext is PlannerLockedOperator);
            chip.Opacity.Should().Be(0.55);
            Children<TextBlock>(chip).Single(t => t.Text == "已 Ban").Visibility.Should().Be(Visibility.Visible);
            vm.ActiveLockedCount.Should().Be(1);
            bans.SetBans([]); view.UpdateLayout(); Pump();
            chip.Opacity.Should().Be(1); vm.ActiveLockedCount.Should().Be(2);

            void Check(bool expected)
            {
                panel.Topmost.Should().Be(expected);
                ((User32.GetWindowLong(ph, User32.GWL_EXSTYLE) & 8) != 0).Should().Be(expected);
            }
        }
        finally { dialog.Close(); other.Close(); game.Close(); panel.Close(); }
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is T found) yield return found;
            foreach (var next in Children<T>(c)) yield return next;
        }
    }
}
