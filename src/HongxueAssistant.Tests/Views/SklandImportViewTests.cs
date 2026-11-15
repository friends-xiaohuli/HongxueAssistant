using System.Diagnostics;
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

internal static class SklandImportViewTests
{
    public static void Verify()
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var directory = Path.Combine(Path.GetTempPath(), "skland-import-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Operator[] operators = [new() { Name = "甲", Tier = 2 }, new() { Name = "乙", Tier = 3 },
            .. Enumerable.Range(1, 20).Select(i => new Operator { Name = "候选" + i, Tier = (i - 1) % 6 + 1 })];
        var calculator = new CovenantCalculator(operators, [], new Dictionary<string, string>());
        var lineups = new LineupViewModel(new LineupStore(Path.Combine(directory, "lineups"), calculator.EquipmentRules), calculator);
        var tracking = new TrackingViewModel();
        var operatorVm = new OperatorBrowseViewModel();
        operatorVm.LoadOperators(operators.ToList());
        var panel = new SidePanelWindow(operatorVm, new EquipmentBrowseViewModel(), tracking, new GameStateViewModel(),
            new BanViewModel(), new EnemyViewModel(), lineups,
            new LineupShareService(calculator.EquipmentRules, Path.Combine(directory, "share")), operators, [], [],
            new SettingsManager(directory), GameShopTheme.Terminal, new SidePanelPositionSettings { FreeMovement = true })
        { Opacity = 0, ShowActivated = false, ShowInTaskbar = false, Left = -20000, Top = -20000 };
        try
        {
            panel.Show();
            var count = 0;
            foreach (var theme in Enum.GetValues<GameShopTheme>())
            foreach (var night in new[] { false, true })
            {
                ThemeManager.Apply(theme, night);
                var draft = new Lineup
                {
                    Name = "官方攻略草稿", SourceUrl = Services.SklandGuideImporterTests.Url,
                    Slots = [new() { OperatorName = "甲", IsElite = true }, new() { OperatorName = "乙" }]
                };
                const string notice = "已读取大成阵容 2 人、策略 0 项。推荐装备 20 件未绑定到干员，回合讲解 2 段未导入；请查看来源攻略。技能与模组沿用本地模式数据。检查并保存后才加入我的阵容，不自动追踪。";
                var cancel = panel.OpenEditorAsync(draft, notice);
                var editor = panel.OwnedWindows.OfType<LineupEditorWindow>().Single();
                editor.Width = 760;
                editor.Height = 680;
                editor.UpdateLayout();
                var url = (TextBox)editor.FindName("SourceUrlBox");
                ((Border)editor.FindName("SourcePanel")).Visibility.Should().Be(Visibility.Visible);
                url.Text.Should().Be(draft.SourceUrl);
                url.IsReadOnly.Should().BeTrue();
                ((TextBlock)editor.FindName("ImportNoticeText")).Text.Should().Be(notice);
                url.ActualWidth.Should().BeGreaterThan(100);
                var bounds = url.TransformToAncestor((Visual)editor.Content).TransformBounds(new Rect(url.RenderSize));
                bounds.Right.Should().BeLessThanOrEqualTo(editor.ActualWidth);
                lineups.Lineups.Should().HaveCount(count, "preview must not write the draft");
                Save(editor, $"{theme}-{night}");
                editor.Close();
                Complete(cancel);
                lineups.Lineups.Should().HaveCount(count, "cancel must not save the imported lineup");

                var save = panel.OpenEditorAsync(draft, notice);
                editor = panel.OwnedWindows.OfType<LineupEditorWindow>().Single();
                ((Button)editor.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Complete(save);
                count++;
                lineups.Lineups.Should().HaveCount(count);
                var saved = lineups.Lineups.Single(x => x.Id == draft.Id);
                saved.SourceUrl.Should().Be(draft.SourceUrl);
                saved.Slots[0].IsElite.Should().BeTrue();
                tracking.TrackedOperators.Should().BeEmpty();
                tracking.TrackedEquipment.Should().BeEmpty();
            }
            Directory.GetFiles(Path.Combine(directory, "lineups"), "*.json")
                .Count(path => !Path.GetFileName(path).StartsWith('.')).Should().BeGreaterThanOrEqualTo(6);
            var candidates = new Lineup
            {
                Name = "20 人候选", SourceUrl = Services.SklandGuideImporterTests.Url,
                Slots = operators.Skip(2).Select(op => new LineupSlot { OperatorName = op.Name }).ToList()
            };
            var select = panel.OpenEditorAsync(candidates, "官方列出20名候选，已全部保留；请筛选至9人以内再保存。");
            var chooser = panel.OwnedWindows.OfType<LineupEditorWindow>().Single();
            var host = (ItemsControl)chooser.FindName("SlotsHost");
            host.Items.Count.Should().Be(20);
            ((Button)chooser.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            select.IsCompleted.Should().BeFalse();
            lineups.Lineups.Should().HaveCount(6, "too many candidates must not be saved");
            ((TextBlock)chooser.FindName("EditorHint")).Text.Should().Contain("9");
            for (var i = 0; i < 11; i++)
            {
                var card = (FrameworkElement)host.Items[0];
                Descendants<Button>(card).Single(button => Equals(button.ToolTip, "清空该位置"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            host.Items.Count.Should().Be(9);
            ((Button)chooser.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Complete(select);
            lineups.Lineups.Should().HaveCount(7);
            lineups.Lineups.Single(x => x.Id == candidates.Id).Slots.Should().HaveCount(9);
            candidates.Slots.Should().HaveCount(20);
            tracking.TrackedOperators.Should().BeEmpty();

            var grouped = new Lineup
            {
                Name = "20 人分阶段攻略", SourceUrl = Services.SklandGuideImporterTests.Url,
                EarlySlots = operators.Skip(2).Where(op => op.Tier <= 2).Select(op => new LineupSlot { OperatorName = op.Name }).ToList(),
                MidSlots = operators.Skip(2).Where(op => op.Tier is 3 or 4).Select(op => new LineupSlot { OperatorName = op.Name }).ToList(),
                Slots = operators.Skip(2).Where(op => op.Tier >= 5).Select(op => new LineupSlot { OperatorName = op.Name, IsElite = true }).ToList()
            };
            const string phaseNotice = "官方列出 20 名候选，已按本数分组并全部保留：1–2本前期 8 人、3–4本中期 6 人、5–6本后期 6 人。检查并保存后才加入我的阵容，不自动追踪。";
            foreach (var theme in Enum.GetValues<GameShopTheme>())
            foreach (var night in new[] { false, true })
            {
                ThemeManager.Apply(theme, night);
                var preview = panel.OpenEditorAsync(grouped, phaseNotice);
                var phaseEditor = panel.OwnedWindows.OfType<LineupEditorWindow>().Single();
                phaseEditor.Width = 760;
                phaseEditor.Height = 680;
                var tabs = (Panel)phaseEditor.FindName("PhaseTabs");
                foreach (var (phase, people) in new[] { (LineupPhase.前期, 8), (LineupPhase.中期, 6), (LineupPhase.后期, 6) })
                {
                    var tab = tabs.Children.OfType<Button>().Single(button => Equals(button.DataContext, phase));
                    string.Concat(((TextBlock)tab.Content).Inlines.OfType<System.Windows.Documents.Run>().Select(run => run.Text))
                        .Should().Be($"{phase} {people}");
                    tab.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    ((TextBlock)phaseEditor.FindName("EditorHint")).Text.Should().Contain(phase.ToString());
                    Save(phaseEditor, $"phases-{theme}-{night}-{phase}");
                }
                phaseEditor.Close();
                Complete(preview);
                lineups.Lineups.Should().HaveCount(7);
            }
            var saveGrouped = panel.OpenEditorAsync(grouped, phaseNotice);
            var groupedEditor = panel.OwnedWindows.OfType<LineupEditorWindow>().Single();
            ((Button)groupedEditor.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Complete(saveGrouped);
            var savedGrouped = lineups.Lineups.Single(lineup => lineup.Id == grouped.Id);
            savedGrouped.EarlySlots.Should().HaveCount(8);
            savedGrouped.MidSlots.Should().HaveCount(6);
            savedGrouped.Slots.Should().HaveCount(6).And.OnlyContain(slot => slot.IsElite);
            tracking.TrackedOperators.Should().BeEmpty();
            tracking.TrackLineup(savedGrouped);
            tracking.TrackedOperators.Should().HaveCount(20);
            tracking.TrackedOperators.Count(entry => entry.Role == TrackingRole.Final).Should().Be(6);
            VerifyGuideFlow(operators, calculator);
            VerifyEditableRoundStages(operators, calculator);
        }
        finally
        {
            foreach (Window child in panel.OwnedWindows.Cast<Window>().ToArray()) child.Close();
            panel.Close();
            Directory.Delete(directory, true);
        }
    }

    private static void VerifyGuideFlow(Operator[] operators, CovenantCalculator calculator)
    {
        var source = new Lineup
        {
            Name = "完整攻略草稿", SourceUrl = Services.SklandGuideImporterTests.Url,
            Slots = [new() { OperatorName = "乙", IsElite = true }],
            Guide = new LineupGuide
            {
                GuideId = "123", Title = "攻略流程 · 保留回合阵容和技能模组", Tabs =
                [
                    new() { TabType = 1, OperationIdea = "总体思路", Operators = [new() { OperatorName = "乙", ShopLevel = 3, MergeLevel = 1 }] },
                    new() { TabType = 2, StartRound = 1, EndRound = 4, OperationIdea = "第一行运营思路\n第二行保留完整内容",
                        Operators = [new() { OperatorName = "甲", ShopLevel = 2, SkillId = "skill-a", SkillName = "示例技能", ModuleId = "module-a", ModuleName = "示例模组" }],
                        Equipment = [new() { ItemId = "item-a", Name = "独立推荐装备" }],
                        ActiveBonds = [new() { BondId = "bond-a", Name = "示例盟约" }] }
                ]
            }
        };
        foreach (var theme in Enum.GetValues<GameShopTheme>())
        foreach (var night in new[] { false, true })
        {
            ThemeManager.Apply(theme, night);
            var vm = new LineupEditorViewModel(source, operators, [], calculator);
            var editor = new LineupEditorWindow(vm, [])
            { Opacity = 0, ShowActivated = false, ShowInTaskbar = false, Left = -20000, Top = -20000 };
            LineupGuideWindow? flow = null;
            try
            {
                editor.Show();
                ((Button)editor.FindName("GuideFlowButton")).Visibility.Should().Be(Visibility.Visible);
                flow = new LineupGuideWindow(vm.Guide!, vm.ApplyGuideTab)
                { Owner = editor, Width = 760, Height = 680, Opacity = 0, ShowActivated = false, ShowInTaskbar = false, Left = -20000, Top = -20000 };
                flow.Show();
                ((ComboBox)flow.FindName("GuideTabs")).SelectedIndex = 1;
                flow.UpdateLayout();
                ((TextBox)flow.FindName("GuideNotesText")).Text.Should().Be("第一行运营思路\n第二行保留完整内容");
                ((ItemsControl)flow.FindName("GuideOperatorsHost")).Items.Count.Should().Be(1);
                ((ItemsControl)flow.FindName("GuideEquipmentHost")).Items.Count.Should().Be(1);
                ((TextBlock)flow.FindName("GuideBondsText")).Text.Should().Be("示例盟约");
                ((ComboBox)flow.FindName("GuideTargetPhase")).SelectedItem = LineupPhase.前期;
                ((Button)flow.FindName("ApplyGuideButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                ((TextBlock)flow.FindName("GuideApplyStatus")).Text.Should().Contain("已替换前期草稿");
                vm.FilledCount(LineupPhase.前期).Should().Be(1);
                source.EarlySlots.Should().BeEmpty();
                var saved = vm.ToLineup();
                saved.Guide!.Tabs[1].Operators[0].SkillId.Should().Be("skill-a");
                saved.EarlySlots.Should().OnlyContain(slot => slot.Equipments.Count == 0);
                Save(flow, $"complete-flow-{theme}-{night}");
            }
            finally { flow?.Close(); editor.Close(); }
        }
    }

    private static void VerifyEditableRoundStages(Operator[] operators, CovenantCalculator calculator)
    {
        var intervals = new[] { (1, 4), (5, 7), (8, 11), (12, 13) };
        var source = new Lineup
        {
            Name = "网页原始四回合", SourceUrl = Services.SklandGuideImporterTests.Url,
            Slots = [new() { OperatorName = "乙", IsElite = true }],
            Guide = new LineupGuide
            {
                Title = "成型与四回合分别编辑",
                Tabs = [new() { TabType = 1, OperationIdea = "成型说明", Operators = [new() { OperatorName = "乙", ShopLevel = 3 }] },
                    .. intervals.Select((interval, index) => new LineupGuideTab
                    {
                        TabType = 2, StartRound = interval.Item1, EndRound = interval.Item2,
                        OperationIdea = $"第{interval.Item1}到{interval.Item2}回合原始说明",
                        Operators = [new() { OperatorName = "甲", ShopLevel = 2, SkillId = "skill-" + index }],
                        Equipment = [new() { ItemId = "item-" + index, Name = "本回合推荐" + index }]
                    })]
            },
            RoundStages = [.. intervals.Select((interval, index) => new LineupRoundStage
            {
                StartRound = interval.Item1, EndRound = interval.Item2, SourceTabIndex = index + 1,
                Slots = index % 2 == 0 ? [new() { OperatorName = "甲" }]
                    : [new() { OperatorName = "甲" }, new() { OperatorName = "乙" }]
            })]
        };
        foreach (var theme in Enum.GetValues<GameShopTheme>())
        foreach (var night in new[] { false, true })
        {
            ThemeManager.Apply(theme, night);
            var vm = new LineupEditorViewModel(source, operators, [], calculator);
            var editor = new LineupEditorWindow(vm, [])
            { Width = 760, Height = 680, Opacity = 0, ShowActivated = false,
                ShowInTaskbar = false, Left = -20000, Top = -20000 };
            try
            {
                editor.Show();
                ((Expander)editor.FindName("ManualStagesExpander")).IsExpanded.Should().BeFalse();
                var stageTabs = (Panel)editor.FindName("RoundStageTabs");
                stageTabs.Children.OfType<Button>().Should().HaveCount(5);
                for (var index = 0; index < intervals.Length; index++)
                {
                    // RefreshEditor reconstructs the buttons: always resolve the current rendered tab.
                    stageTabs.Children.OfType<Button>().Single(button => Equals(button.DataContext, index))
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    editor.UpdateLayout();
                    vm.CurrentRoundStageIndex.Should().Be(index);
                    ((TextBlock)editor.FindName("CurrentStageText")).Text.Should().Contain(source.RoundStages[index].DisplayTitle);
                    ((TextBox)editor.FindName("CurrentStageNotesText")).Text.Should().Be(source.Guide.Tabs[index + 1].OperationIdea);
                    ((ItemsControl)editor.FindName("CurrentStageEquipmentHost")).Items.Count.Should().Be(1);
                    vm.FilledSlotCount.Should().Be(source.RoundStages[index].Slots.Count);
                    foreach (Button button in stageTabs.Children)
                    {
                        var bounds = button.TransformToAncestor((Visual)editor.Content).TransformBounds(new Rect(button.RenderSize));
                        bounds.Left.Should().BeGreaterThanOrEqualTo(0);
                        bounds.Right.Should().BeLessThanOrEqualTo(editor.ActualWidth - 1);
                    }
                    if (index is 0 or 3) Save(editor, $"round-stages-{theme}-{night}-{index}");
                }
                stageTabs.Children.OfType<Button>().Single(button => Equals(button.DataContext, 0))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var host = (ItemsControl)editor.FindName("SlotsHost");
                var firstCard = (FrameworkElement)host.Items[0];
                Descendants<Button>(firstCard).Single(button => Equals(button.ToolTip, "清空该位置"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                vm.FilledRoundCount(0).Should().Be(0);
                vm.FilledRoundCount(3).Should().Be(2);
                vm.FilledCount(LineupPhase.后期).Should().Be(1);
                ((Button)editor.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                editor.Result.Should().NotBeNull();
                var saved = editor.Result!;
                saved.RoundStages.Should().HaveCount(4);
                saved.RoundStages[0].Slots.Should().BeEmpty();
                saved.RoundStages[3].Slots.Should().HaveCount(2);
                var reopened = new LineupEditorViewModel(saved, operators, [], calculator);
                reopened.FilledRoundCount(0).Should().Be(0);
                reopened.FilledRoundCount(3).Should().Be(2);
                source.RoundStages[0].Slots.Should().ContainSingle();
            }
            finally { editor.Close(); }
        }
    }

    private static void Complete(Task task)
    {
        var watch = Stopwatch.StartNew();
        while (!task.IsCompleted && watch.Elapsed < TimeSpan.FromSeconds(5))
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () => frame.Continue = false);
            Dispatcher.PushFrame(frame);
        }
        task.IsCompleted.Should().BeTrue("the editor should finish saving or cancelling");
        task.GetAwaiter().GetResult();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static void Save(Window window, string name)
    {
        var content = (FrameworkElement)window.Content;
        content.UpdateLayout();
        var image = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        image.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        var folder = Path.Combine(AppContext.BaseDirectory, "TestResults", "skland-import-previews");
        Directory.CreateDirectory(folder);
        using var stream = File.Create(Path.Combine(folder, name + ".png"));
        encoder.Save(stream);
    }
}
