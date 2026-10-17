using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Lexi.Core;
using Lexi.Host;

// The JSON-lines protocol used by the macOS app: in process with synthetic providers for
// streaming and cancellation, then the built host as a separate process.
internal static class HostChecks
{
    // fixtureOutput, when given, receives the protocol views this check produced so front-end
    // tests decode real host output (apple/Tests/LeXiKitTests/Fixtures/views.json).
    public static async Task Run(string fixture, string root, string hostAssembly, string? fixtureOutput = null)
    {
        int count = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); count++; }
        var work = Path.Combine(Path.GetFullPath(root), Guid.NewGuid().ToString("N"));
        var blocked = 0;
        var providers = new QueryProviders(
            async (model, query, evidence, options, token, preview) =>
            {
                // The first "hello" model call waits until cancelled; later visits complete normally.
                if (query == "hello" && Interlocked.Exchange(ref blocked, 1) == 0) await Task.Delay(Timeout.Infinite, token);
                var entry = new AiEntry(query, true, [new("noun", model.Name + " meaning", "合成释义：" + model.Name, "", "", "", [])], "", "unverified", WordForms: []);
                preview.Report(entry); await Task.Yield(); return entry;
            },
            (request, options, token) => Task.FromResult(new SenseMergePlan(request.InputHash, request.Pairs.Select(p => new SenseVerdict(p.A, p.B, "equivalent", 0.99)).ToArray())),
            async (text, target, options, token, preview) =>
            {
                var entry = new TranslationEntry(Cache.Hash(text), target, ["船已就绪。", "保留 42。"], "");
                preview.Report(entry with { Paragraphs = ["船已就绪。"] }); await Task.Yield(); return entry;
            });
        var user = Path.Combine(work, "in-process");
        // Synthetic speech services: the key decides the answer, and every request is recorded.
        const string speechKey = "synthetic-host-speech-key";
        var speechRequests = new ConcurrentQueue<HttpRequestMessage>();
        var syntheticAudio = new byte[32]; Encoding.ASCII.GetBytes("ID3").CopyTo(syntheticAudio, 0);
        using var speechClient = new SpeechClient(new SpeechHandler(request =>
        {
            speechRequests.Enqueue(request);
            var key = request.Headers.Authorization?.Parameter ?? request.Headers.GetValues("xi-api-key").Single();
            if (key != speechKey) return new(HttpStatusCode.Unauthorized) { Content = new StringContent("private-provider-response") };
            return request.RequestUri!.AbsolutePath == "/v2/voices"
                ? new(HttpStatusCode.OK) { Content = new StringContent("{\"voices\":[{\"voice_id\":\"voice_b\",\"name\":\"Beta\"},{\"voice_id\":\"voice_a\",\"name\":\"Alpha\"}],\"has_more\":false}") }
                : new(HttpStatusCode.OK) { Content = new ByteArrayContent(syntheticAudio) };
        }));
        using (var session = new QuerySession(new(Path.Combine(work, "assets"), Path.GetFullPath(fixture), user), providers: providers, network: new NoNetwork()))
        {
            var client = new Client();
            var server = new HostServer(session, client.Input, client.Output, speechClient).RunAsync();
            var init = await client.Call("initialize");
            Check(init.GetProperty("result") is var ready && ready.GetProperty("protocol").GetInt32() == HostServer.Protocol &&
                ready.GetProperty("view").GetProperty("kind").GetString() == "home" &&
                ready.GetProperty("settings").GetProperty("models").EnumerateArray().Any(m => m.GetProperty("label").GetString() == "GPT-6 Luna · Fast") &&
                ready.GetProperty("settings").GetProperty("controls").GetProperty("onlineLabel").GetString() == "在线补充",
                "initialize reports the protocol version, the home view, shared model choices and control labels");
            Check((await client.Call("suggest", new { text = "wal" })).GetProperty("result").GetProperty("suggestions").EnumerateArray().Any(s => s.GetString() == "walk"),
                "suggestions come from the shared offline library");

            var submitted = (await client.Call("query.submit", new { text = "serendipity" })).GetProperty("result");
            var request = submitted.GetProperty("request").GetInt64();
            var views = client.Views(request);
            var final = views.Last();
            Check(final.GetProperty("revision").GetInt64() == submitted.GetProperty("revision").GetInt64() &&
                views.Select(v => v.GetProperty("revision").GetInt64()).Zip(views.Skip(1).Select(v => v.GetProperty("revision").GetInt64())).All(p => p.First < p.Second),
                "a submission streams ordered views and answers with the revision of its final view");
            Check(final.GetProperty("kind").GetString() == "dictionary" && final.GetProperty("progress") is var done &&
                done.GetProperty("outcome").GetString() == "complete" && !done.GetProperty("busy").GetBoolean() &&
                done.GetProperty("title").GetString() == "已完成 · 1 个义项" && done.GetProperty("stageLabels").GetArrayLength() == 4 &&
                views.Any(v => v.GetProperty("progress") is { ValueKind: JsonValueKind.Object } p && p.GetProperty("stage").GetString() == "supplement" && p.GetProperty("busy").GetBoolean()),
                "views carry camelCase fields, string enums and the same lifecycle as the Windows window");
            Check(final.GetProperty("answers").GetProperty("senses")[0].GetProperty("alternativesLabel").GetString() == "其他表述 · 1" &&
                final.GetProperty("quickRead").GetProperty("caption").GetString() == "TL;DR · 已整理",
                "merged senses and the overview reach the front end fully worded");

            var pending = client.Send("query.submit", new { text = "hello" });
            await client.Until(v => v.GetProperty("query").GetString() == "hello" && v.GetProperty("progress").GetProperty("stage").GetString() == "supplement");
            var cancelled = await client.Call("cancel", new { id = pending.Id });
            var interrupted = await pending.Response;
            var stopped = client.Views(interrupted.GetProperty("result").GetProperty("request").GetInt64()).Last().GetProperty("progress");
            Check(cancelled.GetProperty("result").GetProperty("cancelled").GetBoolean() && stopped.GetProperty("title").GetString() == "查询已中断" &&
                !stopped.GetProperty("busy").GetBoolean(), "cancelling a running submission ends the query with an interrupted, settled state");

            var translated = (await client.Call("query.submit", new { text = "The ship is ready.\nKeep 42 intact." })).GetProperty("result");
            var translationViews = client.Views(translated.GetProperty("request").GetInt64());
            Check(translationViews.Last().GetProperty("translation").GetProperty("paragraphs").GetArrayLength() == 2 &&
                translationViews.Any(v => v.GetProperty("translation") is { ValueKind: JsonValueKind.Object } t && !t.GetProperty("complete").GetBoolean()),
                "translation previews stream before the complete paragraphs");
            if (fixtureOutput != null)
                File.WriteAllText(fixtureOutput, JsonSerializer.Serialize(new { dictionary = final, translation = translationViews.Last(),
                    preview = translationViews.First(v => v.GetProperty("translation").ValueKind == JsonValueKind.Object && !v.GetProperty("translation").GetProperty("complete").GetBoolean()),
                    streaming = views.First(v => v.GetProperty("progress") is { ValueKind: JsonValueKind.Object } p && p.GetProperty("busy").GetBoolean() && v.GetProperty("answers").ValueKind == JsonValueKind.Object),
                    home = ready.GetProperty("view") }, new JsonSerializerOptions(HostServer.Options) { WriteIndented = true }) + "\n");
            var history = (await client.Call("history.list")).GetProperty("result").GetProperty("entries");
            Check(history[0].GetProperty("kind").GetString() == "翻译" && history[0].GetProperty("preview").GetString() == "The ship is ready. Keep 42 intact." &&
                (await client.Call("history.remove", new { query = history[0].GetProperty("query").GetString(), translation = true })).GetProperty("result").GetProperty("removed").GetBoolean(),
                "history exposes previews and kinds and removes single entries");

            var page = (await client.Call("query.navigate", new { direction = -1 })).GetProperty("result").GetProperty("page");
            Check(page.GetProperty("query").GetString() == "hello" &&
                (await client.Call("query.navigate", new { direction = 2 })).GetProperty("error").GetProperty("code").GetString() == "invalid_params",
                "navigation moves through the session trail and rejects other offsets");

            var duplicate = await client.Call("settings.update", new { settings = new { parallelLookup = true, secondaryProvider = "codex", secondaryModel = "" } });
            Check(duplicate.GetProperty("error").GetProperty("code").GetString() == "invalid_settings" &&
                duplicate.GetProperty("error").GetProperty("message").GetString() == "请选择两个不同的查询模型。",
                "settings use the same validation message as the Windows dialog");
            Check((await client.Call("settings.update", new { settings = new { unknown = 1 } })).GetProperty("error").GetProperty("code").GetString() == "invalid_params" &&
                (await client.Call("settings.update", new { settings = new { online = "yes" } })).GetProperty("error").GetProperty("code").GetString() == "invalid_params",
                "unknown settings and wrong value types are rejected without saving");
            var updated = (await client.Call("settings.update", new { settings = new { mergeSenses = false, reasoningEffort = "high" } })).GetProperty("result").GetProperty("settings");
            Check(!updated.GetProperty("mergeSenses").GetBoolean() && updated.GetProperty("reasoningEffort").GetString() == "low" &&
                !AppSettings.Load(user).MergeSenses, "valid settings are saved with the fixed Codex options");
            const string modelKey = "synthetic-host-model-key";
            var keySet = await client.Call("keys.set", new { provider = "anthropic", key = modelKey });
            Check(keySet.GetProperty("result").ValueKind == JsonValueKind.Object && !keySet.GetRawText().Contains(modelKey) &&
                (await client.Call("keys.set", new { provider = "codex", key = modelKey })).GetProperty("error").GetProperty("code").GetString() == "invalid_params" &&
                !Directory.EnumerateFiles(user, "*", SearchOption.AllDirectories).Any(f => File.ReadAllText(f).Contains(modelKey)),
                "model API keys are taken for API services only and never returned or written");
            await CheckSpeech(client, user, speechKey, syntheticAudio, speechRequests, Check);
            var offline = (await client.Call("settings.setOnline", new { online = false })).GetProperty("result");
            Check(offline.GetProperty("controls").GetProperty("onlineLabel").GetString() == "离线查询" && !AppSettings.Load(user).Online &&
                client.Latest().GetProperty("status").GetString()!.StartsWith("查询模式已更改"), "the online switch saves and keeps the current result");
            var replay = new { text = "US", provider = "openai", model = "gpt-4o-mini-tts", voice = "coral" };
            var requestsBefore = speechRequests.Count;
            Check((await client.Call("speech.synthesize", replay)).GetProperty("result").GetProperty("path").GetString()!.EndsWith(".mp3") &&
                SpeechError(await client.Call("speech.synthesize", replay with { text = "uncached" })) == "离线模式下没有此声线的缓存发音。" &&
                SpeechError(await client.Call("speech.elevenVoices", new { key = speechKey })) == "请先开启在线补充，再读取声线列表。" &&
                speechRequests.Count == requestsBefore, "offline speech replays cached audio and reports missing audio without contacting a service");

            var login = (await client.Call("connection.loginCommand", new { provider = "codex", codexPath = Environment.ProcessPath })).GetProperty("result");
            Check(login.GetProperty("fileName").GetString() == Environment.ProcessPath && login.GetProperty("arguments").EnumerateArray().Select(a => a.GetString()).SequenceEqual(["login"]) &&
                (await client.Call("connection.status", new { provider = "other" })).GetProperty("error").GetProperty("code").GetString() == "invalid_params",
                "sign-in commands come from the shared client discovery and unknown providers are refused");
            Check((await client.Call("dictionaries.list")).GetProperty("result").GetProperty("dictionaries").EnumerateArray().Single().GetProperty("available").GetBoolean(),
                "installed dictionaries are listed for settings");

            client.Raw("not json"); client.Raw("{\"method\":\"query.view\"}");
            var unknown = await client.Call("query.unknown");
            Check(client.Orphans.Count == 2 && client.Orphans.Select(o => o.GetProperty("error").GetProperty("code").GetString()).Order().SequenceEqual(["invalid_request", "parse_error"]) &&
                unknown.GetProperty("error").GetProperty("code").GetString() == "method_not_found", "malformed, anonymous and unknown requests get protocol errors");
            var shutdown = client.Send("shutdown");
            await server;
            Check((await shutdown.Response).TryGetProperty("result", out _) && client.LastMessageId == shutdown.Id, "shutdown is answered last and ends the server");
        }

        // The built executable, offline: argument handling, stdout hygiene and exit on end of input.
        var host = Path.Combine(work, "process");
        Directory.CreateDirectory(host);
        File.WriteAllText(Path.Combine(host, "settings.json"), "{\"online\":false}");
        var (exit, lines, errors) = await RunHost(hostAssembly, ["--data", Path.GetFullPath(fixture), "--user", host],
            ["{\"id\":1,\"method\":\"initialize\"}", "{\"id\":2,\"method\":\"query.submit\",\"params\":{\"text\":\"walked\"}}",
             "{\"id\":3,\"method\":\"speech.models\"}",
             "{\"id\":4,\"method\":\"speech.synthesize\",\"params\":{\"text\":\"walked\",\"provider\":\"xai\",\"model\":\"grok-tts\",\"voice\":\"eve\",\"key\":\"" + speechKey + "\"}}",
             "{\"id\":5,\"method\":\"shutdown\"}"]);
        var messages = lines.Select(l => JsonDocument.Parse(l).RootElement).ToArray();
        var last = messages.Where(m => m.TryGetProperty("event", out _)).Last().GetProperty("data");
        Check(exit == 0 && errors.Length == 0 && last.GetProperty("local")[0].GetProperty("chinese").GetString() == "步行" &&
            last.GetProperty("wordForms")[0].GetProperty("cells")[0].GetProperty("spelling").GetString() == "walked → walk" &&
            messages.Last().GetProperty("id").GetInt32() == 5, "the host process answers on stdout only and exits after shutdown");
        JsonElement Answer(int id) => messages.Single(m => m.TryGetProperty("id", out var value) && value.ValueKind == JsonValueKind.Number && value.GetInt32() == id);
        Check(Answer(3).GetProperty("result").GetProperty("models")[0].GetProperty("name").GetString() == SpeechCatalog.Local.Name &&
            SpeechError(Answer(4)) == "离线模式下没有此声线的缓存发音。" && !lines.Any(l => l.Contains(speechKey)) && !errors.Contains(speechKey),
            "the built host lists speech models and refuses offline generation without echoing the key");
        (exit, lines, _) = await RunHost(hostAssembly, ["--user", host, "--data", Path.GetFullPath(fixture)], ["{\"id\":1,\"method\":\"query.view\"}"]);
        Check(exit == 0 && lines.Length == 1, "closing standard input ends the host without a shutdown request");
        (exit, _, errors) = await RunHost(hostAssembly, ["--unknown"], []);
        Check(exit == 2 && errors.Contains("usage"), "unknown arguments are rejected before any protocol output");
        Console.WriteLine($"{count} host protocol checks passed");
    }

    // Speech through the protocol with synthetic services; the session is online.
    private static async Task CheckSpeech(Client client, string user, string key, byte[] audio, ConcurrentQueue<HttpRequestMessage> requests, Action<bool, string> check)
    {
        var models = (await client.Call("speech.models")).GetProperty("result").GetProperty("models").EnumerateArray().ToArray();
        check(models.Length == SpeechCatalog.Models.Length && models[0].GetProperty("local").GetBoolean() &&
            models[0].GetProperty("name").GetString() == SpeechCatalog.Local.Name && models.Skip(1).All(m => !m.GetProperty("local").GetBoolean()) &&
            models.Count(m => m.GetProperty("provider").GetString() == "elevenlabs") == 5,
            "speech models come from the shared catalog with this platform's local voices first");
        int Voices(string provider, string model) =>
            client.Call("speech.voices", new { provider, model }).Result.GetProperty("result").GetProperty("voices").GetArrayLength();
        check(Voices("openai", "gpt-4o-mini-tts") == 13 && Voices("xai", "grok-tts") == 5 && Voices("elevenlabs", "eleven_v3") == 1 &&
            Voices(SpeechCatalog.Local.Provider, SpeechCatalog.Local.Id) == 0, "catalog voices follow the model; system voices are listed by the front end");

        var account = (await client.Call("speech.elevenVoices", new { key })).GetProperty("result").GetProperty("voices");
        check(account.EnumerateArray().Select(v => v.GetProperty("id").GetString() + "=" + v.GetProperty("name").GetString()).SequenceEqual(["voice_a=Alpha", "voice_b=Beta"]) &&
            requests.Last().RequestUri!.AbsolutePath == "/v2/voices", "ElevenLabs account voices load with the key supplied for this call");
        check(SpeechError(await client.Call("speech.elevenVoices")) == "请在设置 → 发音的在线语音连接中填写所选服务的 API Key。" &&
            SpeechError(await client.Call("speech.elevenVoices", new { key = "wrong-key" })) is { } rejected && rejected.Contains("API Key") && !rejected.Contains("private"),
            "voice loading without a key or with a rejected key reports Core's sanitized wording");

        var choice = new { text = "US", provider = "openai", model = "gpt-4o-mini-tts", voice = "coral" };
        var probe = (await client.Call("speech.cached", choice)).GetProperty("result");
        check(probe.GetProperty("path").ValueKind == JsonValueKind.Null && !probe.GetProperty("local").GetBoolean() &&
            (await client.Call("speech.cached", new { text = "US", provider = SpeechCatalog.Local.Provider, model = SpeechCatalog.Local.Id, voice = "auto" }))
                .GetProperty("result").GetProperty("local").GetBoolean(), "the cache probe reports missing audio and marks local voices for the front end");
        var before = requests.Count;
        var path = (await client.Call("speech.synthesize", new { choice.text, choice.provider, choice.model, choice.voice, key })).GetProperty("result").GetProperty("path").GetString()!;
        check(path == Path.Combine(user, "speech-cache", new SpeechOptions(choice.provider, choice.model, choice.voice).CacheKey(choice.text) + ".mp3") &&
            File.ReadAllBytes(path).SequenceEqual(audio) && requests.Count == before + 1 && requests.Last().Headers.Authorization?.Parameter == key,
            "synthesis writes the shared provider/model/voice cache file and returns its path");
        check((await client.Call("speech.synthesize", choice)).GetProperty("result").GetProperty("path").GetString() == path &&
            (await client.Call("speech.cached", choice)).GetProperty("result").GetProperty("path").GetString() == path && requests.Count == before + 1,
            "cached audio is returned without a key or another request");
        var lower = (await client.Call("speech.synthesize", new { text = "us", choice.provider, choice.model, choice.voice, key })).GetProperty("result").GetProperty("path").GetString();
        var alloy = (await client.Call("speech.synthesize", new { choice.text, choice.provider, choice.model, voice = "alloy", key })).GetProperty("result").GetProperty("path").GetString();
        check(new[] { path, lower, alloy }.Distinct().Count() == 3 && requests.Count == before + 3, "case and voice keep separate audio files");

        check(SpeechError(await client.Call("speech.synthesize", new { text = "new", choice.provider, choice.model, choice.voice })) == "请在设置 → 发音的在线语音连接中填写所选服务的 API Key。" &&
            SpeechError(await client.Call("speech.synthesize", new { text = "new", choice.provider, model = "tts-1", voice = "marin", key })) == "请在设置中选择这个模型支持的声线。" &&
            SpeechError(await client.Call("speech.synthesize", new { text = "new", provider = SpeechCatalog.Local.Provider, model = SpeechCatalog.Local.Id, voice = "auto", key })) == "此模型不使用在线语音接口。" &&
            SpeechError(await client.Call("speech.synthesize", new { text = "new", provider = "elevenlabs", model = "eleven_v3", voice = "../voice", key })) == "请选择 ElevenLabs 声线，或填写有效的 Voice ID。",
            "missing keys, unsupported voices and local voices fail with the Windows wording before any request");
        var refused = SpeechError(await client.Call("speech.synthesize", new { text = "new", choice.provider, choice.model, choice.voice, key = "wrong-key" }));
        check(refused == "语音服务拒绝认证，请检查 API Key 和接口权限。" && (await client.Call("speech.cached", new { text = "new", choice.provider, choice.model, choice.voice }))
            .GetProperty("result").GetProperty("path").ValueKind == JsonValueKind.Null, "rejected keys report Core's message and leave nothing in the cache");
        check((await client.Call("speech.synthesize", new { choice.provider, choice.model, choice.voice })).GetProperty("error").GetProperty("code").GetString() == "invalid_params",
            "speech requests without text are protocol errors");

        var invalid = await client.Call("settings.update", new { settings = new { speechProvider = "openai", speechModel = "tts-1", speechVoice = "marin" } });
        check(invalid.GetProperty("error").GetProperty("code").GetString() == "invalid_settings" &&
            invalid.GetProperty("error").GetProperty("message").GetString() == "请在设置中选择这个模型支持的声线。", "speech settings use the Windows dialog's validation");
        var saved = (await client.Call("settings.update", new { settings = new { speechProvider = "elevenlabs", speechModel = "eleven_v3", speechVoice = "custom_voice", speechVoiceName = "自定义声线" } }))
            .GetProperty("result").GetProperty("settings");
        check(saved.GetProperty("speechVoice").GetString() == "custom_voice" && AppSettings.Load(user) is { SpeechProvider: "elevenlabs", SpeechVoiceName: "自定义声线" },
            "a manual ElevenLabs Voice ID is saved with its display name");
        check(!client.Echoed(key) && !Directory.EnumerateFiles(user, "*", SearchOption.AllDirectories).Any(f => File.ReadAllText(f).Contains(key)),
            "speech keys are never returned, published or written to the user folder");
    }

    private static string? SpeechError(JsonElement response) =>
        response.TryGetProperty("error", out var error) && error.GetProperty("code").GetString() == "speech_failed" ? error.GetProperty("message").GetString() : null;

    private static async Task<(int Exit, string[] Lines, string Errors)> RunHost(string assembly, string[] arguments, string[] requests)
    {
        // A framework-dependent build runs through dotnet; a published single-file host runs directly.
        var managed = assembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
        var info = new ProcessStartInfo(managed ? "dotnet" : Path.GetFullPath(assembly)) { RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false, StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8 };
        if (managed) info.ArgumentList.Add(Path.GetFullPath(assembly));
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        foreach (var request in requests) await process.StandardInput.WriteLineAsync(request);
        process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await process.WaitForExitAsync(timeout.Token);
        return (process.ExitCode, (await output).Split('\n', StringSplitOptions.RemoveEmptyEntries), await errors);
    }

    private sealed class Client
    {
        private readonly Channel<string?> requests = Channel.CreateUnbounded<string?>();
        private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> waiting = new();
        private readonly List<JsonElement> views = [];
        private readonly ConcurrentQueue<string> lines = new();
        private int next;
        public LineReader Input { get; }
        public LineWriter Output { get; }
        public List<JsonElement> Orphans { get; } = [];
        public int LastMessageId { get; private set; }
        public Client() { Input = new(requests); Output = new(Receive); }

        public sealed record Pending(int Id, Task<JsonElement> Response);
        public Pending Send(string method, object? parameters = null)
        {
            var id = Interlocked.Increment(ref next);
            var response = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            waiting[id] = response;
            Raw(JsonSerializer.Serialize(new { id, method, @params = parameters }));
            return new(id, response.Task);
        }
        public async Task<JsonElement> Call(string method, object? parameters = null) =>
            await Send(method, parameters).Response.WaitAsync(TimeSpan.FromSeconds(30));
        public void Raw(string line) => requests.Writer.TryWrite(line);

        public List<JsonElement> Views(long request) { lock (views) return views.Where(v => v.GetProperty("request").GetInt64() == request).ToList(); }
        public JsonElement Latest() { lock (views) return views[^1]; }
        public async Task Until(Func<JsonElement, bool> condition)
        {
            for (var i = 0; i < 500; i++) { lock (views) if (views.Any(condition)) return; await Task.Delay(10); }
            throw new TimeoutException("FAIL: expected host view was not published");
        }

        public bool Echoed(string value) => lines.Any(l => l.Contains(value));

        private void Receive(string line)
        {
            lines.Enqueue(line);
            var message = JsonDocument.Parse(line).RootElement.Clone();
            if (message.TryGetProperty("event", out _)) { lock (views) views.Add(message.GetProperty("data")); return; }
            if (message.GetProperty("id").ValueKind != JsonValueKind.Number) { lock (Orphans) Orphans.Add(message); return; }
            LastMessageId = message.GetProperty("id").GetInt32();
            if (waiting.TryRemove(LastMessageId, out var response)) response.TrySetResult(message);
        }
    }

    private sealed class LineReader(Channel<string?> lines) : TextReader
    {
        public override async ValueTask<string?> ReadLineAsync(CancellationToken token)
        {
            try { return await lines.Reader.ReadAsync(token); }
            catch (ChannelClosedException) { return null; }
        }
    }

    private sealed class LineWriter(Action<string> receive) : TextWriter
    {
        private readonly StringBuilder pending = new();
        public override Encoding Encoding => Encoding.UTF8;
        public override void Write(char value)
        {
            lock (pending)
            {
                if (value != '\n') { pending.Append(value); return; }
                var line = pending.ToString(); pending.Clear(); receive(line);
            }
        }
        public override void Write(string? value) { foreach (var c in value ?? "") Write(c); }
        public override Task WriteAsync(string? value) { Write(value); return Task.CompletedTask; }
        public override Task WriteAsync(char value) { Write(value); return Task.CompletedTask; }
        public override Task FlushAsync() => Task.CompletedTask;
    }

    private sealed class SpeechHandler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(send(request));
    }

    private sealed class NoNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request });
    }
}
