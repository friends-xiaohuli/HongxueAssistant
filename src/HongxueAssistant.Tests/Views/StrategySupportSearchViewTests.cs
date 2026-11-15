using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HongxueAssistant.App;
using HongxueAssistant.App.Views;
using HongxueAssistant.App.Windows;
using HongxueAssistant.Core.Data;
using HongxueAssistant.Core.Models;
using HongxueAssistant.Core.Services;
using HongxueAssistant.Core.ViewModels;
using FluentAssertions;

namespace HongxueAssistant.Tests.Views;

internal static class StrategySupportSearchViewTests
{
    public static void Verify(GameShopTheme theme, bool night)
    {
        Operator[] operators = [new() { Name = PlannerStrategies.PithName, RequiredStrategyId = PlannerStrategies.PithId, AdditionalCovenants = ["调和"] },
            new() { Name = "Pith(卫戍协议)", IsCustom = true, IsSelectedCustom = true }, new() { Name = "正常干员" }];
        var vm = new LineupPlannerViewModel(new LineupPlanner(operators, []), operators, new(), EquipmentTestData.Rules);
        var view = new LineupPlannerView { DataContext = vm };
        var host = new Window { Content = view, Height = 950, Left = -20000, Top = -20000, Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            host.Show(); ThemeManager.Apply(theme, night);
            foreach (var width in new[] { 280, 360, 400 })
            {
                host.Width = width; vm.Strategy = PlannerStrategy.Pith;
                ((TextBox)view.FindName("LockSearchBox")).Text = "盟约辅助"; Pump();
                var chip = Desc<Button>(view).Single(b => b.Tag is Operator o && o.Name == PlannerStrategies.PithName);
                chip.IsEnabled.Should().BeFalse();
                Desc<TextBlock>(chip).Single(t => t.Text == "策略已包含").IsVisible.Should().BeTrue();
                chip.ActualWidth.Should().BeLessThanOrEqualTo(width - 20);
                Save(chip, theme, night, width);
                vm.AddLocked("盟约辅助").Should().BeFalse();
                vm.Strategy = PlannerStrategy.Other; Pump();
                Desc<Button>(view).Any(b => b.Tag is Operator o && o.Name == PlannerStrategies.PithName).Should().BeFalse();
                vm.LockedOperators.Should().BeEmpty();
            }
            foreach (var selected in new[] { true, false })
            {
                var picker = new OperatorPickerWindow(operators, "", selected ? new HashSet<string> { PlannerStrategies.PithId } : new HashSet<string>())
                { Left = -20000, Top = -20000, Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
                try
                {
                    picker.Show();
                    ((TextBox)picker.FindName("SearchBox")).Text = "盟约辅助干员"; Pump();
                    var list = (ListBox)picker.FindName("OperatorsList");
                    list.Items.Count.Should().Be(1);
                    var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
                    row.IsEnabled.Should().Be(selected);
                    if (!selected) row.ToolTip.Should().Be("必须在策略中添加Pith的“优等生”策略");
                }
                finally { picker.Close(); }
            }
            var folder = Path.Combine(Path.GetTempPath(), "support-search-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var tracking = new TrackingViewModel();
                var state = new DeployPoolState(); tracking.SetDeployPool(state);
                tracking.AddTracking(PlannerStrategies.PithName, TrackingType.Operator);
                tracking.AddTracking("正常干员", TrackingType.Operator);
                var deploy = new DeployView();
                deploy.Initialize(operators.ToDictionary(o => o.Name), [], null, tracking, new SettingsManager(folder), state, false);
                host.Content = deploy;
                ((TextBox)deploy.FindName("PoolSearch")).Text = "盟约辅助干员"; Pump();
                var groups = (StackPanel)deploy.FindName("PoolGroups");
                Desc<TextBlock>(groups).Should().Contain(t => t.Text == PlannerStrategies.PithName);
                Desc<TextBlock>(groups).Should().NotContain(t => t.Text == "正常干员");
            }
            finally { Directory.Delete(folder, true); }
        }
        finally { host.Close(); }
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
    private static void Save(FrameworkElement view, GameShopTheme theme, bool night, int width)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "TestResults", "strategy-support-search"); Directory.CreateDirectory(dir);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth), (int)Math.Ceiling(view.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen()) drawing.DrawRectangle(new VisualBrush(view), null, new Rect(0, 0, view.ActualWidth, view.ActualHeight));
        bitmap.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(dir, $"{theme}-{night}-{width}.png")); encoder.Save(file);
    }
}
