using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HongxueAssistant.App;
using HongxueAssistant.App.Controls;
using HongxueAssistant.App.Views;
using HongxueAssistant.App.Windows;
using HongxueAssistant.Core.Data;
using HongxueAssistant.Core.Models;
using HongxueAssistant.Core.Services;
using HongxueAssistant.Core.ViewModels;
using FluentAssertions;

namespace HongxueAssistant.Tests.Views;

internal static class LineupRecommendationViewTests
{
    public static void Verify()
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        Operator[] operators = [.. new[] { ("炎", "甲乙丙癸"), ("谢拉格", "丁戊己"), ("乌萨斯", "庚辛壬"), ("维多利亚", "子丑寅") }
            .SelectMany(pair => pair.Item2.Select(name => new Operator { Name = name.ToString(), Tier = 1, CoreCovenants = [pair.Item1] }))];
        CovenantInfo[] covenants = [.. new[] { "炎", "谢拉格", "乌萨斯", "维多利亚" }.Select(n => new CovenantInfo { Name = n, BondType = "SEASON", ActivateCount = 3 })];
        var calculator = new CovenantCalculator(operators, covenants, new Dictionary<string, string>());
        using var operatorData = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "Regulation", "operators.json")));
        var manyBans = operatorData.RootElement.GetProperty("operators").EnumerateArray().Take(40)
            .Select(o => o.GetProperty("name").GetString()!).ToArray();
        foreach (var theme in Enum.GetValues<GameShopTheme>())
        foreach (var night in new[] { false, true })
        {
            var folder = Path.Combine(Path.GetTempPath(), "recommendation-ui-" + Guid.NewGuid().ToString("N"));
            var lineups = new LineupViewModel(new LineupStore(folder, calculator.EquipmentRules), calculator);
            Lineup Make(string id, string name, string people, string covenant) => new() { Id = id, Name = name,
                Slots = [.. people.Select(n => new LineupSlot { OperatorName = n.ToString() })],
                TargetCovenants = [new() { Name = covenant }] };
            var affected = Make("affected", "受影响阵容", "甲乙丙", "炎");
            affected.Slots[0].CandidateEquipments = [new() { Name = "候选盾", IsPriority = true }];
            var safe = Make("safe", "安全阵容", "丁戊己", "谢拉格");
            lineups.LoadLineups([safe, affected, Make("broken", "断档阵容", "庚辛壬", "乌萨斯")]);
            var operatorsVm = new OperatorBrowseViewModel();
            operatorsVm.LoadOperators(operators.ToList());
            var tracking = new TrackingViewModel { TrackMultipleLineups = true };
            var bans = new BanViewModel();
            var panel = new SidePanelWindow(operatorsVm, new EquipmentBrowseViewModel(), tracking, new GameStateViewModel(), bans,
                new EnemyViewModel(), lineups, new LineupShareService(calculator.EquipmentRules, Path.Combine(folder, "share")),
                operators, [], [], new SettingsManager(folder), theme, new SidePanelPositionSettings { FreeMovement = true })
            { Opacity = 0, ShowActivated = false, ShowInTaskbar = false, Left = -20000, Top = -20000, Height = 1000 };
            panel.Show();
            ThemeManager.Apply(theme, night);
            panel.PlannerVm.DebounceMilliseconds = 0;
            var tabs = (TabControl)panel.FindName("MainTabs");
            var segment = (ListBox)panel.FindName("LineupsSegment");
            var view = (LineupListView)panel.FindName("LineupView");
            tabs.SelectedItem = panel.FindName("LineupsTab");
            tracking.TrackLineup(safe);
            bans.SetBans(["乙"]);
            bans.SetCovenantBans(["乌萨斯"]);
            lineups.SelectedAvailabilityFilter = LineupAvailabilityFilter.Pending;
            Pump();
            ((FrameworkElement)view.FindName("RecommendSection")).Visibility.Should().Be(Visibility.Visible);
            var recommendations = ((ItemsControl)view.FindName("RecommendItems")).Items.Cast<LineupCardItem>().ToList();
            recommendations.Select(r => r.Recommendation!.Kind).Should().Equal(RecommendationKind.NeedsRepair);
            foreach (var many in new[] { false, true })
            {
                bans.SetBans(many ? manyBans : ["乙"]);
                bans.SetCovenantBans(many ? covenants.Select(c => c.Name) : ["乌萨斯"]);
                foreach (var width in new[] { 280, 360, 400 })
                {
                    panel.Width = width;
                    Pump();
                    var output = Path.Combine(AppContext.BaseDirectory, "TestResults", "lineup-recommendation");
                    Directory.CreateDirectory(output);
                    var content = (FrameworkElement)panel.Content;
                    var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(content);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var file = File.Create(Path.Combine(output, $"{theme}-{night}-{width}-{(many ? "many" : "single")}.png"));
                    encoder.Save(file);
                    var strip = (FrameworkElement)view.FindName("BannedStrip");
                    var avatars = (ItemsControl)view.FindName("BannedAvatars");
                    var rendered = Descendants<Avatar>(avatars).ToList();
                    rendered.Should().HaveCount(lineups.RunBannedNames.Count);
                    foreach (var avatar in rendered)
                    {
                        var bounds = avatar.TransformToAncestor(strip).TransformBounds(new Rect(0, 0, avatar.ActualWidth, avatar.ActualHeight));
                        bounds.Left.Should().BeGreaterThanOrEqualTo(0);
                        bounds.Right.Should().BeLessThanOrEqualTo(strip.ActualWidth + 0.5, "banned portraits must fit inside the summary instead of clipping at its right edge");
                    }
                    if (many)
                    {
                        rendered[1].TranslatePoint(new Point(), avatars).Y.Should().BeApproximately(rendered[0].TranslatePoint(new Point(), avatars).Y, 0.5,
                            "a narrow sidebar still has room for multiple portraits per row");
                        strip.ActualHeight.Should().BeLessThan(360, "forty banned operators should wrap across the available width");
                    }
                }
            }
            bans.ClearBans();
            Pump();
            ((FrameworkElement)view.FindName("RecommendSection")).Visibility.Should().Be(Visibility.Collapsed);
            bans.SetBans(["乙"]);
            bans.SetCovenantBans(["乌萨斯"]);
            Pump();
            lineups.SelectedAvailabilityFilter = LineupAvailabilityFilter.All;
            var search = (TextBox)view.FindName("SearchBox");
            search.Text = "安全";
            Pump();
            lineups.FilteredLineups.Should().Equal(safe);
            search.Clear();
            lineups.ToggleFilterCovenant("炎");
            Pump();
            lineups.FilteredLineups.Should().Equal(affected);
            var cards = (ItemsControl)view.FindName("LineupItems");
            Descendants<Button>(cards).Single(b => Equals(b.Content, "修补")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump();
            segment.SelectedIndex.Should().Be(1);
            panel.PlannerVm.IsRepairing.Should().BeTrue();
            panel.PlannerVm.RepairSource.Should().BeSameAs(affected);
            panel.PlannerVm.LockedOperators.Select(o => o.Name).Should().Equal("甲", "丙");
            var pending = panel.PlannerVm.PendingSearch!;
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!pending.IsCompleted && DateTime.UtcNow < deadline) Pump();
            pending.IsCompleted.Should().BeTrue();
            pending.GetAwaiter().GetResult();
            panel.PlannerVm.HasError.Should().BeFalse();
            segment.SelectedIndex = 0;
            Pump();
            lineups.FilteredLineups.Should().Equal(affected);
            affected.Slots[0].CandidateEquipments.Single().Name.Should().Be("候选盾");
            bans.ClearBans();
            operatorsVm.SetAvailableCovenants(["炎", "谢拉格", "乌萨斯", "维多利亚"]);
            panel.ShowLineupsSegment(planner: false);
            Pump();
            lineups.HasRunInfo.Should().BeTrue("recorded information with zero bans is still a known run");
            lineups.SelectedAvailabilityFilter.Should().Be(LineupAvailabilityFilter.Available);
            lineups.VisibleLineups.Should().HaveCount(3);
            operatorsVm.ClearCovenantHighlights();
            Pump();
            lineups.HasRunInfo.Should().BeFalse();
            lineups.SelectedAvailabilityFilter.Should().Be(LineupAvailabilityFilter.All);
            panel.Close();
        }
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T value) yield return value;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
