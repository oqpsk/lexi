using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Channels;
using Lexi.Core;
using Lexi.Host;

namespace Lexi.Embed;

// The host protocol inside the app's own process, for platforms that cannot start lexi-host (iOS).
// The app writes one JSON line at a time with lexi_send and receives each reply or event line
// through its callback, exactly as over standard input and output (docs/host-protocol.md).
public static class Embedded
{
    private static readonly object gate = new();
    private static Channel<string>? input;
    private static Task? server;

    // Starts the server once. callback(line, state) runs on a background thread with a UTF-8,
    // NUL-terminated line that is valid only during the call. Returns 0, or 1 if already started,
    // 2 for bad arguments and 3 when the user folder cannot be created.
    [UnmanagedCallersOnly(EntryPoint = "lexi_start")]
    public static unsafe int Start(byte* assets, byte* data, byte* user, delegate* unmanaged<byte*, void*, void> callback, void* state)
    {
        if (callback == null || Text(assets) is not { } assetRoot || Text(data) is not { } dataRoot || Text(user) is not { } userRoot) return 2;
        var target = (nint)callback; var context = (nint)state;
        return Launch(LexiPaths.Current.With(assetRoot, dataRoot, userRoot), line => Deliver(target, context, line));
    }

    private static int Launch(LexiPaths paths, Action<string> deliver)
    {
        lock (gate)
        {
            if (server != null) return 1;
            try { Directory.CreateDirectory(paths.UserRoot); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return 3; }
            input = Channel.CreateUnbounded<string>(new() { SingleReader = true });
            var reader = new LineReader(input.Reader);
            var writer = new LineWriter(deliver);
            server = Task.Run(async () =>
            {
                using var session = new QuerySession(paths);
                await new HostServer(session, reader, writer).RunAsync();
            });
            return 0;
        }
    }

    private static unsafe void Deliver(nint callback, nint state, string line)
    {
        var bytes = Encoding.UTF8.GetBytes(line + '\0');
        fixed (byte* text = bytes) ((delegate* unmanaged<byte*, void*, void>)callback)(text, (void*)state);
    }

    // Queues one request line. Returns 0, or 1 when the server is not running or input has ended.
    [UnmanagedCallersOnly(EntryPoint = "lexi_send")]
    public static unsafe int Send(byte* line) => Text(line) is { } text && input?.Writer.TryWrite(text) == true ? 0 : 1;

    // Ends the input, like closing standard input: the server finishes running requests and stops.
    [UnmanagedCallersOnly(EntryPoint = "lexi_close")]
    public static void Close() => input?.Writer.TryComplete();

    private static unsafe string? Text(byte* value) => value == null ? null : Marshal.PtrToStringUTF8((nint)value);

    private sealed class LineReader(ChannelReader<string> lines) : TextReader
    {
        public override async ValueTask<string?> ReadLineAsync(CancellationToken token)
        {
            try { return await lines.ReadAsync(token); }
            catch (ChannelClosedException) { return null; }
        }
        public override string? ReadLine() => ReadLineAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
    }

    // Collects the server's text and hands over each complete line.
    private sealed class LineWriter(Action<string> deliver) : TextWriter
    {
        private readonly StringBuilder pending = new();
        public override Encoding Encoding => Encoding.UTF8;
        public override void Write(char value)
        {
            if (value != '\n') { pending.Append(value); return; }
            var line = pending.ToString(); pending.Clear(); deliver(line);
        }
        public override void Write(string? value) { foreach (var c in value ?? "") Write(c); }
        public override Task WriteAsync(char value) { Write(value); return Task.CompletedTask; }
        public override Task WriteAsync(string? value) { Write(value); return Task.CompletedTask; }
        public override Task FlushAsync() => Task.CompletedTask;
    }
}
