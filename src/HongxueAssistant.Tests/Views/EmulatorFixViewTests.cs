using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using HongxueAssistant.App.Views;
using HongxueAssistant.App.Windows;
using HongxueAssistant.Core.Data;
using HongxueAssistant.Core.Interop;
using HongxueAssistant.Core.Models;
using HongxueAssistant.Core.Services;
using HongxueAssistant.Core.ViewModels;
using FluentAssertions;

namespace HongxueAssistant.Tests.Views;

internal static class EmulatorFixViewTests
{
    public static void VerifyEquipmentPurchase()
    {
        const string gear = "虹彩活血饮料";
        Operator[] operators = [new() { Name = "角峰" }];
        Equipment[] equipment = [new() { Name = gear }];
        foreach (var manual in new[] { false, true })
        {
            var folder = Path.Combine(Path.GetTempPath(), "equipment-purchase-" + Guid.NewGuid().ToString("N"));
            var settings = new SettingsManager(folder);
            var tracking = new TrackingViewModel();
            if (manual) tracking.AddTracking(gear, TrackingType.Equipment);
            else tracking.TrackLineup(new Lineup { Id = "test", Slots = [new() { OperatorName = "角峰", Equipments = [gear] }] });
            var calculator = new CovenantCalculator(operators, [], new Dictionary<string, string>());
            var view = new TrackingView { DataContext = tracking };
            view.SetOperators(operators); view.SetEquipment(equipment); view.SetCovenantCalculator(calculator);
            view.DeployPage.Initialize(operators.ToDictionary(o => o.Name), [], null, tracking, settings, new(), false);
            var host = new Window { Content = view, Height = 900, Width = 400, Opacity = 0,
                ShowActivated = false, ShowInTaskbar = false, Left = -20000, Top = -20000 };
            host.Show();
            ((ListBox)view.FindName("CompletionFilterList")).SelectedIndex = 2;
            view.DeployPage.ApplyRecognizedPurchases([gear]);
            foreach (var theme in Enum.GetValues<GameShopTheme>())
            foreach (var night in new[] { false, true })
            foreach (var width in new[] { 280, 360, 400 })
            {
                HongxueAssistant.App.ThemeManager.Apply(theme, night);
                host.Width = width;
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                view.UpdateLayout();
                var cell = Descendants<HongxueAssistant.App.Controls.EquipmentCell>((ItemsControl)view.FindName("EquipmentCells")).Single();
                cell.IsAcquired.Should().BeTrue();
                var button = (Button)cell.FindName("AcquireButton");
                button.Content.Should().Be("已获取");
                button.IsEnabled.Should().BeFalse();
                button.TransformToAncestor(view).TransformBounds(new Rect(button.RenderSize)).Right.Should().BeLessThanOrEqualTo(width + 1);
            }
            settings.SaveTrackingStateAsync(tracking.CaptureState()).GetAwaiter().GetResult();
            var restored = new TrackingViewModel();
            restored.LoadState(settings.LoadTrackingStateAsync(calculator.EquipmentRules).GetAwaiter().GetResult());
            restored.TrackedEquipment.Single().IsAcquired.Should().BeTrue();
            view.DeployPage.ApplyRecognizedPurchases([gear]);
            tracking.TrackedEquipment.Single().IsAcquired.Should().BeTrue();
            host.Close();
        }
    }

