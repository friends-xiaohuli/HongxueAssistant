using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

internal static class OperatorPickerPerformanceViewTests
{
    public static void VerifyInteraction(GameShopTheme theme, bool night)
    {
        ThemeManager.Apply(theme, night);
        var data = new JsonDataStore(Path.Combine(AppContext.BaseDirectory, "data"));
        var operators = data.LoadOperatorsAsync().GetAwaiter().GetResult();
        var (covenants, mappings) = data.LoadCovenantsAsync().GetAwaiter().GetResult();
        var calculator = new CovenantCalculator(operators, covenants, mappings);
        var vm = new LineupEditorViewModel(new Lineup { Slots = [new() { OperatorName = "角峰" }],
            TargetCovenants = [new() { Name = "谢拉格", Tier = 2 }] }, operators, [], calculator);
        var context = new OperatorPickerContext("后期 · 第 2 位", "", new HashSet<string> { "角峰" },
            vm.CountsWith(1, ""), name => vm.CountsWith(1, name), vm.TargetStates, vm.AllCovenants);
        foreach (var width in new[] { 680, 1000 })
        {
            var picker = new OperatorPickerWindow(operators.Where(o => o.Name != LineupEquipmentService.MechName), "", new HashSet<string>(), context);
            try
            {
                Show(picker, width, 690);
                var list = (ListBox)picker.FindName("OperatorsList");
                var search = (TextBox)picker.FindName("SearchBox");
                var fullCount = list.Items.Count;
                fullCount.Should().BeGreaterThan(100);
                var unavailable = operators.Single(o => o.Name == "角峰");
                list.ScrollIntoView(unavailable);
                Pump();
                var disabled = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(unavailable);
                disabled.IsEnabled.Should().BeFalse();
                list.SelectedItem = unavailable;
                Enter(list, picker);
                picker.Result.Should().BeNull();
                picker.IsVisible.Should().BeTrue();
                var strategyRequired = list.Items.Cast<Operator>().First(o => o.RequiredStrategyId.Length > 0);
                list.ScrollIntoView(strategyRequired);
                Pump();
                ((ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(strategyRequired)).IsEnabled.Should().BeFalse();

                ((ListBox)picker.FindName("ScopeList")).SelectedIndex = 1;
                Pump();
                list.Items.Count.Should().BeGreaterThan(0).And.BeLessThan(fullCount);
                list.Items.Cast<Operator>().Should().OnlyContain(op =>
                    ((OperatorEffect)OperatorPickerWindow.EffectOf.Convert(op.Name, typeof(OperatorEffect), null!, System.Globalization.CultureInfo.InvariantCulture)!).HelpsTarget);
                ((ListBox)picker.FindName("ScopeList")).SelectedIndex = 0;
                Pump();
                list.Items.Count.Should().Be(fullCount);

                search.Text = "迁移测试中不存在的名字";
                Pump();
                list.Items.Count.Should().Be(0);
                search.Clear();
                Pump();
                list.Items.Count.Should().Be(fullCount);
                var chosen = list.Items.Cast<Operator>().Last(o => o.RequiredStrategyId.Length == 0 && o.Name != "角峰");
                search.Text = chosen.Name;
                Pump();
                list.Items.Cast<Operator>().Should().Contain(chosen);
                list.SelectedItem = chosen;
                list.ScrollIntoView(chosen);
                Pump();
                Enter(list, picker);
                picker.Result.Should().Be(chosen.Name);
            }
            finally { picker.Close(); }

            var mousePicker = new OperatorPickerWindow(operators.Take(30), "", new HashSet<string>());
            try
            {
                Show(mousePicker, width, 690);
                var list = (ListBox)mousePicker.FindName("OperatorsList");
                var chosen = list.Items.Cast<Operator>().First(o => o.RequiredStrategyId.Length == 0);
                var item = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(chosen);
                item.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                    { RoutedEvent = Mouse.PreviewMouseDownEvent });
                item.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                    { RoutedEvent = Mouse.MouseUpEvent });
                mousePicker.Result.Should().Be(chosen.Name);
            }
            finally { mousePicker.Close(); }
        }
    }

    public static void VerifyVirtualization(GameShopTheme theme, bool night)
    {
        ThemeManager.Apply(theme, night);
        var operators = new JsonDataStore(Path.Combine(AppContext.BaseDirectory, "data")).LoadOperatorsAsync().GetAwaiter().GetResult();
        foreach (var (width, height) in new[] { (680, 520), (1000, 900) })
        {
            var picker = new OperatorPickerWindow(operators, "", new HashSet<string>());
            try
            {
                Show(picker, width, height);
                var list = (ListBox)picker.FindName("OperatorsList");
                var scroll = Descendants<ScrollViewer>(list).First();
                var first = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
                var second = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(1);
                var a = first.TransformToAncestor(list).TransformBounds(new Rect(first.RenderSize));
                var b = second.TransformToAncestor(list).TransformBounds(new Rect(second.RenderSize));
                a.Top.Should().BeApproximately(b.Top, 0.5);
                a.Right.Should().BeLessThanOrEqualTo(b.Left + 0.5);
                b.Right.Should().BeLessThanOrEqualTo(list.ActualWidth + 0.5);
                AssertBounded(list);
                Save(picker, $"{theme}-{night}-{width}-top");

                scroll.ScrollToEnd();
                Pump();
                var last = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(list.Items.Count - 1);
                last.Should().NotBeNull();
                list.ItemContainerGenerator.ContainerFromIndex(0).Should().BeNull();
                var bottom = last.TransformToAncestor(list).TransformBounds(new Rect(last.RenderSize));
                bottom.Top.Should().BeLessThan(list.ActualHeight);
                bottom.Bottom.Should().BeGreaterThan(0);
                AssertBounded(list);
                Save(picker, $"{theme}-{night}-{width}-bottom");

                var middle = list.Items[list.Items.Count / 2];
                list.ScrollIntoView(middle);
                Pump();
                list.ItemContainerGenerator.ContainerFromItem(middle).Should().NotBeNull();
                AssertBounded(list);
                list.Focus();
                foreach (var (key, expected) in new[] { (Key.End, list.Items.Count - 1), (Key.Home, 0), (Key.Down, 2) })
                {
                    list.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(picker), Environment.TickCount, key)
                        { RoutedEvent = Keyboard.KeyDownEvent });
                    Pump();
                    list.SelectedIndex.Should().Be(expected, "keyboard navigation must retain the two-column order");
                    list.ItemContainerGenerator.ContainerFromIndex(expected).Should().NotBeNull();
                }
                picker.Width = width == 680 ? 1000 : 680;
                Pump();
                AssertBounded(list);
                ((TextBox)picker.FindName("SearchBox")).Text = ((Operator)middle).Name;
                Pump();
                list.Items.Cast<Operator>().Should().Contain((Operator)middle);
                scroll.VerticalOffset.Should().Be(0);
            }
            finally { picker.Close(); }
        }
    }

    private static void AssertBounded(ListBox list)
    {
        Enumerable.Range(0, list.Items.Count).Count(i => list.ItemContainerGenerator.ContainerFromIndex(i) is not null)
            .Should().BeInRange(1, (int)Math.Ceiling(list.ActualHeight / 60) * 2 + 4);
    }

    private static void Enter(ListBox list, Window picker) => list.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,
        PresentationSource.FromVisual(picker), Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });

    private static void Show(Window picker, double width, double height)
    {
        picker.ShowActivated = false;
        picker.ShowInTaskbar = false;
        picker.Opacity = 0;
        picker.WindowStartupLocation = WindowStartupLocation.Manual;
        picker.Left = picker.Top = -20000;
        picker.Width = width;
        picker.Height = height;
        picker.Show();
        Pump();
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static void Save(Window window, string name)
    {
        var content = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var directory = Path.Combine(AppContext.BaseDirectory, "TestResults", "operator-picker-performance");
        Directory.CreateDirectory(directory);
        using var file = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(file);
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
