using System.Text.Json.Serialization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Lexi.Core;

public sealed record SpeechModel(string Provider, string Id, string Name)
{ public override string ToString() => Name; }
public sealed record SpeechVoice(string Id, string Name)
{ public override string ToString() => Name; }
public sealed record SpeechOptions(string Provider, string Model, string Voice)
{
    public string CacheKey(string text) => Cache.Hash(JsonSerializer.Serialize(new SpeechCacheKey(1, text, Provider, Model, Voice), Json.Options));
    public static SpeechOptions From(AppSettings settings) => new(settings.SpeechProvider, settings.SpeechModel, settings.SpeechVoice);
}

public static class SpeechCatalog
{
    // Local synthesis belongs to each front end: Windows SAPI voices, or macOS and iOS system voices.
    public static SpeechModel Local { get; } = OperatingSystem.IsIOS() ? new("ios", "avspeech", "iOS 本地发音")
        : OperatingSystem.IsMacOS() ? new("macos", "avspeech", "macOS 本地发音") : new("windows", "sapi", "Windows 本地发音");
    public static bool IsLocal(string provider) => provider == Local.Provider;
    public static SpeechModel[] Models => [Local, new("openai", "gpt-4o-mini-tts", "OpenAI · GPT-4o mini TTS"),
        new("openai", "tts-1", "OpenAI · TTS-1"), new("openai", "tts-1-hd", "OpenAI · TTS-1 HD"), new("xai", "grok-tts", "Grok · TTS"),
        new("elevenlabs", "eleven_flash_v2_5", "ElevenLabs · Flash v2.5 · 快速"),
        new("elevenlabs", "eleven_multilingual_v2", "ElevenLabs · Multilingual v2"),
        new("elevenlabs", "eleven_v3", "ElevenLabs · Eleven v3"),
        new("elevenlabs", "eleven_v4", "ElevenLabs · Eleven v4"),
        new("elevenlabs", "eleven_v4_turbo", "ElevenLabs · Eleven v4 Turbo")];
    // Online catalog voices. Local voices come from the operating system.
    public static SpeechVoice[] Voices(string provider, string model)
    {
        if (provider == "elevenlabs") return [new("JBFqnCBsd6RMkjVDRZzb", "George")];
        if (provider is not ("openai" or "xai")) return [];
        var ids = provider == "xai" ? new[] { "eve", "ara", "leo", "rex", "sal" } :
            model == "gpt-4o-mini-tts" ? ["coral", "alloy", "ash", "ballad", "echo", "fable", "nova", "onyx", "sage", "shimmer", "verse", "marin", "cedar"] :
            new[] { "alloy", "ash", "coral", "echo", "fable", "onyx", "nova", "sage", "shimmer" };
        return ids.Select(id => new SpeechVoice(id, char.ToUpperInvariant(id[0]) + id[1..])).ToArray();
    }
    public static bool ValidElevenVoice(string id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 128 &&
        id.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
    public static void Validate(string text, SpeechOptions options)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("没有可朗读的文字。");
        var max = options.Provider == "openai" ? 4096 : 5000;
        if (text.Length > max) throw new InvalidOperationException($"此发音服务每次最多朗读 {max} 个字符。");
        if (!Models.Any(m => m.Provider == options.Provider && m.Id == options.Model)) throw new InvalidOperationException("请在设置中重新选择发声模型。");
        if (options.Provider == "elevenlabs" && !ValidElevenVoice(options.Voice))
            throw new InvalidOperationException("请选择 ElevenLabs 声线，或填写有效的 Voice ID。");
        if (!IsLocal(options.Provider) && options.Provider != "elevenlabs" && !Voices(options.Provider, options.Model).Any(v => v.Id == options.Voice))
            throw new InvalidOperationException("请在设置中选择这个模型支持的声线。");
    }
    // Local synthesis saved on the other platform stays local here; its voice IDs belong to that system.
    public static void AdoptLocal(AppSettings settings)
    {
        if (settings.SpeechProvider is not ("windows" or "macos" or "ios") || IsLocal(settings.SpeechProvider)) return;
        settings.SpeechProvider = Local.Provider; settings.SpeechModel = Local.Id; settings.SpeechVoice = "auto"; settings.SpeechVoiceName = "";
    }
}

