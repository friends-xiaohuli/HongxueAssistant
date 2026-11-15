using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HongxueAssistant.App;
using HongxueAssistant.App.Views;
using HongxueAssistant.Core.Models;
using HongxueAssistant.Core.Services;
using HongxueAssistant.Core.ViewModels;
using FluentAssertions;

namespace HongxueAssistant.Tests.Views;

internal static class PlannerLockedBanViewTests
{
    public static void Verify(GameShopTheme theme, bool night)
    {
        Operator[] operators = [
            new() { Name = "手动禁用必带", CoreCovenants = ["维多利亚"] },
            new() { Name = "盟约禁用必带", CoreCovenants = ["炎"] },
            new() { Name = "正常必带", CoreCovenants = ["维多利亚"] }];
        CovenantInfo[] covenants = [
            new() { Name = "炎", BondType = "SEASON", ActivateCount = 1 },
            new() { Name = "维多利亚", BondType = "SEASON", ActivateCount = 1 }];
        var bans = new BanViewModel();
        var vm = new LineupPlannerViewModel(new LineupPlanner(operators, covenants), operators, bans, EquipmentTestData.Rules);
        foreach (var op in operators) vm.AddLocked(op.Name);
        var view = new LineupPlannerView { DataContext = vm };
        var host = new Window { Content = view, Opacity = 0, ShowActivated = false, ShowInTaskbar = false,
            Left = -20000, Top = -20000, Height = 850 };
        try
        {
            host.Show();
            ThemeManager.Apply(theme, night);
            var list = (ItemsControl)view.FindName("LockedOperatorsList");
            foreach (var width in new[] { 280, 360, 400 })
            {
                host.Width = width;
                vm.ShowResults = false;
                bans.SetBans(["手动禁用必带"]);
                bans.SetCovenantBans(["炎"]);
                Pump();
                list.Items.Count.Should().Be(3);
                foreach (var item in vm.LockedOperators)
                {
                    var chip = Desc<Border>(list).Single(b => ReferenceEquals(b.Tag, item.Operator));
                    chip.Opacity.Should().Be(item.Name == "正常必带" ? 1 : 0.55);
                    var marker = Desc<TextBlock>(chip).Single(t => t.Text == "已 Ban");
                    marker.Visibility.Should().Be(item.Name == "正常必带" ? Visibility.Collapsed : Visibility.Visible);
                    marker.ToolTip.Should().Be("本局暂不计入必带，解除 Ban 后自动恢复");
                    var bounds = chip.TransformToAncestor(list).TransformBounds(new Rect(0, 0, chip.ActualWidth, chip.ActualHeight));
                    bounds.Right.Should().BeLessThanOrEqualTo(list.ActualWidth + 0.5);
                }
                Save(view, theme, night, width, "banned");
                vm.ShowResults = true;
                Pump();
                var count = (TextBlock)view.FindName("ActiveLockedCountText");
                count.IsVisible.Should().BeTrue();
                count.Inlines.OfType<Run>().Last().Text.Should().Be("1");
                Save(view, theme, night, width, "count");

                bans.ClearBans();
                Pump();
                count.Inlines.OfType<Run>().Last().Text.Should().Be("3");
                vm.ShowResults = false;
                Pump();
                Desc<Border>(list).Where(b => b.Tag is Operator).Should().OnlyContain(b => b.Opacity == 1);
                Desc<TextBlock>(list).Where(t => t.Text == "已 Ban").Should().OnlyContain(t => t.Visibility == Visibility.Collapsed);
                Save(view, theme, night, width, "restored");
            }

            bans.SetBans(["手动禁用必带"]);
            Pump();
            var paused = Desc<Border>(list).Single(b => ReferenceEquals(b.Tag, operators[0]));
            var remove = Desc<Button>(paused).Single();
            remove.IsEnabled.Should().BeTrue();
            remove.Command.Execute(remove.CommandParameter);
            Pump();
            vm.LockedOperators.Select(item => item.Name).Should().Equal("盟约禁用必带", "正常必带");
            vm.ActiveLockedCount.Should().Be(2);
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

    private static void Save(FrameworkElement view, GameShopTheme theme, bool night, int width, string state)
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "TestResults", "planner-locked-ban");
        Directory.CreateDirectory(folder);
        var image = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth), (int)Math.Ceiling(view.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        image.Render(view);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(Path.Combine(folder, $"{theme}-{night}-{width}-{state}.png"));
        encoder.Save(stream);
    }
}
