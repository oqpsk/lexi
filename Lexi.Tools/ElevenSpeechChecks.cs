using System.Net;
using System.Text;
using System.Text.Json;
using Lexi.Core;

internal static class ElevenSpeechChecks
{
    public static async Task Run()
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); }
        var options = new SpeechOptions("elevenlabs", "eleven_flash_v2_5", "synthetic_voice");
        using (var request = SpeechClient.Request("TEE", options, "synthetic"))
        {
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Check(request.RequestUri!.AbsoluteUri == "https://api.elevenlabs.io/v1/text-to-speech/synthetic_voice?output_format=mp3_44100_128" &&
                request.Headers.GetValues("xi-api-key").Single() == "synthetic" && request.Headers.Authorization == null &&
                payload.RootElement.GetProperty("text").GetString() == "TEE" && payload.RootElement.GetProperty("model_id").GetString() == options.Model,
                "ElevenLabs speech uses its official route, key header, model and case-preserving text");
        }
        foreach (var voice in new[] { "../voice", "voice?query", "voice/child", "voice name", "voice\nvalue", "" })
        {
            try { SpeechCatalog.Validate("word", options with { Voice=voice }); Check(false, "invalid ElevenLabs voice"); }
            catch (InvalidOperationException) { Check(true, "invalid ElevenLabs voice IDs cannot alter the request route"); }
        }
        Check(SpeechCatalog.Models.Count(m => m.Provider == "elevenlabs") == 5 &&
            options.CacheKey("word") != (options with { Voice="another_voice" }).CacheKey("word") &&
            options.CacheKey("word") != (options with { Model="eleven_v4" }).CacheKey("word") &&
            options.CacheKey("TEE") != options.CacheKey("tee"), "ElevenLabs catalog and audio cache distinguish model, voice and input case");
        int calls = 0;
        using (var client = new SpeechClient(new Handler((request, _) =>
        {
            calls++;
            Check(request.Method == HttpMethod.Get && request.RequestUri!.Host == "api.elevenlabs.io" &&
                request.RequestUri.AbsolutePath == "/v2/voices" && request.Headers.GetValues("xi-api-key").Single() == "synthetic",
                "voice discovery only sends the supplied key to the official ElevenLabs host");
            if (calls == 1) return Reply("{\"voices\":[{\"voice_id\":\"voice_a\",\"name\":\"Alpha\"},{\"voice_id\":\"bad/id\",\"name\":\"Invalid\"}],\"has_more\":true,\"next_page_token\":\"token + /\"}");
            Check(request.RequestUri!.Query.Contains("next_page_token=token%20%2B%20%2F"), "voice-list pagination escapes provider tokens without changing hosts");
            return Reply("{\"voices\":[{\"voice_id\":\"voice_a\",\"name\":\"Duplicate\"},{\"voice_id\":\"voice_b\",\"name\":\"Beta\"}],\"has_more\":false}");
        })))
        {
            var voices = await client.ElevenVoicesAsync("synthetic", CancellationToken.None);
            Check(calls == 2 && voices.Select(v => v.Id).SequenceEqual(new[]{"voice_a","voice_b"}) && voices[0].Name == "Alpha",
                "account voice loading follows pagination, rejects malformed IDs and deduplicates results");
        }
        using (var client = new SpeechClient(new Handler((_, _) => Reply("{\"voices\":[],\"has_more\":false}"))))
            Check((await client.ElevenVoicesAsync("synthetic",CancellationToken.None)).Length == 0, "empty ElevenLabs accounts return an empty voice list without inventing account voices");
        foreach (var invalid in new[] { "not-json", "{\"voices\":{}}", "{\"voices\":[],\"has_more\":true}" })
        using (var client = new SpeechClient(new Handler((_, _) => Reply(invalid))))
        {
            try { await client.ElevenVoicesAsync("synthetic",CancellationToken.None); Check(false,"invalid voice list"); }
            catch (InvalidDataException) { Check(true,"malformed voice lists fail with a safe actionable response"); }
        }
        int pages = 0;
        using (var client = new SpeechClient(new Handler((_, _) => { pages++; return Reply("{\"voices\":[],\"has_more\":true,\"next_page_token\":\"repeated\"}"); })))
        {
            try { await client.ElevenVoicesAsync("synthetic",CancellationToken.None); Check(false,"repeating pages"); }
            catch (InvalidDataException) { Check(pages == 2,"repeated voice-list tokens cannot create an unbounded request loop"); }
        }
        foreach (var code in new[]{"quota_exceeded","missing_permissions","voice_not_found"})
        using (var client = new SpeechClient(new Handler((_, _) => Reply(JsonSerializer.Serialize(new {detail=new{status=code,message="private-provider-response"}}),HttpStatusCode.Unauthorized))))
        {
            try { await client.GenerateAsync("word",options,"synthetic",CancellationToken.None); Check(false,"provider error"); }
            catch (InvalidOperationException error)
            {
                var expected = code == "quota_exceeded" ? "额度不足" : code == "missing_permissions" ? "权限不足" : "声线不可用";
                Check(error.Message.Contains(expected) && !error.Message.Contains("private-provider-response"),"ElevenLabs service codes produce distinct sanitized errors");
            }
        }
        using (var client = new SpeechClient(new Handler((_, _) => Reply("private-voice-response",HttpStatusCode.Unauthorized))))
        {
            try { await client.ElevenVoicesAsync("synthetic",CancellationToken.None); Check(false,"voice authentication"); }
            catch (InvalidOperationException error) { Check(!error.Message.Contains("private-voice-response") && error.Message.Contains("API Key"),"voice-list authentication never reveals the provider response body"); }
        }
        foreach (var byCode in new[] { false, true })
        foreach (var discovery in new[] { false, true })
        using (var client = new SpeechClient(new Handler((_, _) => Reply(JsonSerializer.Serialize(new
        {
            detail = new { status = byCode ? "" : "api_key_id_used_as_api_key", code = byCode ? "api_key_id_used_as_api_key" : "",
                message = "private-key-or-account-value", request_id = "private-request" }
        }), HttpStatusCode.BadRequest))))
        {
            try
            {
                if (discovery) await client.ElevenVoicesAsync("synthetic", CancellationToken.None);
                else await client.GenerateAsync("Hello.", options, "synthetic", CancellationToken.None);
                Check(false, "key ID rejection");
            }
            catch (InvalidOperationException error)
            {
                Check(error.Message.Contains("API Key ID") && error.Message.Contains("Secret Key") &&
                    !error.Message.Contains("private-"), "key IDs receive an actionable private-data-free error for voice discovery and speech in both service envelopes");
            }
        }
        using (var client = new SpeechClient(new Handler((_, _) => Reply("{\"detail\":{\"status\":\"unknown_private_value\",\"message\":\"private-body\"}}", HttpStatusCode.BadRequest))))
        {
            try { await client.GenerateAsync("Hello.", options, "synthetic", CancellationToken.None); Check(false, "unknown service error"); }
            catch (InvalidOperationException error)
            { Check(error.Message.Contains("HTTP 400") && !error.Message.Contains("private"), "unknown service codes and raw messages stay private in the fallback error"); }
        }
        var audio = new byte[32]; Encoding.ASCII.GetBytes("ID3").CopyTo(audio,0);
        using (var client = new SpeechClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(audio)}))))
            Check((await client.GenerateAsync("word",options,"synthetic",CancellationToken.None)).SequenceEqual(audio),"ElevenLabs MP3 output passes the existing bounded audio validation");
        using (var cancel = new CancellationTokenSource())
        using (var client = new SpeechClient(new Handler(async (_,token) => { cancel.Cancel(); await Task.Delay(1000,token); return new(HttpStatusCode.OK); })))
        {
            try { await client.ElevenVoicesAsync("synthetic",cancel.Token); Check(false,"voice cancellation"); }
            catch (OperationCanceledException) { Check(true,"voice loading preserves caller cancellation for model switches and dialog closure"); }
        }
        Check(JsonSerializer.Deserialize<AppSettings>("{}",Json.Flexible)!.SpeechProvider == SpeechCatalog.Local.Provider &&
            JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(new AppSettings{SpeechProvider="elevenlabs",SpeechModel=options.Model,SpeechVoice=options.Voice,SpeechVoiceName="My voice"},Json.Flexible),Json.Flexible)!.SpeechVoiceName == "My voice",
            "legacy settings retain local speech and selected ElevenLabs voice names survive settings serialization");
    }
    private static Task<HttpResponseMessage> Reply(string text, HttpStatusCode status=HttpStatusCode.OK) =>
        Task.FromResult(new HttpResponseMessage(status){Content=new StringContent(text)});
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken) => send(request,cancellationToken); }
}
