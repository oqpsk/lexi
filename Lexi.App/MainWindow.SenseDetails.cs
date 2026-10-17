using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using Lexi.Core;

namespace Lexi;

public partial class MainWindow
{
    // Disclosure state belongs to this window and this lookup, never to the user's disk cache.
    private readonly Dictionary<string, bool> senseDetails = new(StringComparer.Ordinal);
    internal void ResetSenseDetails() => senseDetails.Clear();

    private TextBlock CompactHint(string text, bool warning = false) => new()
    { Text = text, Foreground = warning ? Theme.Brush("Warning") : muted, FontSize = 14, Margin = new(0,2,0,6), TextWrapping = TextWrapping.Wrap };

    private TextBox CompactText(string text, double size)
    {
        var block = Selectable(text, size);
        block.Margin = new(0,2,0,3);
        return block;
    }

    private TextBox ExampleText(string text, bool chinese = false)
    {
        var block = CompactText(text, 18);
        block.FontFamily = (FontFamily)FindResource(chinese ? "ChineseExampleFont" : "LexicalFont");
        block.FontStyle = chinese ? FontStyles.Normal : FontStyles.Italic;
        return block;
    }

    private TextBlock SenseHeading(SenseHeadingView heading)
    {
        var label = CompactHint(""); label.Margin = new(0,0,0,3);
        label.ToolTip = heading.Tooltip;
        label.Inlines.Add(new Run(heading.Prefix + "  ") { Foreground = accentBrush });
        label.Inlines.Add(new Run(heading.PartOfSpeech)
            { Foreground = Theme.Brush("Warning"), FontFamily = (FontFamily)FindResource("LexicalFont"), FontStyle = FontStyles.Italic, FontSize = 18 });
        if (heading.Form.Length > 0) label.Inlines.Add(new Run("  " + heading.Form) { FontFamily = (FontFamily)FindResource("LexicalFont"), FontStyle = FontStyles.Italic, FontSize = 16 });
        if (heading.Usage.Length > 0) label.Inlines.Add(new Run("  " + heading.Usage));
        if (heading.Abbreviation) label.Inlines.Add(new Run("  缩写"));
        if (heading.Provisional) label.Inlines.Add(new Run(" · 生成中"));
        return label;
    }

    private Border SenseRow(SenseView sense, int index)
    {
        var content = new StackPanel();
        content.Children.Add(SenseHeading(sense.Heading));
        RenderSenseContent(content, sense);
        return new Border { Child = content, BorderBrush = Theme.Brush("Stroke"), BorderThickness = new(0,index == 0 ? 0 : 1,0,0), Padding = new(0,index == 0 ? 0 : 10,0,8) };
    }

    // indent: how far disclosure bodies indent this sense, for the read-aloud speaker column.
    private void RenderSenseContent(Panel parent, SenseView sense, double indent = 0)
    {
        if (sense.Expansion.Length > 0)
        {
            var fullForm = CompactText(sense.Expansion, 17);
            fullForm.FontFamily = (FontFamily)FindResource("LexicalFont");
            fullForm.Foreground = accentBrush; fullForm.ToolTip = "英文全称";
            parent.Children.Add(fullForm);
        }
        parent.Children.Add(Speakable(CompactText(sense.Chinese, 20), sense.Key + ":chinese", indent));
        parent.Children.Add(Speakable(CompactText(sense.English, 18), sense.Key + ":english", indent));
        var controls = new WrapPanel { Margin = new(0,2,0,0) }; var details = new StackPanel();
        parent.Children.Add(controls); parent.Children.Add(details);
        if (!string.IsNullOrWhiteSpace(sense.Example) || !string.IsNullOrWhiteSpace(sense.ExampleChinese))
        {
            var examples = new StackPanel();
            if (!string.IsNullOrWhiteSpace(sense.Example)) examples.Children.Add(Speakable(ExampleText(sense.Example), sense.Key + ":example", indent + DisclosureIndent));
            if (!string.IsNullOrWhiteSpace(sense.ExampleChinese)) examples.Children.Add(Speakable(ExampleText(sense.ExampleChinese, true), sense.Key + ":exampleChinese", indent + DisclosureIndent));
            AddDisclosure(controls, details, sense.Key + ":examples", "例句", examples);
        }

        var sources = new StackPanel();
        sources.Children.Add(CompactHint(sense.Provenance));
        sources.Children.Add(CompactHint(sense.OriginalLabels));
        var links = new WrapPanel(); sources.Children.Add(links);
        foreach (var citation in sense.Citations)
        {
            var link = new Button { Content = citation.Label + " ↗", FontSize = 14, Foreground = muted, Padding = new(0,3,12,3), ToolTip = citation.Definition };
            link.Click += (_, _) => OpenSource(citation.Url);
            links.Children.Add(link);
        }
        foreach (var brief in sense.Briefs)
        {
            sources.Children.Add(CompactHint(brief.Caption));
            sources.Children.Add(CompactText(brief.Chinese, 16));
            sources.Children.Add(CompactText(brief.English, 16));
        }
        if (sense.SourceNote.Length > 0) sources.Children.Add(CompactHint(sense.SourceNote));
        if (sense.AlternativesNote.Length > 0) sources.Children.Add(CompactHint(sense.AlternativesNote));
        var keepReferenceOpen = sense.ReferenceKeys.Any(key => senseDetails.GetValueOrDefault(key));
        AddDisclosure(controls, details, sense.Key + ":sources", sense.SourceLabel, sources, keepReferenceOpen);

        if (sense.Alternatives.Length == 0) return;
        var originals = new StackPanel();
        foreach (var variant in sense.Alternatives)
        {
            var section = new StackPanel { Margin = new(0,2,0,6) };
            section.Children.Add(SenseHeading(variant.Heading));
            RenderSenseContent(section, variant, indent + DisclosureIndent);
            originals.Children.Add(section);
        }
        // If the user was reading the faster model before merging, keep that
        // expanded detail accessible when it moves under the primary wording.
        var retainReading = sense.Alternatives.Any(v => senseDetails.Any(s => s.Value && s.Key.StartsWith(v.Key + ":", StringComparison.Ordinal)));
        AddDisclosure(controls, details, sense.Key + ":alternatives", sense.AlternativesLabel, originals, retainReading);
    }

    // A disclosure body's left rule plus its padding.
    private const double DisclosureIndent = 12;
    private void AddDisclosure(Panel controls, Panel details, string key, string label, FrameworkElement content, bool initiallyOpen = false)
    {
        var open = senseDetails.GetValueOrDefault(key, initiallyOpen);
        var body = new Border { Child = content, BorderBrush = Theme.Brush("Stroke"), BorderThickness = new(2,0,0,0),
            Padding = new(10,4,0,2), Margin = new(0,5,0,3), Visibility = open ? Visibility.Visible : Visibility.Collapsed };
        var toggle = new ToggleButton { Content = label, Tag = key, IsChecked = open, Style = (Style)FindResource("DisclosureButton"), ToolTip = "展开或收起" + label };
        AutomationProperties.SetName(toggle, label);
        toggle.Checked += (_, _) => { senseDetails[key] = true; body.Visibility = Visibility.Visible; };
        toggle.Unchecked += (_, _) => { senseDetails[key] = false; body.Visibility = Visibility.Collapsed; };
        controls.Children.Add(toggle); details.Children.Add(body);
    }
}
