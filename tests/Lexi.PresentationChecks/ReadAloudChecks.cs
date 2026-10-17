using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lexi;
using Lexi.Core;

internal static partial class Program
{
    private static void CheckReadAloud(MainWindow main)
    {
        int count = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); count++; }
        var model = new LookupModel("codex", "gpt-6.1-sol");
        var sense = new AiSense("noun", "The property of being unchanged by repetition.", "幂等性。", "computing",
            "Retries are safe because the request is idempotent.", "请求是幂等的，所以可以安全重试。", [1]);
        var answers = new[] { new ModelAnswer(model, ModelPhase.Complete, new("idempotency", true, [sense], "", "source_based")) };
        ((FrameworkElement)main.FindName("EntryPanel")).Visibility = Visibility.Visible;
        main.RenderAnswers(new LookupSnapshot(answers, SenseMerging.Group(answers, new("fixture", []))), null);
        Measure((FrameworkElement)main.Content, 718, 618);
        var ai = (Panel)main.FindName("AiPanel");
        TextBox Text(string value) => Contents(ai).OfType<TextBox>().First(t => t.Text == value);
        Grid Block(TextBox text) => (Grid)((Border)text.Parent).Parent;
        Button Speaker(TextBox text) => Block(text).Children.OfType<Button>().Single();

        var definition = Text(sense.Chinese);
        var menu = definition.ContextMenu!;
        Check(menu.Items[0] is MenuItem { Header: "朗读" } && menu.Items.OfType<MenuItem>().Any(i => i.Command == ApplicationCommands.Copy),
            "definitions offer 朗读 above copy in their context menu");
        var speaker = Speaker(definition);
        Check(speaker.Visibility == Visibility.Hidden && AutomationProperties.GetName(speaker) == "朗读", "the read-aloud speaker stays out of sight at rest");
        Block(definition).RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
        Check(speaker.Visibility == Visibility.Visible, "pointing at a definition shows its speaker in the margin");
        Block(definition).RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
        Check(speaker.Visibility == Visibility.Hidden, "the speaker hides again when the pointer leaves");
        Check(Text(sense.English).ContextMenu?.Items[0] is MenuItem { Header: "朗读" }, "English definitions can be read aloud too");
        var example = Text(sense.Example);
        Check(example.ContextMenu?.Items[0] is MenuItem { Header: "朗读" } && Text(sense.ExampleChinese).ContextMenu?.Items[0] is MenuItem { Header: "朗读" },
            "examples and their translations can be read aloud");
        Contents(ai).OfType<System.Windows.Controls.Primitives.ToggleButton>().Single(b => b.Content.ToString() == "例句").IsChecked = true;
        CheckReadAloudGeometry(ai, new[] { definition, Text(sense.English), example, Text(sense.ExampleChinese) }, Check);
        Console.WriteLine($"{count} read-aloud presentation checks passed without displaying windows");
    }
    private static void CheckReadAloudGeometry(Panel answers, TextBox[] text, Action<bool, string> check)
    {
        var card = answers.Children.OfType<Border>().Single();
        answers.Children.Remove(card);
        var root = new Border { Child = card, Background = Theme.Brush("Canvas"), Padding = new(20),
            Language = XmlLanguage.GetLanguage("zh-CN"), UseLayoutRounding = true };
        TextOptions.SetTextFormattingMode(root, TextFormattingMode.Display);
        Grid Row(TextBox item) => (Grid)((Border)item.Parent).Parent;
        Button Button(TextBox item) => Row(item).Children.OfType<Button>().Single();
        bool Within(DependencyObject? hit, DependencyObject owner)
        {
            for (var at = hit; at != null; at = VisualTreeHelper.GetParent(at)) if (at == owner) return true;
            return false;
        }
        DependencyObject? Hit(Point point)
        {
            DependencyObject? hit = null;
            VisualTreeHelper.HitTest(root,
                item => item is UIElement element && (!element.IsHitTestVisible || element.Visibility != Visibility.Visible)
                    ? HitTestFilterBehavior.ContinueSkipSelfAndChildren : HitTestFilterBehavior.Continue,
                result => { hit = result.VisualHit; return HitTestResultBehavior.Stop; }, new PointHitTestParameters(point));
            return hit;
        }
        try
        {
            foreach (var width in new[] { 380d, 600d, 880d })
            {
                root.Measure(new Size(width, double.PositiveInfinity));
                root.Arrange(new Rect(0, 0, width, root.DesiredSize.Height)); root.UpdateLayout();
                foreach (var item in text) Row(item).RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0) { RoutedEvent = Mouse.MouseEnterEvent });
                root.UpdateLayout();
                var speakerColumn = Button(text[0]).TranslatePoint(new Point(),root).X;
                check(text.All(item => Math.Abs(Button(item).TranslatePoint(new Point(),root).X-speakerColumn) <= 0.1),
                    $"speakers share an actual rendered column across definitions and indented examples at {width} DIP");
                foreach (var item in text)
                {
                    var button = Button(item);
                    var center = button.TranslatePoint(new Point(button.ActualWidth/2, button.ActualHeight/2),root);
                    var start = item.TranslatePoint(new Point(item.GetRectFromCharacterIndex(0).X+4,0),root).X;
                    bool continuous = true;
                    for (double x = center.X; x <= start; x += 0.5) continuous &= Within(Hit(new(x,center.Y)),Row(item));
                    check(continuous && Within(Hit(center),button),
                        $"the real hit-test path from text into its speaker has no hover gap at {width} DIP");
                }
                foreach (var scale in new[] { 1d, 1.5d })
                {
                    var image = new RenderTargetBitmap((int)Math.Ceiling(width*scale), (int)Math.Ceiling(root.ActualHeight*scale), 96*scale,96*scale,PixelFormats.Pbgra32);
                    image.Render(root);
                    var pixels = new byte[image.PixelWidth*image.PixelHeight*4]; image.CopyPixels(pixels,image.PixelWidth*4,0);
                    double InkCenter(Rect area, bool glyph)
                    {
                        int top = image.PixelHeight, bottom = -1;
                        for (int y=Math.Max(0,(int)Math.Floor(area.Top*scale));y<Math.Min(image.PixelHeight,(int)Math.Ceiling(area.Bottom*scale));y++)
                        for (int x=Math.Max(0,(int)Math.Floor(area.Left*scale));x<Math.Min(image.PixelWidth,(int)Math.Ceiling(area.Right*scale));x++)
                        {
                            int at=(y*image.PixelWidth+x)*4;
                            int low=Math.Min(pixels[at],Math.Min(pixels[at+1],pixels[at+2]));
                            int high=Math.Max(pixels[at],Math.Max(pixels[at+1],pixels[at+2]));
                            if (glyph ? low>210 && high-low<25 : low>=120 && high<210 && high-low<30)
                            { top=Math.Min(top,y); bottom=Math.Max(bottom,y); }
                        }
                        return bottom<0 ? double.NaN : (top+bottom+1)/(2*scale);
                    }
                    foreach (var item in text)
                    {
                        var first=item.GetRectFromCharacterIndex(0);
                        var position=item.TranslatePoint(new Point(first.X,first.Y),root);
                        var ink=InkCenter(new(position,new Size(item.ActualWidth-first.X,first.Height)),true);
                        var button=Button(item);
                        var speaker=InkCenter(new(button.TranslatePoint(new Point(),root),button.RenderSize),false);
                        check(double.IsFinite(ink) && double.IsFinite(speaker) && Math.Abs(ink-speaker)<=1,
                            $"speaker pixels align with first-line text pixels at {width} DIP / {scale*100}% including serif italics and Chinese");
                    }
                }
            }
        }
        finally { root.Child=null; answers.Children.Add(card); }
    }

}