    public static void VerifyConnectionLayout()
    {
        var dialog = new GameConnectionWindow(new ScreenCaptureService(), new SettingsManager(Path.Combine(Path.GetTempPath(), "connection-layout")))
        { Opacity = 0, ShowActivated = false, ShowInTaskbar = false, Left = -20000, Top = -20000, WindowStartupLocation = WindowStartupLocation.Manual };
        dialog.Show();
        ((RadioButton)dialog.FindName("AdbMode")).IsChecked = true;
        ((System.Windows.Controls.Primitives.ToggleButton)dialog.FindName("ManualToggle")).IsChecked = true;
        var output = Path.Combine(AppContext.BaseDirectory, "TestResults", "emulator-connection");
        Directory.CreateDirectory(output);
        foreach (var theme in Enum.GetValues<GameShopTheme>())
        foreach (var night in new[] { false, true })
        {
            HongxueAssistant.App.ThemeManager.Apply(theme, night);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            dialog.UpdateLayout();
            var content = (FrameworkElement)dialog.Content;
            content.ActualHeight.Should().BeLessThan(900);
            foreach (var name in new[] { "WindowSelector", "DisplaySelector", "FollowFocusedGameToggle", "ApplyButton" })
            {
                var element = (FrameworkElement)dialog.FindName(name);
                element.IsVisible.Should().BeTrue();
                var bounds = element.TransformToAncestor(content).TransformBounds(new Rect(element.RenderSize));
                bounds.Right.Should().BeLessThanOrEqualTo(content.ActualWidth + 1);
                bounds.Bottom.Should().BeLessThanOrEqualTo(content.ActualHeight + 1);
            }
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(content);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(output, $"{theme}-{night}.png"));
            encoder.Save(file);
        }
        dialog.Close();
    }

    public static void VerifyPicker()
    {
        var operators = new List<Operator> { new() { Name = "最终", Tier = 6, CoreCovenants = ["乌萨斯"] },
            new() { Name = "过渡", Tier = 2 }, new() { Name = "其他", Tier = 3 } };
        operators.AddRange(Enumerable.Range(1, 6).Select(t => new Operator { Name = "扩展" + t, Tier = t, CoreCovenants = ["乌萨斯"] }));
        var tracking = new TrackingViewModel();
        tracking.TrackLineup(new Lineup { Slots = [new() { OperatorName = "最终" }], EarlySlots = [new() { OperatorName = "过渡" }] });
        var view = new DeployView();
        var state = new DeployPoolState();
        view.Initialize(operators.ToDictionary(o => o.Name), [], null, tracking,
            new SettingsManager(Path.Combine(Path.GetTempPath(), "picker-exclusions-" + Guid.NewGuid().ToString("N"))), state, false);
        var host = new Window { Content = view, Width = 400, Height = 900, Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        host.Show();
        Exception? error = null;
        var checkedPicker = false;
        Dispatcher.CurrentDispatcher.BeginInvoke(() =>
        {
            var picker = Application.Current.Windows.OfType<OperatorPickerWindow>().Single();
            try
            {
                checkedPicker = true;
                ((ListBox)picker.FindName("OperatorsList")).Items.Cast<Operator>().Select(o => o.Name).Should().Equal("其他");
                ((ListBox)picker.FindName("ScopeList")).Visibility.Should().Be(Visibility.Collapsed);
            }
            catch (Exception exception) { error = exception; }
            finally { picker.Close(); }
        }, DispatcherPriority.Loaded);
        Descendants<Button>(view).Single(b => Equals(b.Content, "＋ 添加")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        checkedPicker.Should().BeTrue();
        if (error is not null) throw error;
        var editor = new OperatorPickerWindow(operators, "", new HashSet<string>(), new("后期", "", new HashSet<string>(),
            new Dictionary<string, int>(), _ => new Dictionary<string, int>(), [], []));
        ((ListBox)editor.FindName("ScopeList")).Visibility.Should().Be(Visibility.Collapsed);
        tracking.AddTracking("追踪装备", TrackingType.Equipment);
        view.ApplyRecognizedPurchases(["最终", "其他", "追踪装备", "未追踪装备"]);
        state.IsOwned("最终").Should().BeTrue();
        state.IsOwned("其他").Should().BeTrue();
        state.IsElite("最终").Should().BeFalse();
        state.CountOf("最终").Should().Be(1);
        tracking.TrackedOperators.Single(e => e.Name == "最终").IsAcquired.Should().BeTrue();
        tracking.TrackedEquipment.Single(e => e.Name == "追踪装备").IsAcquired.Should().BeTrue();
        tracking.TrackedEquipment.Should().NotContain(e => e.Name == "未追踪装备");
        view.ApplyRecognizedPurchases(["最终", "追踪装备"]);
        state.CountOf("最终").Should().Be(1);
        tracking.TrackedEquipment.Single(e => e.Name == "追踪装备").IsAcquired.Should().BeTrue();
        editor.Close(); host.Close();
    }

    public static void VerifyPanel()
    {
        var folder = Path.Combine(Path.GetTempPath(), "panel-layer-" + Guid.NewGuid().ToString("N"));
        var calculator = new CovenantCalculator([], [], new Dictionary<string, string>());
        var panel = new SidePanelWindow(new(), new(), new(), new(), new(), new(),
            new(new LineupStore(Path.Combine(folder, "lineups"), calculator.EquipmentRules), calculator),
            new(calculator.EquipmentRules, Path.Combine(folder, "share")), [], [], [], new(folder), GameShopTheme.Terminal, new())
        { Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        var game = new Window { Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        var other = new Window { Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        panel.Show(); game.Show(); other.Show();
        var hwnd = new WindowInteropHelper(panel).Handle;
        var gameHwnd = new WindowInteropHelper(game).Handle;
        panel.Topmost.Should().BeFalse();
        panel.UpdateTopmost(gameHwnd, gameHwnd);
        panel.Topmost.Should().BeTrue();
        var dialog = new Window { Owner = panel, Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        dialog.Show();
        panel.UpdateTopmost(gameHwnd, new WindowInteropHelper(dialog).Handle);
        panel.Topmost.Should().BeTrue();
        panel.UpdateTopmost(gameHwnd, new WindowInteropHelper(other).Handle);
        panel.Topmost.Should().BeFalse();
        (User32.GetWindowLong(hwnd, User32.GWL_EXSTYLE) & 8).Should().Be(0);
        panel.UpdateTopmost(gameHwnd, hwnd);
        panel.Topmost.Should().BeFalse("clicking an inactive assistant alone does not force topmost");
        panel.UpdateTopmost(gameHwnd, gameHwnd);
        panel.UpdateTopmost(IntPtr.Zero, gameHwnd);
        panel.Topmost.Should().BeFalse();
        dialog.Close();

        var work = User32.GetMonitorGeometry(300, 300).WorkArea;
        var scale = User32.GetWindowDpiScale(hwnd);
        var start = new RECT { Left = work.Left + 80, Top = work.Top + 100, Right = work.Left + 80 + (int)(310 * scale), Bottom = work.Top + 100 + (int)(400 * scale) };
        User32.SetWindowBounds(hwnd, start);
        User32.SendMessage(hwnd, User32.WM_ENTERSIZEMOVE, IntPtr.Zero, IntPtr.Zero);
        var near = start; near.Left = work.Left + 5; near.Right = near.Left + start.Width;
        User32.SetWindowBounds(hwnd, near);
        User32.SendMessage(hwnd, User32.WM_EXITSIZEMOVE, IntPtr.Zero, IntPtr.Zero);
        User32.ReadWindowRect(hwnd).Left.Should().Be(work.Left);
        var control = (CheckBox)panel.FindName("FreeSidePanelMovementToggle");
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (!control.IsEnabled && DateTime.UtcNow < timeout)
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        control.IsEnabled.Should().BeTrue();
        var saved = new SettingsManager(folder).LoadSidePanelPositionAsync().GetAwaiter().GetResult();
        saved.Placement!.Left.Should().Be(work.Left);
        // Resizing near an edge must preserve the user's exact position.
        User32.SendMessage(hwnd, User32.WM_ENTERSIZEMOVE, IntPtr.Zero, IntPtr.Zero);
        near.Right += 30;
        User32.SetWindowBounds(hwnd, near);
        User32.SendMessage(hwnd, User32.WM_EXITSIZEMOVE, IntPtr.Zero, IntPtr.Zero);
        User32.ReadWindowRect(hwnd).Left.Should().Be(near.Left);
        panel.Close(); game.Close(); other.Close();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
