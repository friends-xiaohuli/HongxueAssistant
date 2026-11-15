using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HongxueAssistant.App;
using HongxueAssistant.App.Windows;
using HongxueAssistant.Core.Data;
using HongxueAssistant.Core.Models;
using HongxueAssistant.Core.Services;
using HongxueAssistant.Core.ViewModels;
using FluentAssertions;

namespace HongxueAssistant.Tests.Views;

internal static class ExperimentalPurchaseViewTests
{
    public static void Verify(GameShopTheme theme, bool night)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var folder = Path.Combine(Path.GetTempPath(), "purchase-toggle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var settings = new SettingsManager(folder);
        var calculator = new CovenantCalculator([], [], new Dictionary<string, string>());
        var panel = new SidePanelWindow(new(), new(), new(), new(), new(), new(),
            new(new LineupStore(Path.Combine(folder, "lineups"), calculator.EquipmentRules), calculator),
            new(calculator.EquipmentRules, Path.Combine(folder, "share")), [], [], [], settings, theme, new() { FreeMovement = true })
        { Opacity = 0, ShowActivated = false, ShowInTaskbar = false, Left = -20000, Top = -20000, Width = 280, Height = 1000 };
        var events = new List<bool>();
        panel.AutomaticPurchaseTrackingChanged += events.Add;
        panel.InitAutomaticPurchaseTracking(Task.Run(settings.LoadAutomaticPurchaseTrackingAsync).GetAwaiter().GetResult());
        try
        {
            panel.Show(); ThemeManager.Apply(theme, night); Pump();
            ((Button)panel.FindName("SettingsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            var toggle = (CheckBox)panel.FindName("AutomaticPurchaseTrackingToggle");
            var peer = (CheckBox)panel.FindName("MultiLineupTrackingToggle");
            toggle.IsChecked.Should().BeFalse(); events.Should().BeEmpty();
            toggle.Style.Should().BeSameAs(peer.Style);
            System.Windows.Automation.AutomationProperties.GetName(toggle).Should().Be("自动标记购买（实验性）");
            foreach (var width in new[] { 280, 360, 400 })
            {
                panel.Width = width;
                foreach (var enabled in new[] { true, false })
                {
                    toggle.IsChecked = enabled;
                    var until = DateTime.UtcNow.AddSeconds(5);
                    while (!toggle.IsEnabled && DateTime.UtcNow < until) Pump();
                    toggle.IsEnabled.Should().BeTrue();
                    Task.Run(settings.LoadAutomaticPurchaseTrackingAsync).GetAwaiter().GetResult().Should().Be(enabled);
                    events.Last().Should().Be(enabled);
                    toggle.BringIntoView(); Pump();
                    toggle.ActualWidth.Should().BeApproximately(peer.ActualWidth, .5);
                    toggle.ActualHeight.Should().BeApproximately(peer.ActualHeight, .5);
                    var row = (DockPanel)VisualTreeHelper.GetParent(toggle);
                    foreach (var text in Desc<TextBlock>(row))
                    {
                        var bounds = text.TransformToAncestor(row).TransformBounds(new Rect(text.RenderSize));
                        bounds.Right.Should().BeLessThanOrEqualTo(row.ActualWidth + .5);
                    }
                    Save(row, theme, night, width, enabled);
                }
            }
            events.Should().Equal(true, false, true, false, true, false);
        }
        finally { panel.Close(); Directory.Delete(folder, true); }
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static IEnumerable<T> Desc<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T item) yield return item;
            foreach (var nested in Desc<T>(child)) yield return nested;
        }
    }
    private static void Save(FrameworkElement view, GameShopTheme theme, bool night, int width, bool enabled)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "TestResults", "experimental-purchases"); Directory.CreateDirectory(dir);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth), (int)Math.Ceiling(view.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen()) drawing.DrawRectangle(new VisualBrush(view), null, new Rect(0, 0, view.ActualWidth, view.ActualHeight));
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(dir, $"{theme}-{night}-{width}-{enabled}.png")); encoder.Save(file);
    }
}