public sealed partial class SpeechClient : IDisposable
{
    private readonly HttpClient client;
    public SpeechClient(HttpMessageHandler? handler = null)
    { client = new(handler ?? new HttpClientHandler { AllowAutoRedirect = false }); client.Timeout = TimeSpan.FromSeconds(35); }
    public static HttpRequestMessage Request(string text, SpeechOptions options, string apiKey)
    {
        SpeechCatalog.Validate(text, options);
        if (options.Provider is not ("openai" or "xai" or "elevenlabs")) throw new InvalidOperationException("此模型不使用在线语音接口。");
        ValidateKey(apiKey);
        if (options.Provider == "elevenlabs") return ElevenRequest(text, options, apiKey);
        object body = options.Provider == "openai" ? new OpenAiSpeechBody(options.Model, options.Voice, text, "mp3") :
            new XaiSpeechBody(text, options.Voice, "auto", new XaiAudioFormat("mp3", 24000, 128000));
        var request = new HttpRequestMessage(HttpMethod.Post, options.Provider == "openai" ? "https://api.openai.com/v1/audio/speech" : "https://api.x.ai/v1/tts");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        request.Content = new StringContent(JsonSerializer.Serialize(body, Json.Options), Encoding.UTF8, "application/json");
        return request;
    }
    private static void ValidateKey(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) throw new InvalidOperationException("请在设置 → 发音的在线语音连接中填写所选服务的 API Key。");
        if (apiKey.Trim().Any(c => char.IsWhiteSpace(c) || char.IsControl(c)) || apiKey.Length > 2048)
            throw new InvalidOperationException("API Key 格式无效，请检查是否包含换行或空格。");
    }
    public async Task<byte[]> GenerateAsync(string text, SpeechOptions options, string apiKey, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(35));
        try { return await ReceiveAsync(text, options, apiKey, timeout.Token); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException("语音生成超时，请重试。"); }
        // Transport messages are platform-localized and may name hosts; every front end shows the same wording.
        catch (Exception e) when (e is HttpRequestException or IOException)
        { throw new InvalidOperationException(options.Provider == "elevenlabs" ? ElevenUnreachable : "无法连接语音服务，请检查网络后重试。", e); }
    }
    private async Task<byte[]> ReceiveAsync(string text, SpeechOptions options, string apiKey, CancellationToken token)
    {
        using var request = Request(text, options, apiKey);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (!response.IsSuccessStatusCode && options.Provider == "elevenlabs") throw await ElevenFailureAsync(response, token);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "语音服务拒绝认证，请检查 API Key 和接口权限。",
                HttpStatusCode.TooManyRequests => "语音服务额度不足或请求过多，请稍后重试。",
                _ => $"语音服务暂不可用（HTTP {(int)response.StatusCode}）。"
            });
        const int limit = 8 * 1024 * 1024;
        if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("语音文件过大，已停止接收。");
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var output = new MemoryStream(); var buffer = new byte[16384];
        int read;
        while ((read = await stream.ReadAsync(buffer, token)) > 0)
        { if (output.Length + read > limit) throw new InvalidDataException("语音文件过大，已停止接收。"); await output.WriteAsync(buffer.AsMemory(0, read), token); }
        var bytes = output.ToArray();
        if (!IsMp3(bytes)) throw new InvalidDataException("语音服务没有返回有效的 MP3 音频。");
        return bytes;
    }
    public static bool IsMp3(byte[] bytes) => bytes.Length > 16 &&
        (bytes[0] == 'I' && bytes[1] == 'D' && bytes[2] == '3' || bytes[0] == 0xff && (bytes[1] & 0xe0) == 0xe0);
    public void Dispose() => client.Dispose();
}

internal sealed record SpeechCacheKey(int Version, string Text, string Provider, string Model, string Voice);
internal sealed record OpenAiSpeechBody(string Model, string Voice, string Input, [property: JsonPropertyName("response_format")] string ResponseFormat);
internal sealed record XaiSpeechBody(string Text, [property: JsonPropertyName("voice_id")] string VoiceId, string Language,
    [property: JsonPropertyName("output_format")] XaiAudioFormat OutputFormat);
internal sealed record XaiAudioFormat(string Codec, [property: JsonPropertyName("sample_rate")] int SampleRate, [property: JsonPropertyName("bit_rate")] int BitRate);
