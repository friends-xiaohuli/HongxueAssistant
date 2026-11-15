using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HongxueAssistant.App;
using HongxueAssistant.App.Converters;
using HongxueAssistant.App.Views;
using HongxueAssistant.App.Windows;
using HongxueAssistant.Core.Models;
using HongxueAssistant.Core.Services;
using HongxueAssistant.Core.ViewModels;
using FluentAssertions;

namespace HongxueAssistant.Tests.Views;

internal static class CandidateEquipmentViewTests
{
    public static void Verify()
    {
        Operator[] operators = [new() { Name = "甲", Tier = 1 }];
        Equipment[] equipment = [new() { Name = "装备格", Tier = 1 }, new() { Name = "候选甲", Tier = 2 }, new() { Name = "候选乙", Tier = 3 }];
        var calculator = new CovenantCalculator(operators, [], new Dictionary<string, string>());
        foreach (var theme in Enum.GetValues<GameShopTheme>())
        foreach (var night in new[] { false, true })
        {
            ThemeManager.Apply(theme, night);
            var picker = new SimplePickerDialog(equipment, "甲", [new() { Name = "候选甲" }], ["装备格"])
            { ShowActivated = false, ShowInTaskbar = false, Opacity = 0, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 };
            var action = Dispatcher.CurrentDispatcher.InvokeAsync(() =>
            {
                try
                {
                    var list = (ListBox)picker.FindName("ItemsList");
                    SimplePickerDialog.PickerItem Item(string key) => list.Items.Cast<SimplePickerDialog.PickerItem>().Single(i => i.Key == key);
                    list.SelectedItem = Item("装备格");
                    Item("装备格").RightLabel.Should().Be("装备格");
                    list.SelectedItem = Item("候选甲");
                    Item("候选甲").RightLabel.Should().Be("★ 优先");
                    list.SelectedItem = Item("候选甲");
                    Item("候选甲").RightLabel.Should().BeNull();
                    list.SelectedItem = Item("候选甲");
                    Item("候选甲").RightLabel.Should().Be("候选");
                    var search = (TextBox)picker.FindName("SearchBox");
                    search.Text = "候选乙";
                    list.Items.Count.Should().Be(1);
                    list.SelectedItem = Item("候选乙");
                    search.Text = "";
                    list.SelectedItem = Item("候选乙");
                    Item("候选乙").RightLabel.Should().Be("★ 优先");
                    ((Button)picker.FindName("ConfirmButton")).Content.Should().Be("完成 · 2 件");
                    foreach (var width in new[] { 420, 560 })
                    {
                        picker.Width = width;
                        Pump();
                        var hint = (TextBlock)picker.FindName("HintText");
                        new FormattedText(hint.Text, CultureInfo.CurrentCulture, hint.FlowDirection,
                            new Typeface(hint.FontFamily, hint.FontStyle, hint.FontWeight, hint.FontStretch),
                            hint.FontSize, hint.Foreground, VisualTreeHelper.GetDpi(hint).PixelsPerDip)
                            .Width.Should().BeLessThanOrEqualTo(hint.ActualWidth + 1);
                        Save((FrameworkElement)picker.Content, $"{theme}-{night}-{width}-picker");
                    }
                    list.UpdateLayout();
                    var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(Item("候选乙"));
                    var summary = (ToolTip)row.ToolTip;
                    summary.PlacementTarget = row;
                    summary.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    summary.Arrange(new Rect(summary.DesiredSize));
                    summary.UpdateLayout();
                    ((SolidColorBrush)((TextBlock)summary.Content).Foreground).Color.Should().Be(((SolidColorBrush)picker.FindResource("TitleTextBrush")).Color);
                    Save(summary, $"{theme}-{night}-candidate-summary");
                    ((Button)picker.FindName("ConfirmButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                finally { picker.Close(); }
            }, DispatcherPriority.ApplicationIdle);
            var result = picker.ShowDialog();
            action.Task.GetAwaiter().GetResult();
            result.Should().BeTrue();
            picker.Candidates!.Select(c => (c.Name, c.IsPriority)).Should().Equal(("候选甲", false), ("候选乙", true));

            var source = new Lineup { Slots = [new() { OperatorName = "甲", Equipments = ["装备格"],
                CandidateEquipments = [new() { Name = "候选甲" }, new() { Name = "候选乙", IsPriority = true }] }] };
            var vm = new LineupEditorViewModel(source, operators, equipment, calculator);
            var editor = new LineupEditorWindow(vm, []);
            var content = (FrameworkElement)editor.Content;
            editor.Content = null;
            var host = new Window { Content = content, Width = 1000, Height = 760, WindowStyle = WindowStyle.None,
                Opacity = 0, ShowActivated = false, ShowInTaskbar = false, Left = -20000, Top = -20000 };
            host.Show();
            foreach (var width in new[] { 760, 1000 })
            {
                host.Width = width;
                Pump();
                Save(content, $"{theme}-{night}-{width}-editor");
            }
            Button Chip(string name) => Descendants<Button>(content).Single(b => b.ToolTip is string tip && tip.Contains(name + "（右键更多）"));
            ((MenuItem)Chip("候选甲").ContextMenu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            vm.Slots[0].CandidateEquipments.Single(c => c.Name == "候选甲").IsPriority.Should().BeTrue();
            ((MenuItem)Chip("候选甲").ContextMenu.Items[1]).Visibility.Should().Be(Visibility.Visible);
            ((MenuItem)Chip("候选甲").ContextMenu.Items[1]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            vm.Slots[0].Equipments.Should().Equal("装备格", "候选甲");
            ((MenuItem)Chip("候选乙").ContextMenu.Items[1]).Visibility.Should().Be(Visibility.Collapsed);
            ((MenuItem)Chip("候选乙").ContextMenu.Items[2]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            vm.Slots[0].CandidateEquipments.Should().BeEmpty();
            host.Close();
            editor.Close();

            var tracking = new TrackingViewModel();
            tracking.TrackLineup(source);
            var view = new TrackingView { DataContext = tracking };
            view.SetOperators(operators);
            view.SetEquipment(equipment);
            view.SetCovenantCalculator(calculator);
            var trackingHost = new Window { Content = view, Width = 280, Height = 900, WindowStyle = WindowStyle.None,
                Opacity = 0, ShowActivated = false, ShowInTaskbar = false, Left = -20000, Top = -20000 };
            trackingHost.Show();
            foreach (var width in new[] { 280, 360 })
            {
                trackingHost.Width = width;
                Pump();
                var cells = (ItemsControl)view.FindName("EquipmentCells");
                Descendants<TextBlock>(cells).Should().Contain(t => t.Text == "候选" && t.IsVisible);
                Descendants<TextBlock>(cells).Should().Contain(t => t.Text == "★ 优先" && t.IsVisible);
                Save(view, $"{theme}-{night}-{width}-tracking");
            }
            var description = new EquipmentWantersConverter().Convert([tracking.TrackedEquipment.Single(e => e.Name == "候选乙"), tracking],
                typeof(string), null!, CultureInfo.InvariantCulture);
            description.Should().Be("优先：甲");
            trackingHost.Close();
        }
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T value) yield return value;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private static void Save(FrameworkElement view, string name)
    {
        var output = Path.Combine(AppContext.BaseDirectory, "TestResults", "candidate-equipment");
        Directory.CreateDirectory(output);
        var size = view.RenderSize;
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen()) context.DrawRectangle(new VisualBrush(view), null, new Rect(size));
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(output, name + ".png"));
        encoder.Save(file);
    }
}
