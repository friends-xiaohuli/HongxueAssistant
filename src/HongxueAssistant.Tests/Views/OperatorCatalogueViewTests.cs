using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HongxueAssistant.App;
using HongxueAssistant.App.Controls;
using HongxueAssistant.App.Views;
using HongxueAssistant.Core.Data;
using HongxueAssistant.Core.Models;
using HongxueAssistant.Core.ViewModels;
using FluentAssertions;

namespace HongxueAssistant.Tests.Views;

internal static class OperatorCatalogueViewTests
{
    public static void Verify(GameShopTheme theme, bool night)
    {
        ThemeManager.Apply(theme, night);
        var store = new JsonDataStore(Path.Combine(AppContext.BaseDirectory, "data"));
        var operators = store.LoadOperatorsAsync().GetAwaiter().GetResult();
        var custom = store.LoadCustomOperatorsAsync().GetAwaiter().GetResult();
        operators.AddRange(custom);
        var pith = custom.Single(o => o.Name == "Pith(卫戍协议)");
        pith.IsSelectedCustom = true;
        var vm = new OperatorBrowseViewModel { SelectedOperatorSource = OperatorSourceFilter.Default, SearchText = PlannerStrategies.PithName };
        vm.LoadOperators(operators);
        var tracking = new TrackingViewModel();
        var view = new OperatorBrowseView { DataContext = vm, IsTrackedCheck = tracking.IsTracked };
        view.TrackingRequested += (name, type) => tracking.AddTracking(name, type);
        view.UntrackingRequested += tracking.RemoveTracking;
        var host = new Window { Content = view, Width = 360, Height = 850, Opacity = 0, ShowActivated = false,
            ShowInTaskbar = false, Left = -20000, Top = -20000, WindowStyle = WindowStyle.None };
        host.Show();
        try
        {
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            view.UpdateLayout();
            var support = vm.FilteredOperators.Single();
            support.Name.Should().Be(PlannerStrategies.PithName);
            var card = Desc<OperatorCard>(view).Single(c => ((Operator)c.DataContext).Name == support.Name);
            ((Button)card.FindName("TrackButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            tracking.IsTracked(support.Name).Should().BeTrue();
            Save(view, theme, night, "support");
            var details = new ContentControl { Content = support,
                ContentTemplate = (DataTemplate)Application.Current.FindResource("OperatorDetailsTemplate") };
            var tooltip = new ToolTip { Content = details, DataContext = support };
            tooltip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            tooltip.Arrange(new Rect(tooltip.DesiredSize));
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            details.UpdateLayout();
            details.ActualWidth.Should().BeGreaterThanOrEqualTo(464);
            Desc<TextBlock>(details).Select(t => t.Text).Should().Contain(support.Name);
            Desc<TextBlock>(details).SelectMany(t => t.Inlines.OfType<Run>()).Select(r => r.Text)
                .Should().Contain("战术咏唱·双型");
            Save(details, theme, night, "support-details");

            ((ListBox)view.FindName("OperatorSourceList")).SelectedValue = OperatorSourceFilter.Custom;
            ((TextBox)view.FindName("SearchBox")).Text = "Pith";
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            view.UpdateLayout();
            vm.FilteredOperators.Should().Equal(pith);
            Desc<OperatorCard>(view).Should().Contain(c => ((Operator)c.DataContext).Name == pith.Name);
            pith.AdditionalCovenants.Should().Equal("协防干员");
            pith.Normal.Skill.Should().BeNull();
            Save(view, theme, night, "custom-pith");
        }
        finally { host.Close(); }
    }

    private static IEnumerable<T> Desc<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T item) yield return item;
            foreach (var nested in Desc<T>(child)) yield return nested;
        }
    }

    private static void Save(FrameworkElement view, GameShopTheme theme, bool night, string suffix)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "TestResults", "operator-catalogue");
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(directory, $"{theme}-{night}-{suffix}.png"));
        png.Save(file);
    }
}
