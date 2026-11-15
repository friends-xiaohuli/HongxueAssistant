using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HongxueAssistant.App;
using HongxueAssistant.App.Controls;
using HongxueAssistant.App.Windows;
using HongxueAssistant.Core.Data;
using HongxueAssistant.Core.Models;
using HongxueAssistant.Core.Services;
using HongxueAssistant.Core.ViewModels;
using FluentAssertions;

namespace HongxueAssistant.Tests.Views;

internal static class EditorRedesignViewTests
{
    public static void Verify(GameShopTheme theme, bool night)
    {
        var data = new JsonDataStore(Path.Combine(AppContext.BaseDirectory, "TestData", "Regulation"));
        var operators = data.LoadOperatorsAsync().GetAwaiter().GetResult();
        var (equipment, _) = data.LoadEquipmentAsync().GetAwaiter().GetResult();
        var (covenants, map) = data.LoadCovenantsAsync().GetAwaiter().GetResult();
        var strategies = data.LoadStrategiesAsync().GetAwaiter().GetResult();
        var calculator = new CovenantCalculator(operators, covenants, map);
        SearchMatch.LoadFile(Path.Combine(AppContext.BaseDirectory, "data", "search_keys.json"));
        var gear = equipment.Where(e => e.Type != EquipmentType.JobChange && e.Name != CovenantCalculator.ShapeshifterEquipment)
            .OrderBy(e => e.Tier).Take(4).ToList();
        var people = operators.Where(o => !o.IsCustom && o.RequiredStrategyId.Length == 0).Take(4).ToList();
        foreach (var wide in new[] { false, true })
        {
            ThemeManager.Apply(theme, night);
            var prefix = $"{theme}-{night}-{(wide ? "wide" : "narrow")}";
            var savedStrategies = new List<LineupStrategySlot>
            {
                new() { StrategyId = strategies[0].Id, Tags = [StrategyTag.阵容核心, StrategyTag.优选] },
                new() { StrategyId = "shared-unknown", Tags = [StrategyTag.优选, StrategyTag.次选] },
                new() { StrategyId = strategies[1].Id, Tags = [StrategyTag.次选] },
            };
            var source = new Lineup
            {
                Name = "编辑器回归", SourceUrl = "https://www.skland.com/article?id=example",
                Slots = [new() { OperatorName = people[0].Name, IsElite = true, Equipments = [gear[0].Name],
                    CandidateEquipments = [new() { Name = gear[1].Name, IsPriority = true }], Tags = [LineupTag.阵容核心] },
                    new() { OperatorName = people[1].Name }],
                EarlySlots = [new() { OperatorName = people[2].Name }], MidSlots = [new() { OperatorName = people[3].Name }],
                SupportSlots = [new() { OperatorName = people[2].Name, Tags = [LineupTag.输出] }],
                Strategies = savedStrategies, TargetCovenants = [new() { Name = "乌萨斯", Tier = 2 }]
            };
            var vm = new LineupEditorViewModel(source, operators, equipment, calculator);
            var editor = new LineupEditorWindow(vm, strategies);
            Run(editor, wide, () =>
            {
                ((FrameworkElement)editor.FindName("SourcePanel")).IsVisible.Should().BeTrue();
                ((TextBox)editor.FindName("LineupNameBox")).ActualWidth.Should().BeGreaterThan(100);
                ((FrameworkElement)((ItemsControl)editor.FindName("SlotsHost")).Items[0]).ActualWidth.Should().BeGreaterThanOrEqualTo(200);
                Descendants<CheckBox>(editor).Should().Contain(c => Equals(c.Content, "精锐") && c.IsChecked == true);
                Save(editor, prefix + "-editor");
                ((Button)editor.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            });
            editor.Result.Should().NotBeNull();
            editor.Result!.SourceUrl.Should().Be(source.SourceUrl);
            editor.Result.Slots[0].CandidateEquipments.Single().IsPriority.Should().BeTrue();
            editor.Result.Strategies[0].Tags.Should().Equal(source.Strategies[0].Tags);
            editor.Result.EarlySlots.Should().BeEquivalentTo(source.EarlySlots);
            editor.Result.MidSlots.Should().BeEquivalentTo(source.MidSlots);
            editor.Result.SupportSlots.Should().BeEquivalentTo(source.SupportSlots);

            var picker = new OperatorPickerWindow(operators, "", new HashSet<string>());
            Run(picker, wide, () =>
            {
                var search = (TextBox)picker.FindName("SearchBox");
                var chosen = operators.First(o => !o.IsCustom && o.RequiredStrategyId.Length == 0 && SearchMatch.InitialsOf(o.Name).Length > 0);
                search.Text = SearchMatch.InitialsOf(chosen.Name).ToUpperInvariant();
                Pump();
                var list = (ListBox)picker.FindName("OperatorsList");
                list.Items.Cast<Operator>().Should().Contain(o => o.Name == chosen.Name);
                Save(picker, prefix + "-operators");
                picker.Close();
            });
            picker.Result.Should().BeNull("closing a picker is cancellation");

            var single = new EquipmentPickerWindow(equipment, calculator.EquipmentRules, people[0].Name + " · 装备格 2", gear[0].Name, false);
            Run(single, wide, () =>
            {
                var list = (ListBox)single.FindName("ItemsList");
                var row = list.Items.Cast<EquipmentPickRow>().Single(r => r.Name == gear[2].Name);
                list.ScrollIntoView(row);
                Pump();
                MouseClick((ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(row));
                ((ListBox)single.FindName("CoreMode")).SelectedIndex = 1;
                Save(single, prefix + "-equipment");
                ((Button)single.FindName("ConfirmButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            });
            single.Result.Should().Be(gear[2].Name);
            single.IsCoreEquipment.Should().BeTrue();

            var candidates = new EquipmentPickerWindow(equipment, calculator.EquipmentRules, people[0].Name,
                [new() { Name = gear[1].Name }], [gear[0].Name]);
            Run(candidates, wide, () =>
            {
                var list = (ListBox)candidates.FindName("ItemsList");
                list.Items.Cast<EquipmentPickRow>().Should().NotContain(r => r.Name == gear[0].Name);
                var row = list.Items.Cast<EquipmentPickRow>().Single(r => r.Name == gear[2].Name);
                list.ScrollIntoView(row);
                Pump();
                var container = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(row);
                MouseClick(container);
                Pump();
                Descendants<Button>(container).Single(b => Equals(b.ToolTip, "设为优先")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                row.IsPriority.Should().BeTrue();
                ((TextBox)candidates.FindName("SearchBox")).Text = gear[1].Name;
                Pump();
                ((TextBox)candidates.FindName("SearchBox")).Clear();
                Pump();
                Save(candidates, prefix + "-candidates");
                ((Button)candidates.FindName("ConfirmButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            });
            candidates.Candidates.Should().HaveCount(2);
            candidates.Candidates!.Single(c => c.Name == gear[2].Name).IsPriority.Should().BeTrue();
            candidates.Candidates.Single(c => c.Name == gear[1].Name).IsPriority.Should().BeFalse();

            var strategy = new StrategyPickerWindow(strategies, savedStrategies);
            Run(strategy, wide, () =>
            {
                ((ItemsControl)strategy.FindName("ChosenList")).Items.Count.Should().Be(2, "multiple known strategies remain selected");
                Save(strategy, prefix + "-strategies");
                Descendants<Button>(strategy).Single(b => Equals(b.Content, "完成")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            });
            strategy.Picked.Should().HaveCount(3);
            strategy.Picked.Select(s => s.StrategyId).Should().Equal(savedStrategies.Select(s => s.StrategyId));
            strategy.Picked[0].Tags.Should().Equal(StrategyTag.阵容核心, StrategyTag.优选);
            strategy.Picked.Single(s => s.StrategyId == "shared-unknown").Tags.Should().Equal(StrategyTag.优选, StrategyTag.次选);
            vm.SetStrategies(strategy.Picked);
            vm.ToLineup().Strategies.Should().BeEquivalentTo(savedStrategies);

            var crowded = new StrategyPickerWindow(strategies, []);
            Run(crowded, wide, () =>
            {
                var list = (ListBox)crowded.FindName("ItemsList");
                var rows = list.Items.Cast<StrategyPickRow>().Take(10).ToList();
                foreach (var row in rows)
                {
                    list.ScrollIntoView(row);
                    Pump();
                    MouseClick((ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(row));
                    Pump();
                }
                ((ItemsControl)crowded.FindName("ChosenList")).Items.Count.Should().Be(9);
                rows[9].IsChosen.Should().BeFalse("a tenth choice must not be silently discarded when saved");
                ((TextBlock)crowded.FindName("HintText")).Text.Should().Contain("9");
                Save(crowded, prefix + "-strategy-capacity");
                Descendants<Button>(crowded).Single(b => Equals(b.Content, "完成")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            });
            crowded.Picked.Should().HaveCount(9);

            var target = new TargetPickerWindow(covenants, vm.ComputeLate(), vm.TargetCovenants.ToList(), vm.HasSecondTier);
            Run(target, wide, () =>
            {
                var icons = Descendants<CovenantIcon>(target).ToList();
                icons.Should().HaveCount(25);
                icons.Should().NotContain(icon => icon.Covenant == "绝技");
                foreach (var icon in icons)
                {
                    var glyph = Descendants<Border>(icon).Single();
                    glyph.OpacityMask.Should().BeOfType<ImageBrush>();
                    glyph.Visibility.Should().Be(Visibility.Visible);
                    var bitmap = new RenderTargetBitmap((int)icon.ActualWidth, (int)icon.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(icon);
                    var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
                    bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
                    var sample = Enumerable.Range(0, pixels.Length / 4).MaxBy(i => pixels[i * 4 + 3]) * 4;
                    var alpha = pixels[sample + 3];
                    alpha.Should().BeGreaterThan((byte)20, $"{icon.Covenant} must visibly render");
                    var color = ((SolidColorBrush)icon.Brush!).Color;
                    Math.Abs(pixels[sample] - color.B * alpha / 255).Should().BeLessThanOrEqualTo(3);
                    Math.Abs(pixels[sample + 1] - color.G * alpha / 255).Should().BeLessThanOrEqualTo(3);
                    Math.Abs(pixels[sample + 2] - color.R * alpha / 255).Should().BeLessThanOrEqualTo(3);
                }
                var cells = Descendants<Button>(target).Where(b => b.Tag is string).ToList();
                cells.Single(b => Equals(b.Tag, "乌萨斯")).IsEnabled.Should().BeFalse();
                var arcane = cells.Single(b => Equals(b.Tag, "奥术"));
                arcane.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                target.Picked.Should().Equal(("奥术", 1));
                arcane.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                target.Picked.Should().Equal(("奥术", 2));
                arcane.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                target.Picked.Should().BeEmpty();
                arcane.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Save(target, prefix + "-targets");
                ((Button)target.FindName("ConfirmButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            });
            target.Picked.Should().Equal(("奥术", 1));
            source.Slots[0].CandidateEquipments.Single().IsPriority.Should().BeTrue();
            source.Strategies[0].Tags.Should().Equal(StrategyTag.阵容核心, StrategyTag.优选);
        }
        var clear = new StrategyPickerWindow(strategies, [new() { StrategyId = "shared-unknown", Tags = [StrategyTag.优选] }]);
        Run(clear, false, () =>
        {
            Descendants<Button>(clear).Single(b => Equals(b.Content, "全部取消")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Descendants<Button>(clear).Single(b => Equals(b.Content, "完成")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        clear.Picked.Should().BeEmpty("explicitly clearing strategies also clears unrecognised saved strategies");
    }

    private static void Run(Window dialog, bool wide, Action action)
    {
        dialog.ShowActivated = false; dialog.ShowInTaskbar = false; dialog.Opacity = 0;
        dialog.WindowStartupLocation = WindowStartupLocation.Manual; dialog.Left = -20000; dialog.Top = -20000;
        if (!wide) dialog.Width = dialog.MinWidth;
        var operation = dialog.Dispatcher.InvokeAsync(() =>
        {
            try { Pump(); action(); }
            finally { if (dialog.IsVisible) dialog.Close(); }
        }, DispatcherPriority.ApplicationIdle);
        dialog.ShowDialog();
        operation.Task.GetAwaiter().GetResult();
    }

    private static void Save(Window dialog, string name)
    {
        Pump();
        var content = (FrameworkElement)dialog.Content;
        var output = Path.Combine(AppContext.BaseDirectory, "TestResults", "editor-redesign");
        Directory.CreateDirectory(output);
        var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(output, name + ".png")); png.Save(file);
        foreach (var button in Descendants<Button>(content).Where(b => b.IsVisible && (b.Name is "SaveButton" or "ConfirmButton" || Equals(b.Content, "完成"))))
        {
            var bounds = button.TransformToAncestor(content).TransformBounds(new Rect(0, 0, button.ActualWidth, button.ActualHeight));
            bounds.Left.Should().BeGreaterThanOrEqualTo(0);
            bounds.Right.Should().BeLessThanOrEqualTo(content.ActualWidth + 0.5);
        }
    }

    private static void MouseClick(UIElement element) => element.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseUpEvent });
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T value) yield return value;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
