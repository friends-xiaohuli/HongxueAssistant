using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HongxueAssistant.Core.Models;

namespace HongxueAssistant.App.Windows;

/// <summary>Displays the preserved source; applying a snapshot changes the editor draft only.</summary>
public partial class LineupGuideWindow : Window
{
    private readonly Func<int, LineupPhase, string> _apply;

    public LineupGuideWindow(LineupGuide guide, Func<int, LineupPhase, string> apply)
    {
        _apply = apply;
        InitializeComponent();
        var snapshot = guide.Clone();
        GuideTitleText.Text = snapshot.Title;
        GuideTargetPhase.ItemsSource = Enum.GetValues<LineupPhase>();
        GuideTargetPhase.SelectedItem = LineupPhase.前期;
        GuideTabs.ItemsSource = snapshot.Tabs;
        GuideTabs.SelectedIndex = snapshot.Tabs.Count > 0 ? 0 : -1;
    }

    private void OnGuideTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GuideTabs.SelectedItem is not LineupGuideTab tab) return;
        GuideMetaText.Text = $"{tab.Operators.Count} 名推荐干员 · {tab.Equipment.Count} 件推荐装备 · 原文快照"
            + (tab.HasInvalidEntries ? " · 部分条目无效，请核对来源数据" : "");
        GuideNotesText.Text = tab.OperationIdea.Length > 0 ? tab.OperationIdea : "此页未填写运营思路。";
        GuideOperatorsHost.ItemsSource = tab.Operators;
        GuideEquipmentHost.ItemsSource = tab.Equipment;
        GuideBondsText.Text = tab.ActiveBonds.Count > 0
            ? string.Join("、", tab.ActiveBonds.Select(bond => bond.Name)) : "此页未指定目标盟约。";
        ApplyGuideButton.IsEnabled = tab.Operators.Count > 0 && !tab.HasInvalidEntries;
    }

    private void OnApplyGuide(object sender, RoutedEventArgs e)
    {
        if (GuideTargetPhase.SelectedItem is not LineupPhase phase || GuideTabs.SelectedIndex < 0) return;
        var error = _apply(GuideTabs.SelectedIndex, phase);
        GuideApplyStatus.Text = error.Length > 0 ? error : $"已替换{phase}草稿，请回到编辑器核对并保存；推荐装备保持独立。";
        GuideApplyStatus.SetResourceReference(TextBlock.ForegroundProperty, error.Length > 0 ? "DangerBrush" : "AccentBrush");
    }

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
