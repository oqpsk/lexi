using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Input;
using Lexi.Core;

namespace Lexi;

public partial class MainWindow
{
    internal void ResetLexicalDetails() { ResetLexicalPopup(); session.ResetLexical(); }
    private void ResetLexicalPopup() { EtymologyPopup.IsOpen = false; EtymologyButton.IsChecked = false; }
    internal void ApplySourceEtymology(EtymologyExcerpt[] excerpts) => session.ApplySourceEtymology(excerpts);
    internal void BeginLexicalOverview(string query, LocalHit[] local) => session.BeginOverview(query, local);

    private void RenderEtymology(EtymologyView[] items)
    {
        var offset = EtymologyScroll.VerticalOffset;
        EtymologyContent.Children.Clear();
        EtymologyButton.Visibility = items.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (EtymologyButton.Visibility == Visibility.Collapsed) EtymologyPopup.IsOpen = false;
        foreach (var item in items)
        {
            if (item.Generated)
            {
                EtymologyContent.Children.Add(CompactText(item.Text, 17));
                EtymologyContent.Children.Add(CompactHint(item.Caption));
                continue;
            }
            EtymologyContent.Children.Add(CompactHint(item.Caption));
            EtymologyContent.Children.Add(CompactText(item.Text, 17));
            var link = new Button { Content = item.LinkLabel + " ↗", FontSize = 14, Foreground = muted, HorizontalAlignment = HorizontalAlignment.Left };
            link.Click += (_, _) => OpenSource(item.SourceUrl); EtymologyContent.Children.Add(link);
        }
        EtymologyScroll.ScrollToVerticalOffset(offset);
    }
    private void Etymology_Opened(object sender, EventArgs e)
    {
        EtymologySurface.Width = Math.Clamp(ActualWidth - 64, 280, 460);
        EtymologyScroll.MaxHeight = Math.Clamp(ActualHeight - 150, 140, 420);
        CloseEtymology.Focus();
    }
    private void Etymology_Close(object sender, RoutedEventArgs e) => DismissEtymology();
    internal void DismissEtymology()
    { EtymologyPopup.IsOpen = false; EtymologyButton.IsChecked = false; EtymologyButton.Focus(); }
    private void Etymology_KeyDown(object sender, KeyEventArgs e)
    { if (e.Key == Key.Escape) { DismissEtymology(); e.Handled = true; } }

    private void RenderQuickRead(QuickReadView view)
    {
        QuickReadCard.Visibility = view.Visible ? Visibility.Visible : Visibility.Collapsed;
        QuickReadText.Text = view.Text;
        QuickReadText.ToolTip = view.Tooltip.Length > 0 ? view.Tooltip : null;
        if (view.Caption.Length == 0) return;
        QuickReadState.Text = view.State;
        QuickReadState.Visibility = view.ShowState ? Visibility.Visible : Visibility.Collapsed;
        QuickReadGuide.ToolTip = view.Caption + "\n" + view.Explanation;
        QuickReadState.ToolTip = QuickReadGuide.ToolTip;
        AutomationProperties.SetName(QuickReadGuide, view.Caption);
        AutomationProperties.SetName(QuickReadText, view.Caption);
    }
    private Border ReferenceSenseRow(ReferenceView brief, int index)
    {
        var content = new StackPanel();
        content.Children.Add(CompactHint(brief.Caption));
        if (brief.Expansion.Length > 0) { var expansion = CompactText(brief.Expansion, 17); expansion.Foreground = accentBrush; content.Children.Add(expansion); }
        content.Children.Add(Speakable(CompactText(brief.Chinese, 20), brief.Key + ":chinese"));
        content.Children.Add(Speakable(CompactText(brief.English, 18), brief.Key + ":english"));
        var controls = new WrapPanel(); var details = new StackPanel(); var source = new StackPanel();
        source.Children.Add(CompactHint(brief.SourceNote));
        var link = new Button { Content = brief.SourceName + " ↗", FontSize = 14, Foreground = muted, HorizontalAlignment = HorizontalAlignment.Left };
        link.Click += (_, _) => OpenSource(brief.SourceUrl); source.Children.Add(link);
        content.Children.Add(controls); content.Children.Add(details);
        AddDisclosure(controls, details, brief.Key, "参考资料", source);
        return new Border { Child = content, BorderBrush = Theme.Brush("Stroke"), BorderThickness = new(0,index == 0 ? 0 : 1,0,0), Padding = new(0,index == 0 ? 0 : 10,0,8) };
    }
}
