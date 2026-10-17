using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using Lexi;
using Lexi.Core;

internal static partial class Program
{
    private static void CheckLexicalDetails(MainWindow main)
    {
        void Check(bool ok, string label) { if (!ok) throw new Exception("FAIL: " + label); Console.WriteLine("PASS: " + label); }
        main.ResetSenseDetails(); main.ResetLexicalDetails(); main.ResetWordForms();
        var model = new LookupModel("codex", "fixture");
        var sense = new AiSense("noun", "An isolated execution environment.", "可信执行环境。", "information security", "Code runs in a TEE.", "代码在可信执行环境中运行。", [], "abbreviation", "Trusted Execution Environment");
        var entry = new AiEntry("TEE", true, [sense], "", "unverified", "/tiː iː iː/", [], "由英文全称的首字母组成。");
        var answers = new[] { new ModelAnswer(model,ModelPhase.Complete,entry) };
        var snapshot = new LookupSnapshot(answers,SenseMerging.Group(answers));
        main.RenderAnswers(snapshot,null);
        var ai = (Panel)main.FindName("AiPanel"); var etymology = (Panel)main.FindName("EtymologyContent");
        var etymologyButton = (ToggleButton)main.FindName("EtymologyButton");
        var popup = (Popup)main.FindName("EtymologyPopup");
        Check(Contents(ai,true).OfType<TextBox>().Any(t=>t.Text==sense.Expansion) && Contents(ai,true).OfType<TextBlock>().SelectMany(t=>t.Inlines.OfType<Run>()).Any(r=>r.Text.Contains("缩写")),
            "abbreviation identity and full English expansion are visible beside their own sense");
        var examples = Contents(ai,true).OfType<ToggleButton>().Single(b=>b.Content.ToString()=="例句");
        Measure((FrameworkElement)main.Content,718,618);
        var surface = (Border)examples.Template.FindName("Head",examples);
        Check(surface.Background is SolidColorBrush background && background.Color.A>0 && surface.BorderBrush is SolidColorBrush stroke && stroke.Color.A>0 && examples.MinHeight>=30,
            "collapsed detail controls have visible surfaces, outlines and generous hit targets");
        examples.IsChecked=true;
        Check(Contents(ai,true).OfType<TextBox>().Single(t=>t.Text==sense.Example).FontStyle == FontStyles.Italic &&
            Contents(ai,true).OfType<TextBox>().Single(t=>t.Text==sense.ExampleChinese).FontStyle == FontStyles.Normal,
            "expanded examples use italic English and upright Chinese serif text");
        Check(etymologyButton.Visibility == Visibility.Visible && !popup.IsOpen && !popup.StaysOpen && popup.PlacementTarget == etymologyButton &&
            etymologyButton.Parent is InlineUIContainer && !Theme.CanDragFrom(etymologyButton),
            "etymology is an accessible headword icon with a closed, anchored light-dismiss popover");
        Check(etymologyButton.Content is System.Windows.Shapes.Path icon && !icon.Data.Bounds.IsEmpty && icon.Width == 18 && etymologyButton.Width == 32,
            "Rosetta-stone geometry matches the other lexical icon and hit-target sizes");
        main.ApplySourceEtymology([new("Etymology","An initialism.","https://example.org/source")]);
        Check(!popup.IsOpen && Contents(etymology,true).OfType<TextBox>().Count()==2,
            "late source etymology updates selectable content without opening the popover");
        main.RenderAnswers(snapshot,null);
        Check(Contents(etymology,true).OfType<TextBox>().Count()==2 && !popup.IsOpen,
            "unchanged model updates retain both source and model origin content without duplicating it");
        main.ResetWordForms();
        main.ApplyLocalWordForms("TEE",[new("tee",[new("p","过去式",["teed"])],"fixture","")]);
        Check(((Panel)main.FindName("FormsPanel")).Children.Count==0,"uppercase queries do not display the lowercase golf verb's forms");
        main.ResetLexicalDetails();
        Check(etymology.Children.Count==0 && etymologyButton.Visibility == Visibility.Collapsed && !popup.IsOpen && ((FrameworkElement)main.FindName("QuickReadCard")).Visibility == Visibility.Collapsed,
            "the next query or translation clears all lexical detail state");
    }
}
