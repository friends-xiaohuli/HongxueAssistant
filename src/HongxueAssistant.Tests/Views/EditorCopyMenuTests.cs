using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using HongxueAssistant.App;
using HongxueAssistant.App.Windows;
using HongxueAssistant.Core.Models;
using HongxueAssistant.Core.Services;
using HongxueAssistant.Core.ViewModels;
using FluentAssertions;

namespace HongxueAssistant.Tests.Views;

internal static class EditorCopyMenuTests
{
    public static void Verify(GameShopTheme theme, bool night)
    {
        ThemeManager.Apply(theme, night);
        Operator[] operators = [new() { Name = "甲", Tier = 2 },
            .. Enumerable.Range(0, 9).Select(i => new Operator { Name = "其他" + i, Tier = 2 })];
        var calculator = new CovenantCalculator(operators, [], new Dictionary<string, string>());
        var source = new LineupSlot
        {
            OperatorName = "甲", IsElite = true, Equipments = ["装备甲"], CoreEquipments = ["装备甲"],
            CandidateEquipments = [new() { Name = "候选甲", IsPriority = true }], Tags = [LineupTag.阵容核心]
        };
        var vm = new LineupEditorViewModel(new Lineup { Slots = [source] }, operators, [], calculator);
        var editor = new LineupEditorWindow(vm, [])
        {
            ShowActivated = false, ShowInTaskbar = false, Opacity = 0,
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000
        };
        editor.Show();
        Pump();
        var hint = (TextBlock)editor.FindName("EditorHint");
        ContextMenu? menuToClose = null;
        ContextMenu Open()
        {
            menuToClose = OpenMenu(editor);
            return menuToClose;
        }
        try
        {
            foreach (var phase in new[] { LineupPhase.前期, LineupPhase.中期, LineupPhase.挂件 })
            {
                var menu = Open();
                var copy = menu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "复制到"));
                copy.Template.FindName("PART_Popup", copy).Should().BeOfType<Popup>("copying requires a visible submenu, not just a header");
                copy.Role.Should().Be(MenuItemRole.SubmenuHeader);
                ((IExpandCollapseProvider)new MenuItemAutomationPeer(copy).GetPattern(PatternInterface.ExpandCollapse)).Expand();
                Pump();
                var popup = (Popup)copy.Template.FindName("PART_Popup", copy);
                popup.IsOpen.Should().BeTrue("expanding the menu must show the destination choices");
                ((System.Windows.Shapes.Path)copy.Template.FindName("submenuArrow", copy)).Visibility.Should().Be(Visibility.Visible);
                var destination = copy.Items.OfType<MenuItem>().Single(i => Equals(i.Header, phase.ToString()));
                destination.IsVisible.Should().BeTrue();
                destination.ActualHeight.Should().BeGreaterThan(0);
                Invoke(destination);
                menu.IsOpen.Should().BeFalse();
                hint.Text.Should().Contain("甲").And.Contain(phase.ToString()).And.Contain("已复制");
                vm.CurrentPhase.Should().Be(LineupPhase.后期);
            }
            var saved = vm.ToLineup();
            foreach (var slots in new[] { saved.EarlySlots, saved.MidSlots, saved.SupportSlots })
                slots.Should().ContainSingle().Which.Should().BeEquivalentTo(source);

            var repeatedMenu = Open();
            var repeatedCopy = repeatedMenu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "复制到"));
            var expand = (IExpandCollapseProvider)new MenuItemAutomationPeer(repeatedCopy).GetPattern(PatternInterface.ExpandCollapse);
            expand.Expand();
            Pump();
            Invoke(repeatedCopy.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "中期")));
            vm.ToLineup().MidSlots.Should().ContainSingle().Which.Should().BeEquivalentTo(source);

            var leafMenu = Open();
            var tag = leafMenu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "输出"));
            tag.IsChecked.Should().BeFalse();
            ((System.Windows.Shapes.Path)tag.Template.FindName("submenuArrow", tag)).Visibility.Should().Be(Visibility.Collapsed);
            Invoke(tag);
            vm.Slots[0].Tags.Should().Contain(LineupTag.输出);
            var checkedMenu = Open();
            var checkedTag = checkedMenu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "输出"));
            checkedTag.IsChecked.Should().BeTrue();
            ((System.Windows.Shapes.Path)checkedTag.Template.FindName("check", checkedTag)).Visibility.Should().Be(Visibility.Visible);
            checkedMenu.IsOpen = false;

            vm.SelectPhase(LineupPhase.前期);
            for (var i = 0; i < 9; i++) vm.SetSlotOperator(i, "其他" + i);
            ((StackPanel)editor.FindName("PhaseTabs")).Children.OfType<Button>()
                .Single(b => Equals(b.DataContext, LineupPhase.后期)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var fullBefore = vm.ToLineup().EarlySlots.Select(s => s.OperatorName).ToList();
            var fullMenu = Open();
            var fullCopy = fullMenu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "复制到"));
            ((IExpandCollapseProvider)new MenuItemAutomationPeer(fullCopy).GetPattern(PatternInterface.ExpandCollapse)).Expand();
            Pump();
            Invoke(fullCopy.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "前期")));
            hint.Text.Should().Be("前期已满，没有复制");
            vm.ToLineup().EarlySlots.Select(s => s.OperatorName).Should().Equal(fullBefore);
            vm.ToLineup().Slots.Should().ContainSingle();
        }
        finally
        {
            if (menuToClose is not null) menuToClose.IsOpen = false;
            editor.Close();
        }
    }

    private static ContextMenu OpenMenu(LineupEditorWindow editor)
    {
        var card = (FrameworkElement)((ItemsControl)editor.FindName("SlotsHost")).Items[0];
        var menu = card.ContextMenu!;
        menu.PlacementTarget = card;
        menu.Opacity = 0;
        menu.IsOpen = true;
        Pump();
        return menu;
    }

    private static void Invoke(MenuItem item)
    {
        ((IInvokeProvider)new MenuItemAutomationPeer(item).GetPattern(PatternInterface.Invoke)).Invoke();
        Pump();
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
}
