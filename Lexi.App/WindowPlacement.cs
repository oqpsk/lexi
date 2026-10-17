using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Lexi.Core;

namespace Lexi;

// Windows-specific workspace coordinates. Keep Get/SetWindowPlacement paired:
// mixing these with WPF DIPs or screen coordinates makes taskbar offsets drift.
internal sealed record WindowPosition(int Left, int Top, int Right, int Bottom, bool Maximized, int Version = 1)
{
    internal bool Valid => Version == 1 && Math.Abs((long)Left) <= 100000 && Math.Abs((long)Top) <= 100000 &&
        Math.Abs((long)Right) <= 100000 && Math.Abs((long)Bottom) <= 100000 &&
        (long)Right - Left is >= 100 and <= 32768 && (long)Bottom - Top is >= 100 and <= 32768;

    internal static WindowPosition? Load(string path)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 4096) return null;
            var value = JsonSerializer.Deserialize<WindowPosition>(File.ReadAllText(path), Json.Flexible);
            return value?.Valid == true ? value : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }
    internal bool Save(string path)
    {
        if (!Valid) return false;
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(temp, JsonSerializer.Serialize(this, Json.Flexible));
            File.Move(temp, path, true); return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}

internal sealed class WindowPlacement : IDisposable
{
    private readonly Window window;
    private readonly string path;
    private readonly DispatcherTimer timer;
    private readonly WindowPosition? startup;
    private WindowPosition? last;
    private bool ready;
    internal WindowState RestoreState => last?.Maximized == true ? WindowState.Maximized : WindowState.Normal;

    internal WindowPlacement(Window window, string path)
    {
        this.window = window; this.path = path; startup = last = WindowPosition.Load(path);
        if (startup != null)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            if (startup.Maximized) window.WindowState = WindowState.Maximized;
        }
        timer = new(DispatcherPriority.Background, window.Dispatcher) { Interval = TimeSpan.FromMilliseconds(400) };
        timer.Tick += Tick;
        window.SourceInitialized += Initialize;
        window.ContentRendered += Rendered;
        window.LocationChanged += Changed;
        window.SizeChanged += Resized;
        window.StateChanged += StateChanged;
        window.Closing += Closing;
        window.Closed += Closed;
    }
    private void Initialize(object? sender, EventArgs e)
    {
        // Stage native bounds before the first frame without making the HWND visible.
        // WPF/chrome can still change placement during startup. Never save it here.
        if (startup != null) Apply(startup);
    }
    private void Rendered(object? sender, EventArgs e) => CompleteStartup();
    internal void CompleteStartup()
    {
        if (ready) return;
        // Keep the original saved rectangle immutable until layout/chrome has settled.
        // Loaded can run while WPF is still creating the native window; saving then
        // can replace the stored position before restoration is complete.
        if (startup != null && !Apply(startup)) return;
        ready = true;
        window.ContentRendered -= Rendered;
        Flush();
    }
    private bool Apply(WindowPosition value)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return false;
        var native = ToNative(value);
        // SW_HIDE preserves a never-shown test/initialization HWND. SW_SHOWNOACTIVATE
        // restores normal bounds without stealing focus. Windows also relocates
        // off-screen workspace rectangles after a monitor is disconnected.
        if (!IsWindowVisible(handle)) native.Show = 0;
        else if (!value.Maximized) native.Show = 4;
        return SetWindowPlacement(handle, ref native);
    }
    private void Changed(object? sender, EventArgs e) { if (ready) { timer.Stop(); timer.Start(); } }
    private void Resized(object sender, SizeChangedEventArgs e) => Changed(sender, e);
    private void StateChanged(object? sender, EventArgs e) => Flush();
    private void Closing(object? sender, CancelEventArgs e) => Flush();
    private void Closed(object? sender, EventArgs e) => Dispose();
    private void Tick(object? sender, EventArgs e) => Flush();
    private void Flush()
    {
        timer.Stop();
        if (!ready) return;
        var native = new Placement { Length = Marshal.SizeOf<Placement>() };
        if (!GetWindowPlacement(new WindowInteropHelper(window).Handle, ref native) || FromNative(native) is not { } value) return;
        if (value != last && value.Save(path)) last = value;
    }
    internal static WindowPosition? FromNative(Placement value)
    {
        var rect = value.Normal;
        var result = new WindowPosition(rect.Left, rect.Top, rect.Right, rect.Bottom,
            value.Show == 3 || value.Show is 2 or 6 or 7 or 11 && (value.Flags & 2) != 0);
        return result.Valid ? result : null;
    }
    internal static Placement ToNative(WindowPosition value) => new()
    {
        Length = Marshal.SizeOf<Placement>(), Show = value.Maximized ? 3 : 1,
        Normal = new() { Left = value.Left, Top = value.Top, Right = value.Right, Bottom = value.Bottom },
        Min = new() { X = -1, Y = -1 }, Max = new() { X = -1, Y = -1 }
    };
    public void Dispose()
    {
        timer.Stop(); timer.Tick -= Tick; ready = false;
        window.SourceInitialized -= Initialize; window.ContentRendered -= Rendered;
        window.LocationChanged -= Changed; window.SizeChanged -= Resized;
        window.StateChanged -= StateChanged; window.Closing -= Closing; window.Closed -= Closed;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct Placement
    { public int Length, Flags, Show; public Point Min, Max; public Rect Normal; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowPlacement(IntPtr hwnd, ref Placement placement);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPlacement(IntPtr hwnd, ref Placement placement);
}
