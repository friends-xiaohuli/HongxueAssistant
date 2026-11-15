using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HongxueAssistant.App;
using HongxueAssistant.App.Controls;
using HongxueAssistant.App.Views;
using HongxueAssistant.Core.Data;
using HongxueAssistant.Core.GameState;
using HongxueAssistant.Core.Models;
using HongxueAssistant.Core.Services;
using HongxueAssistant.Core.ViewModels;
using FluentAssertions;

namespace HongxueAssistant.Tests.Views;

internal static class EquipmentCompletionViewTests
{
    public static void Verify(GameShopTheme theme, bool night)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        ThemeManager.Apply(theme, night);
        var folder = Path.Combine(Path.GetTempPath(), "equipment-completion-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var settings = new SettingsManager(folder);
        Operator[] operators = [new() { Name = "角峰", Tier = 1 }];
        Equipment[] equipment = [new() { Name = "正式装备", Tier = 1 }, new() { Name = "优先装备", Tier = 2 }, new() { Name = "候选装备", Tier = 3 }];
        var calculator = new CovenantCalculator(operators, [], new Dictionary<string, string>());
        var pool = new DeployPoolState();
        var vm = new TrackingViewModel(); vm.SetDeployPool(pool);
        vm.TrackLineup(new Lineup { Id = "a", Slots = [new() { OperatorName = "角峰", IsElite = true, Equipments = ["正式装备"],
            CandidateEquipments = [new() { Name = "优先装备", IsPriority = true }, new() { Name = "候选装备" }] }] });
        var view = new TrackingView { DataContext = vm };
        view.SetOperators(operators); view.SetEquipment(equipment); view.SetCovenantCalculator(calculator);
        view.ToggleAcquiredRequested += vm.ToggleAcquired;
        var deploy = (DeployView)view.FindName("DeployPage");
        deploy.Initialize(operators.ToDictionary(o => o.Name), [], null, vm, settings, pool, false);
        var host = new Window { Content = view, Width = 280, Height = 900, Opacity = 0, ShowActivated = false, ShowInTaskbar = false,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Left = -20000, Top = -20000 };
        host.Show();
        try
        {
            var pages = (ListBox)view.FindName("TrackingPageList");
            pages.SelectedIndex = 1; deploy.FocusOperator("角峰"); Pump();
            var card = Desc<Grid>((StackPanel)deploy.FindName("PoolGroups")).Single(g => g.ToolTip is string t && t.StartsWith("角峰 · "));
            card.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent }); Pump();
            vm.OperatorProgress(vm.TrackedOperators.Single()).Should().Be("待精锐");
            vm.TrackedEquipment.Should().HaveCount(3);
            card = Desc<Grid>((StackPanel)deploy.FindName("PoolGroups")).Single(g => g.ToolTip is string t && t.StartsWith("角峰 · "));
            Desc<Border>(card).Single(b => b.ToolTip is string t && t.StartsWith("普通 · "))
                .RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent }); Pump();
            vm.OperatorProgress(vm.TrackedOperators.Single()).Should().Be("已完成");
            pages.SelectedIndex = 0; Pump();
            var gear = (ItemsControl)view.FindName("EquipmentCells");
            var cells = Desc<EquipmentCell>(gear).ToList();
            cells.Should().HaveCount(3);
            var badges = Desc<Border>(gear).Where(b => b.Name == "kindBadge" && b.Visibility == Visibility.Visible).ToList();
            badges.Should().HaveCount(2);
            foreach (var badge in badges)
                badge.ToolTip.Should().BeOfType<string>().Which.Should().Contain("角峰");
            List<ShopItem> shop = [new() { Name = "角峰" }, .. equipment.Select(e => new ShopItem { Name = e.Name })];
            GameStateUpdater.UpdateShopTracking(shop, vm.ActiveEntries.ToList());
            shop.Single(i => i.Name == "角峰").IsTracked.Should().BeFalse();
            shop.Where(i => i.Name != "角峰").Should().OnlyContain(i => i.IsTracked);
            Save(view, theme, night, "completed-operator");

            ((Button)cells.Single(c => ((Equipment)c.DataContext).Name == "正式装备").FindName("AcquireButton"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            vm.TrackedEquipment.Single(e => e.Name == "正式装备").IsAcquired.Should().BeTrue();
            gear.Items.Count.Should().Be(2);
            Wait(settings.SaveTrackingStateAsync(vm.CaptureState())); Wait(settings.SaveDeployPoolAsync(pool));
            var restoredPool = Wait(settings.LoadDeployPoolAsync());
            var restored = new TrackingViewModel(); restored.SetDeployPool(restoredPool);
            restored.LoadState(Wait(settings.LoadTrackingStateAsync(calculator.EquipmentRules)));
            var reloaded = new TrackingView { DataContext = restored };
            reloaded.SetOperators(operators); reloaded.SetEquipment(equipment); reloaded.SetCovenantCalculator(calculator);
            reloaded.ToggleAcquiredRequested += restored.ToggleAcquired;
            host.Content = reloaded; Pump();
            restored.IsTracked("角峰").Should().BeFalse();
            restored.IsTracked("正式装备").Should().BeFalse();
            restored.IsTracked("优先装备").Should().BeTrue();
            restored.IsTracked("候选装备").Should().BeTrue();
            gear = (ItemsControl)reloaded.FindName("EquipmentCells");
            gear.Items.Count.Should().Be(2);
            badges = Desc<Border>(gear).Where(b => b.Name == "kindBadge" && b.Visibility == Visibility.Visible).ToList();
            badges.Should().HaveCount(2);
            foreach (var badge in badges)
                badge.ToolTip.Should().BeOfType<string>().Which.Should().Contain("角峰");
            Save(reloaded, theme, night, "reloaded");
            restored.RemoveTracking("角峰"); Pump();
            restored.IsTracked("优先装备").Should().BeFalse();
            restored.IsTracked("候选装备").Should().BeFalse();
            restored.EquipmentWanters("正式装备").Should().BeEmpty();
        }
        finally { host.Close(); Directory.Delete(folder, true); }
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Wait(Task task)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!task.IsCompleted && DateTime.UtcNow < until) { Pump(); Thread.Sleep(5); }
        task.IsCompleted.Should().BeTrue(); task.GetAwaiter().GetResult(); Pump();
    }
    private static T Wait<T>(Task<T> task) { Wait((Task)task); return task.Result; }
    private static IEnumerable<T> Desc<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var nested in Desc<T>(child)) yield return nested;
        }
    }
    private static void Save(FrameworkElement view, GameShopTheme theme, bool night, string suffix)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "TestResults", "equipment-completion"); Directory.CreateDirectory(directory);
        var until = DateTime.UtcNow.AddMilliseconds(250);
        while (DateTime.UtcNow < until) { Pump(); Thread.Sleep(5); }
        var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(directory, $"{theme}-{night}-{suffix}.png")); png.Save(file);
    }
}
