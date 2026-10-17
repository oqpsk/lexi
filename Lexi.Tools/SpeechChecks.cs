using System.Net;
using System.Text;
using System.Text.Json;
using Lexi.Core;

internal static class SpeechChecks
{
    public static async Task Run()
    {
        int count = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); count++; }
        var wiki = "==English==\n===Pronunciation===\n{{IPA|en|/t͡ʃʌd/|a=UK}}\n{{IPA|en|/t͡ʃʌd/}}\n==Czech==\n{{IPA|en|/wrong-language/}}";
        Check(Pronunciation.ParseEnglish(wiki).SequenceEqual(new[] { "/t͡ʃʌd/" }), "IPA extraction scopes English, ignores qualifiers and deduplicates pronunciations");
        Check(Pronunciation.ParseEnglish("==French==\n{{IPA|fr|/x/}}").Length == 0 && !Pronunciation.Valid("/x/\nunsafe"), "missing English and malformed IPA stay absent");
        using (var phonetics = new Pronunciation(new Handler((request, _) =>
        {
            Check(request.RequestUri!.Host == "en.wiktionary.org" && request.RequestUri.Query.Contains("page=chud") && Uri.UnescapeDataString(request.RequestUri.Query).Contains("prop=wikitext|text"), "pronunciation and forms share one word-specific official source request");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { parse = new { wikitext = wiki } })) });
        })))
        {
            var result = await phonetics.LookupAsync("chud", CancellationToken.None);
            Check(result?.Ipa.Single() == "/t͡ʃʌd/" && result.SourceUrl.EndsWith("#English"), "online IPA retains its source attribution");
        }
        using (var metadata = new Pronunciation(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new StringContent(JsonSerializer.Serialize(new { parse = new { text = "<p><span class=\"headword-line\"><strong class=\"headword\" lang=\"en\">mog</strong> (<i>present participle</i> <b class=\"form-of\" lang=\"en\">mogging</b>)</span></p>" } })) }))))
        {
            var result = await metadata.LookupAsync("mog", CancellationToken.None);
            Check(result is { Ipa.Length: 0, Forms.Length: 1 } && result.Forms[0].Forms[0].Words[0] == "mogging",
                "a word-form response remains usable even when the page has no explicit IPA template");
        }
        using (var invalidMetadata = new Pronunciation(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new StringContent("{\"parse\":{\"text\":{},\"wikitext\":null}}") }))))
            Check(await invalidMetadata.LookupAsync("word", CancellationToken.None) == null, "unsupported metadata shapes do not crash or replace existing forms");
        var original = new AiEntry("word", true, [new("noun", "a definition", "释义", "", "example", "例句", [])], "", "unverified", "/wɜːd/");
        var json = JsonSerializer.Serialize(original, Json.Flexible);
        Check(CodexDictionary.ParseAndValidate(json, "word", null).Phonetic == "/wɜːd/" && StreamingEntry.Read(json, "word")?.Phonetic == "/wɜːd/", "model IPA survives complete and streaming output");
        var old = JsonSerializer.Serialize(new { original.Word, original.Found, original.Senses, original.Note, original.EvidenceStatus }, Json.Flexible);
        Check(CodexDictionary.ParseAndValidate(old, "word", null) is { Found: true, Phonetic: "" }, "existing definitions remain readable without an IPA field");

        var openai = new SpeechOptions("openai", "gpt-4o-mini-tts", "coral");
        using (var request = SpeechClient.Request("US", openai, "synthetic"))
        {
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Check(request.RequestUri!.AbsoluteUri == "https://api.openai.com/v1/audio/speech" && payload.RootElement.GetProperty("input").GetString() == "US" &&
                payload.RootElement.GetProperty("voice").GetString() == "coral", "OpenAI speech request preserves input case and the selected model/voice");
        }
        using (var request = SpeechClient.Request("你好", new("xai", "grok-tts", "eve"), "synthetic"))
        {
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Check(request.RequestUri!.AbsoluteUri == "https://api.x.ai/v1/tts" && payload.RootElement.GetProperty("language").GetString() == "auto" &&
                payload.RootElement.GetProperty("voice_id").GetString() == "eve" && !payload.RootElement.TryGetProperty("model", out _), "Grok speech uses its documented endpoint and service-managed model");
        }
        Check(openai.CacheKey("US") != openai.CacheKey("us") && openai.CacheKey("word") != (openai with { Voice = "alloy" }).CacheKey("word") &&
            openai.CacheKey("word") != (openai with { Model = "tts-1" }).CacheKey("word"), "speech cache isolates case, model and voice");
        foreach (var options in new[] { openai with { Model = "unknown" }, openai with { Model = "tts-1", Voice = "marin" } })
        {
            try { SpeechCatalog.Validate("hello", options); Check(false, "invalid speech configuration"); }
            catch (InvalidOperationException) { Check(true, "unsupported model and voice combinations are rejected before any request"); }
        }
        try { SpeechCatalog.Validate(new string('x', 4097), openai); Check(false, "speech length bound"); }
        catch (InvalidOperationException) { Check(true, "speech rejects overlong input instead of silently truncating"); }
        try { using var request = SpeechClient.Request("hello", openai, ""); Check(false, "missing speech key"); }
        catch (InvalidOperationException) { Check(true, "missing API key produces an actionable local error"); }
        using (var client = new SpeechClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
            { Content = new StringContent("private-provider-response") }))))
        {
            try { await client.GenerateAsync("word", openai, "synthetic", CancellationToken.None); Check(false, "authentication failure"); }
            catch (InvalidOperationException e) { Check(!e.Message.Contains("private-provider-response"), "provider error bodies are never exposed as UI messages"); }
        }
        using (var client = new SpeechClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"error\":\"bad\"}") }))))
        {
            try { await client.GenerateAsync("word", openai, "synthetic", CancellationToken.None); Check(false, "invalid audio"); }
            catch (InvalidDataException) { Check(true, "non-audio responses cannot become cached speech"); }
        }
        var syntheticAudio = new byte[32]; Encoding.ASCII.GetBytes("ID3").CopyTo(syntheticAudio, 0);
        using (var client = new SpeechClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new ByteArrayContent(syntheticAudio) }))))
            Check((await client.GenerateAsync("word", openai, "synthetic", CancellationToken.None)).SequenceEqual(syntheticAudio),
                "successful speech responses preserve audio bytes for playback and caching");
        using (var cancel = new CancellationTokenSource())
        using (var client = new SpeechClient(new Handler(async (_, token) => { cancel.Cancel(); await Task.Delay(1000, token); return new(HttpStatusCode.OK); })))
        {
            try { await client.GenerateAsync("word", openai, "synthetic", cancel.Token); Check(false, "speech cancellation"); }
            catch (OperationCanceledException) { Check(true, "canceling speech cancels the network operation"); }
        }
        Check(new AppSettings().SpeechProvider == SpeechCatalog.Local.Provider && SpeechCatalog.Voices("openai", "gpt-4o-mini-tts").Length == 13,
            "speech defaults to local synthesis and exposes documented OpenAI voices");
        await ElevenSpeechChecks.Run();
        await SpeechAudioChecks.Run();
        Console.WriteLine($"{count} pronunciation and speech checks passed");
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken); }
}
