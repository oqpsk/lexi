using System.Windows;
using System.Windows.Controls;
using Lexi.Core;

namespace Lexi;

public partial class MainWindow
{
    // Applies a model snapshot through the shared presenter; the published view redraws the card.
    internal void RenderAnswers(LookupSnapshot snapshot, OnlineEntry? evidence, bool finalized = true) =>
        session.ApplyAnswers(snapshot, evidence, finalized);

    private void RenderAnswerCard(AnswersView? answers)
    {
        AiPanel.Children.Clear(); readAloudBlocks.Clear();
        if (answers is null or { Empty: true }) return;
        var card = Card(AiPanel, "", "", true);
        // A wider left margin holds the read-aloud speakers.
        if (card.Parent is Border frame) frame.Padding = new(16 + ReadAloudGutter - 12, frame.Padding.Top, frame.Padding.Right, frame.Padding.Bottom);
        var index = 0;
        foreach (var reference in answers.References) card.Children.Add(ReferenceSenseRow(reference, index++));
        foreach (var sense in answers.Senses) card.Children.Add(SenseRow(sense, index++));
        foreach (var notice in answers.Notices) card.Children.Add(CompactHint(notice.Text, notice.Warning));
        if (answers.Notes.Length > 0)
        {
            var controls = new WrapPanel(); var details = new StackPanel(); var content = new StackPanel();
            foreach (var note in answers.Notes) content.Children.Add(CompactHint(note));
            card.Children.Add(controls); card.Children.Add(details);
            AddDisclosure(controls, details, "lookup:notes", "补充说明", content);
        }
        if (answers.MergeWarning.Length > 0) card.Children.Add(CompactHint(answers.MergeWarning, true));
        if (answers.Originals == OriginalsMode.Collapse)
        {
            if (OnlinePanel.Children.OfType<Expander>().FirstOrDefault() is { Tag: null } onlineOriginal)
            { onlineOriginal.IsExpanded = false; onlineOriginal.Tag = "initially-collapsed"; }
            // Preserve every dictionary's original text, with a compact default view.
            // Do this only once per lookup so streaming does not undo user expansion.
            var count = LocalPanel.Children.OfType<Border>().Count();
            if (count > 0)
            {
                var originals = new StackPanel();
                var children = LocalPanel.Children.Cast<UIElement>().ToArray();
                LocalPanel.Children.Clear();
                foreach (var child in children) originals.Children.Add(child);
                LocalPanel.Children.Add(new Expander { Header = $"离线词典原文 · {count} 部", Content = originals, Margin = new(0,4,0,4) });
            }
        }
        else if (answers.Originals == OriginalsMode.Expand)
        {
            foreach (var original in LocalPanel.Children.OfType<Expander>().Concat(OnlinePanel.Children.OfType<Expander>())) original.IsExpanded = true;
        }
    }
}
