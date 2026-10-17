using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Lexi;

// Definitions and examples can be read aloud, the same way as on macOS. A speaker appears in the
// card's left margin while the pointer is over a block and stays, as a stop button, while it plays;
// the block is tinted gold while it plays. Its context menu starts with 朗读 (朗读所选文字 for a
// selection, 停止朗读 while it plays).
public partial class MainWindow
{
    internal const string HeadingSpeech = "heading";
    // From a block's text back to the speaker column, at the top level of the answer card.
    internal const double ReadAloudGutter = 28;
    private static readonly Brush ReadingTint = Freeze(new SolidColorBrush(Color.FromArgb(0x29, 0xDD, 0xBD, 0x82)));
    private readonly List<ReadAloudBlock> readAloudBlocks = [];
    private string? speakingKey;

    private static Brush Freeze(Brush brush) { brush.Freeze(); return brush; }

    private sealed class ReadAloudBlock(string key, Border surface, Button speaker)
    {
        public string Key { get; } = key;
        public bool Hovering { get; set; }
        public void Show(bool playing)
        {
            surface.Background = playing ? ReadingTint : Brushes.Transparent;
            var icon = Theme.Icon(playing ? "StopIcon" : "SpeakerIcon"); icon.Width = 14; icon.Height = 14;
            if (playing) icon.Stroke = Theme.Brush("Warning");
            speaker.Content = icon;
            speaker.Visibility = Hovering || playing ? Visibility.Visible : Visibility.Hidden;
            speaker.ToolTip = playing ? "停止朗读" : "朗读";
            AutomationProperties.SetName(speaker, playing ? "停止朗读" : "朗读");
        }
    }

    // indent: how far a disclosure body indents the block, so every speaker in the card shares one column.
    private FrameworkElement Speakable(TextBox text, string key, double indent = 0)
    {
        var margin = text.Margin; text.Margin = new(0);
        // The tint reaches a little past the text without moving it.
        var surface = new Border { Child = text, CornerRadius = new(6), Margin = new(-6,-2,-6,-2), Padding = new(6,2,6,2) };
        var speakerOffset = new TranslateTransform();
        var speaker = new Button { Style = (Style)FindResource("IconButton"), Width = 22, Height = 22,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            RenderTransform = speakerOffset };
        var block = new ReadAloudBlock(key, surface, speaker);
        var gutter = ReadAloudGutter + indent;
        // The whole path from the text to its button belongs to this row, including
        // disclosure indentation. A child outside the old row left a hover dead zone.
        var container = new Grid { Background = Brushes.Transparent,
            Margin = new(margin.Left - gutter, margin.Top, margin.Right, margin.Bottom), Tag = key };
        container.ColumnDefinitions.Add(new() { Width = new(gutter) });
        container.ColumnDefinitions.Add(new());
        Grid.SetColumn(surface, 1);
        container.Children.Add(surface); container.Children.Add(speaker);
        AlignReadAloud(text, container, speakerOffset, speaker.Height);
        container.MouseEnter += (_, _) => { block.Hovering = true; block.Show(speakingKey == key); };
        container.MouseLeave += (_, _) => { block.Hovering = false; block.Show(speakingKey == key); };
        speaker.Click += async (_, _) => await ReadAloudAsync(key, text.Text, "");
        text.ContextMenu = ReadAloudMenu(text, key);
        readAloudBlocks.Add(block);
        block.Show(speakingKey == key);
        return container;
    }

    private static void AlignReadAloud(TextBox text, Grid row, TranslateTransform offset, double buttonHeight)
    {
        var face = new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch);
        string? previousLine = null;
        var previousCaret = Rect.Empty;
        double previousScale = 0;
        text.LayoutUpdated += (_, _) =>
        {
            if (!text.IsArrangeValid || text.LineCount < 1) return;
            var caret = text.GetRectFromCharacterIndex(0);
            if (caret.IsEmpty) return;
            var firstLine = text.GetLineText(0).TrimEnd('\r', '\n');
            var scale = VisualTreeHelper.GetDpi(text).PixelsPerDip;
            if (firstLine == previousLine && caret == previousCaret && scale == previousScale) return;
            previousLine = firstLine; previousCaret = caret; previousScale = scale;
            // Use the rendered first line's ink, rather than half the paragraph's
            // line spacing. This also accounts for serif italics and CJK fallback.
            var formatted = new FormattedText(firstLine, text.Language.GetSpecificCulture(), text.FlowDirection,
                face, text.FontSize, Brushes.Black, scale);
            var ink = formatted.BuildGeometry(new Point()).Bounds;
            var center = caret.Top + (ink.IsEmpty ? caret.Height / 2 : ink.Top + ink.Height / 2);
            offset.Y = Math.Max(0, text.TranslatePoint(new Point(0, center), row).Y - buttonHeight / 2);
        };
    }

    private ContextMenu ReadAloudMenu(TextBox text, string key)
    {
        var read = new MenuItem { Header = "朗读" };
        var menu = new ContextMenu();
        menu.Items.Add(read);
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "复制", Command = ApplicationCommands.Copy, CommandTarget = text });
        menu.Items.Add(new MenuItem { Header = "全选", Command = ApplicationCommands.SelectAll, CommandTarget = text });
        var selection = "";
        menu.Opened += (_, _) =>
        {
            selection = text.SelectedText;
            read.Header = selection.Length > 0 ? "朗读所选文字" : speakingKey == key ? "停止朗读" : "朗读";
        };
        read.Click += async (_, _) => await ReadAloudAsync(key, text.Text, selection);
        return menu;
    }

    // An empty selection reads the whole block, or stops it while it plays.
    private async Task ReadAloudAsync(string key, string text, string selection)
    {
        if (selection.Length == 0 && speakingKey == key) { StopSpeech(); LookupStatus.Text = "发音已停止"; return; }
        await SpeakAsync(selection.Length > 0 ? selection : text, key);
    }

    private void ShowReadAloud()
    {
        foreach (var block in readAloudBlocks) block.Show(speakingKey == block.Key);
    }
}
