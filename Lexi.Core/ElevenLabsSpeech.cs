using System.Text.Json.Serialization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Lexi.Core;

public sealed partial class SpeechClient
{
    private const string ElevenUnreachable = "无法连接 ElevenLabs，请检查网络后重试。";
    // Official speech and voice-list endpoints; keys never appear in URLs.
    private static HttpRequestMessage ElevenRequest(string text, SpeechOptions options, string apiKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post,
            "https://api.elevenlabs.io/v1/text-to-speech/" + Uri.EscapeDataString(options.Voice) + "?output_format=mp3_44100_128");
        request.Headers.Add("xi-api-key", apiKey.Trim());
        request.Content = new StringContent(JsonSerializer.Serialize(new ElevenSpeechBody(text, options.Model), Json.Options), Encoding.UTF8, "application/json");
        return request;
    }

    public async Task<SpeechVoice[]> ElevenVoicesAsync(string apiKey, CancellationToken token)
    {
        ValidateKey(apiKey);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        try { return await ReadElevenVoicesAsync(apiKey.Trim(), timeout.Token); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new TimeoutException("读取 ElevenLabs 声线超时，请重试。"); }
        catch (JsonException) { throw new InvalidDataException("ElevenLabs 声线列表格式不完整，请重试。"); }
        catch (Exception e) when (e is HttpRequestException or IOException) { throw new InvalidOperationException(ElevenUnreachable, e); }
    }

    private async Task<SpeechVoice[]> ReadElevenVoicesAsync(string apiKey, CancellationToken token)
    {
        var voices = new Dictionary<string, SpeechVoice>(StringComparer.Ordinal);
        var pages = new HashSet<string>(StringComparer.Ordinal);
        string? next = null;
        for (var page = 0; page < 10; page++)
        {
            var url = "https://api.elevenlabs.io/v2/voices?page_size=100" +
                (next == null ? "" : "&next_page_token=" + Uri.EscapeDataString(next));
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("xi-api-key", apiKey);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode) throw await ElevenFailureAsync(response, token);
            var bytes = await ReadMetadataAsync(response.Content, 2 * 1024 * 1024, token);
            using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("voices", out var items) || items.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("ElevenLabs 没有返回可用的声线列表。");
            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var id = ReadString(item, "voice_id");
                var name = ReadString(item, "name");
                if (!SpeechCatalog.ValidElevenVoice(id) || name.Length is 0 or > 120 || name.Any(char.IsControl)) continue;
                voices.TryAdd(id, new(id, name));
                if (voices.Count > 2000) throw new InvalidDataException("声线列表过长，请在设置中手动填写 Voice ID。");
            }
            if (!root.TryGetProperty("has_more", out var more) || more.ValueKind == JsonValueKind.False)
                return voices.Values.OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            next = ReadString(root, "next_page_token");
            if (more.ValueKind != JsonValueKind.True || next.Length is 0 or > 4096 || !pages.Add(next))
                throw new InvalidDataException("ElevenLabs 声线分页不完整，请重试。");
        }
        throw new InvalidDataException("声线列表过长，请在设置中手动填写 Voice ID。");
    }

    private static string ReadString(JsonElement item, string property) =>
        item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static async Task<byte[]> ReadMetadataAsync(HttpContent content, int limit, CancellationToken token)
    {
        if (content.Headers.ContentLength > limit) throw new InvalidDataException("ElevenLabs 返回的数据过大，已停止读取。");
        await using var stream = await content.ReadAsStreamAsync(token);
        using var output = new MemoryStream();
        var buffer = new byte[8192]; int read;
        while ((read = await stream.ReadAsync(buffer, token)) > 0)
        {
            if (output.Length + read > limit) throw new InvalidDataException("ElevenLabs 返回的数据过大，已停止读取。");
            await output.WriteAsync(buffer.AsMemory(0, read), token);
        }
        return output.ToArray();
    }

    private static async Task<InvalidOperationException> ElevenFailureAsync(HttpResponseMessage response, CancellationToken token)
    {
        // Only classify known service codes. Raw response text may contain private details.
        string code = "";
        try
        {
            var bytes = await ReadMetadataAsync(response.Content, 32768, token);
            using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
            if (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.Object)
            {
                code = ReadString(detail, "status");
                if (code.Length == 0) code = ReadString(detail, "code");
            }
        }
        catch (Exception e) when (e is JsonException or InvalidDataException) { }
        var message = code switch
        {
            "api_key_id_used_as_api_key" => "填入的是 API Key ID。请在 ElevenLabs 创建密钥时复制完整的 Secret Key，替换此处内容后重试。",
            "invalid_api_key" => "ElevenLabs API Key 无效，请复制完整密钥后重试。",
            "model_not_supported" or "model_not_available" => "此 ElevenLabs 模型当前不可用，请选择账号支持的模型后重试。",
            "quota_exceeded" or "insufficient_credits" => "ElevenLabs 账号额度不足，请检查订阅中的可用额度。",
            "voice_not_found" or "voice_does_not_exist" => "此声线不可用，请重新加载声线或检查 Voice ID。",
            "model_not_found" or "invalid_model_id" => "此 ElevenLabs 模型暂不可用，请在设置中选择账号可用的模型。",
            "missing_permissions" => "ElevenLabs 密钥权限不足，请为它开启文字转语音和读取声线权限。",
            _ => response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "ElevenLabs 连接未通过，请检查 API Key、接口权限和账号访问资格。",
                HttpStatusCode.NotFound => "ElevenLabs 模型或声线不可用，请检查模型和 Voice ID。",
                HttpStatusCode.TooManyRequests => "ElevenLabs 请求过多或额度不足，请稍后重试。",
                HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => "ElevenLabs 拒绝了请求，请检查完整的 API Key、模型和声线是否可用（HTTP " + (int)response.StatusCode + "）。",
                _ => $"ElevenLabs 暂不可用（HTTP {(int)response.StatusCode}）。"
            }
        };
        return new InvalidOperationException(message);
    }
}

internal sealed record ElevenSpeechBody(string Text, [property: JsonPropertyName("model_id")] string ModelId);
