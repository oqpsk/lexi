using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Lexi;
using Lexi.Core;

internal static partial class Program
{
    [STAThread]
    private static void Main()
    {
        // Construct and measure resources only: no visible windows, screenshots,
        // simulated input, application message loop or online provider calls.
        // Validate executable content paths, independently of the test host resource assembly.
        var resourceAssembly = typeof(App).Assembly;
        using (var resources = new System.Resources.ResourceReader(resourceAssembly.GetManifestResourceStream("Lexi.g.resources")!))
        {
            var names = resources.Cast<System.Collections.DictionaryEntry>().Select(x => (string)x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var content in resourceAssembly.GetCustomAttributes(typeof(System.Windows.Resources.AssemblyAssociatedContentFileAttribute), false)
                .Cast<System.Windows.Resources.AssemblyAssociatedContentFileAttribute>())
                if (names.Contains(content.RelativeContentFilePath.Replace((char)92, '/')))
                    throw new Exception("Packaged content shadows an embedded WPF resource: " + content.RelativeContentFilePath);
            Console.WriteLine("PASS: loose shell assets never shadow embedded WPF resource paths in the executable.");
        }
        var app = new App(); app.InitializeComponent();
        var main = new MainWindow();
        var input = (TextBox)main.FindName("SearchBox");
        if (System.Windows.Input.FocusManager.GetFocusedElement(main) != input)
            throw new Exception("The initial logical focus target must be the search input.");
        Console.WriteLine("PASS: search input is the initial logical focus target without displaying a window.");
        input.Text = "synthetic"; input.Select(3, 0);
        main.FocusSearch();
        if (input.SelectionStart != 3 || input.SelectionLength != 0 || input.IsKeyboardFocused)
            throw new Exception("An inactive hidden main window must not redirect focus or alter the draft selection.");
        Console.WriteLine("PASS: focus restoration leaves inactive windows and draft selection alone.");
        input.Clear();
        Measure((FrameworkElement)main.Content, 558, 398);
        Measure((FrameworkElement)main.Content, 718, 618);
        Measure((FrameworkElement)main.Content, 1078, 798);
        var dictionary = new InstalledDictionary("Synthetic dictionary", "en / zh", 42, 1024,
            "synthetic-data", "https://example.org/dictionary", "Test license", "Installed", true, "synthetic");
        var settings = new SettingsWindow(new AppSettings { Model = "gpt-6.1-sol" }, [dictionary]);
        Measure((FrameworkElement)settings.Content, 538, 478);
        var tabs = Descendants((DependencyObject)settings.Content).OfType<TabControl>().Single();
        foreach (TabItem tab in tabs.Items)
        {
            tabs.SelectedItem = tab;
            Measure((FrameworkElement)settings.Content, 618, 663);
        }
        var panel = new StackPanel { Width = 480 };
        panel.Children.Add(new TextBox { Text = "中文与 English" });
        panel.Children.Add(new CheckBox { Content = "Enabled", IsChecked = true });
        panel.Children.Add(new ComboBox { ItemsSource = new[] { "One", "Two" }, SelectedIndex = 0 });
        panel.Children.Add(new ComboBoxItem { Content = "Menu item" });
        panel.Children.Add(new ListBoxItem { Content = "Suggestion", IsSelected = true });
        panel.Children.Add(new Expander { Header = "Source", Content = new TextBlock { Text = "Source text" }, IsExpanded = true });
        panel.Children.Add(new TextBox { Style = (Style)app.FindResource("Selectable"), Text = "Read-only text" });
        Measure(panel, 480, 600);
        Console.WriteLine("PASS: WPF resources, window content and control templates construct and measure without displaying windows.");
        CheckChrome(app, main, settings, panel);
        CheckParallelResults(main, settings);
        CheckReadAloud(main);
        CheckDensity(main);
        CheckQueryProgress(app, main);
        CheckSpeechPresentation(main, settings);
        CheckDictionaryUsability(main, settings);
        CheckModelMorphology(main);
        CheckLexicalDetails(main);
        CheckQuickRead(main);
        CheckHistory(main);
        CheckSuggestions(main);
        CheckWindowNavigation(main, settings);
        CheckWindowMemory();
        CheckNativeWindowMemory();
        CheckPaletteAndSavedView(app, main, settings);
        CheckNativeFrame();
    }
    private static void CheckChrome(App app, MainWindow main, SettingsWindow settings, StackPanel panel)
    {
        int count = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); count++; }
        Check(AppContext.TryGetSwitch("Switch.System.Windows.Controls.Text.UseAdornerForTextboxSelectionRendering", out var adorner) && !adorner,
            "selection uses the text renderer instead of an opaque overlay");
        var boxes = Descendants((DependencyObject)main.Content).OfType<TextBox>().Concat(panel.Children.OfType<TextBox>()).ToArray();
        Check(boxes.Length >= 3 && boxes.All(b => b.SelectionOpacity == 1 &&
            b.SelectionTextBrush is SolidColorBrush ink && b.SelectionBrush is SolidColorBrush selection && Contrast(ink.Color, selection.Color) >= 4.5),
            "editable and read-only selections retain contrasting text");
        foreach (var name in new[] { "SearchIcon", "CloseIcon", "EnterIcon", "SettingsIcon", "ChevronRightIcon", "ChevronDownIcon" })
        {
            var icon = Theme.Icon(name); Measure(icon, 16, 16);
            Check(!icon.Data.Bounds.IsEmpty && icon.Stroke != null && icon.RenderSize.Width > 0, name + " uses drawable vector geometry");
        }
        Check(!Descendants((DependencyObject)main.Content).Concat(Descendants((DependencyObject)settings.Content)).OfType<TextBlock>()
            .Any(t => t.Text.Any(c => c is >= '\uE000' and <= '\uF8FF')), "window controls have no private-use font glyphs");
        Check(Theme.CanDragFrom((DependencyObject)main.Content) && Theme.CanDragFrom((DependencyObject)main.FindName("Headword")) &&
            Theme.CanDragFrom((DependencyObject)main.FindName("LookupStatus")), "frame, result heading and bottom status text are draggable");
        var search = (TextBox)main.FindName("SearchBox");
        Check(!Theme.CanDragFrom(search) && Descendants(search).All(d => !Theme.CanDragFrom(d)), "input and its internal visuals never start a window drag");
        var settingsButton = Descendants((DependencyObject)main.Content).OfType<Button>().Single(b => AutomationProperties.GetName(b) == "设置");
        Check(!Theme.CanDragFrom(settingsButton) && !Theme.CanDragFrom((DependencyObject)settingsButton.Content), "button icons remain clickable instead of dragging the window");
        var host = new Window { Content = panel };
        Check(panel.Children.OfType<TextBox>().All(b => !Theme.CanDragFrom(b)) &&
            panel.Children.OfType<CheckBox>().All(b => !Theme.CanDragFrom(b)) &&
            panel.Children.OfType<ComboBox>().All(b => !Theme.CanDragFrom(b)), "text selection, toggles and dropdowns are excluded from dragging");
        Check(Theme.SupportsGlass(new Version(10, 0, 22621), true, true, false) &&
            !Theme.SupportsGlass(new Version(10, 0, 22000), true, true, false) &&
            !Theme.SupportsGlass(new Version(10, 0, 26200), false, true, false) &&
            !Theme.SupportsGlass(new Version(10, 0, 26200), true, false, false) &&
            !Theme.SupportsGlass(new Version(10, 0, 26200), true, true, true), "glass availability respects OS, composition and accessibility preferences");
        Check(main.Opacity == 1 && !main.AllowsTransparency && ((Border)main.Content).Background is SolidColorBrush fallback && fallback.Color.A == 255,
            "the full window stays opaque and has a solid fallback until native glass succeeds");
        var online = (ToggleButton)main.FindName("OnlineToggle");
        var toggle = (IToggleProvider)new ToggleButtonAutomationPeer(online).GetPattern(PatternInterface.Toggle);
        var savedState = online.IsChecked;
        online.IsChecked = false;
        Check(toggle.ToggleState == ToggleState.Off && !Theme.CanDragFrom(online), "offline switch exposes an accessible off state and does not drag the window");
        online.IsChecked = true;
        Check(toggle.ToggleState == ToggleState.On, "online switch exposes an accessible on state");
        online.IsChecked = savedState;
        var refresh = (Button)main.FindName("RefreshButton");
        refresh.Visibility = Visibility.Visible;
        var status = (TextBlock)main.FindName("LookupStatus");
        var oldText = status.Text; status.Text = "首条释义 2.6 秒 · 查询完成 · 同义项已整理";
        Measure((FrameworkElement)main.Content, 558, 398);
        var bar = (Border)main.FindName("StatusBar");
        var statusRight = status.TranslatePoint(new Point(status.ActualWidth, 0), bar).X;
        var buttonLeft = refresh.TranslatePoint(new Point(0, 0), bar).X;
        var switchRight = online.TranslatePoint(new Point(online.ActualWidth, 0), bar).X;
        Check(status.ActualWidth >= 160 && statusRight <= buttonLeft && switchRight <= bar.ActualWidth && bar.ActualHeight <= 48,
            "single bottom bar preserves readable status and non-overlapping actions at minimum window width");
        refresh.Visibility = Visibility.Collapsed; status.Text = oldText;
        Console.WriteLine($"{count} presentation regression checks passed");
    }
    private static double Contrast(Color a, Color b)
    {
        static double Linear(byte value) { var channel = value / 255.0; return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4); }
        static double Light(Color c) => 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
        var x = Light(a); var y = Light(b); return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }
    private static void Measure(FrameworkElement element, double width, double height)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        foreach (var control in Descendants(element).OfType<Control>()) control.ApplyTemplate();
        if (double.IsNaN(element.DesiredSize.Width) || double.IsNaN(element.DesiredSize.Height)) throw new InvalidOperationException("Invalid layout size.");
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static IEnumerable<DependencyObject> Contents(DependencyObject parent, bool visibleOnly = false)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            if (visibleOnly && child is UIElement element && element.Visibility != Visibility.Visible) continue;
            yield return child;
            foreach (var nested in Contents(child, visibleOnly)) yield return nested;
        }
    }
}
