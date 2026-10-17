using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lexi.Core;

// API keys for model access, held only in memory for this session. The front end supplies them
// from its secure storage; Core never writes, logs or returns them.
public sealed class ApiKeys
{
    private readonly ConcurrentDictionary<string, string> keys = new(StringComparer.Ordinal);
    public static bool Supports(string provider) => provider is "openai" or "anthropic";
    public void Set(string provider, string key)
    {
        if (!Supports(provider)) throw new InvalidOperationException("请选择受支持的模型服务。");
        if (string.IsNullOrWhiteSpace(key)) keys.TryRemove(provider, out _); else keys[provider] = key.Trim();
    }
    public bool Has(string provider) => keys.ContainsKey(provider);
    public string For(string provider) => keys.TryGetValue(provider, out var key) ? key :
        throw new InvalidOperationException($"请在设置 → 模型中填写 {ApiDictionary.ServiceName(provider)} API Key。");
}

// Model access with the user's own API keys, for platforms without the official subscription
// clients (iOS). Requests carry the same instructions, JSON schemas and validation as the Codex and
// Claude Code paths, so results, caches and merging behave the same. Only definitions come from the
// model; nothing here starts a process.
public sealed class ApiDictionary(string assetFolder, HttpMessageHandler? handler = null)
{
    private readonly HttpClient http = new(handler ?? new SocketsHttpHandler(), handler == null) { Timeout = Timeout.InfiniteTimeSpan };

    public static string ServiceName(string provider) => provider == "anthropic" ? "Anthropic" : "OpenAI";

    public async Task<AiEntry> LookupAsync(LookupModel model, string query, OnlineEntry? evidence, string key, CancellationToken token)
    {
        var request = CodexDictionary.DefineRequest(query, evidence);
        return await Retry(async attempt => CodexDictionary.ParseAndValidate(
            await CallAsync(model, "dictionary.instructions.txt", "dictionary.schema.json", request + RetryNote(attempt), key, 70, 8192, token), query, evidence, true));
    }

    public async Task<TranslationEntry> TranslateAsync(LookupModel model, string text, string target, string key, CancellationToken token)
    {
        var request = Translation.Request(text, target);
        return await Retry(async attempt => Translation.Parse(
            await CallAsync(model, "dictionary.instructions.txt", "translation.schema.json", request + RetryNote(attempt), key, 120, 16384, token), text, target));
    }

    public async Task<SenseMergePlan> JudgeSensesAsync(LookupModel model, SenseMergeRequest request, string key, CancellationToken token) =>
        SenseMerging.Parse(await CallAsync(model, "sense-merge.instructions.txt", "sense-merge.schema.json", request.Prompt, key, 25, 4096, token), request);

    // One more attempt when the reply fails structural validation, as the Claude Code path does.
    private static async Task<T> Retry<T>(Func<int, Task<T>> run)
    {
        try { return await run(0); }
        catch (Exception e) when (e is InvalidDataException or JsonException) { return await run(1); }
    }
    private static string RetryNote(int attempt) => attempt == 0 ? "" :
        "\nThe previous response failed structural validation. Regenerate one complete valid JSON object for the exact input. No commentary.";

    private async Task<string> CallAsync(LookupModel model, string instructionsFile, string schemaFile, string prompt, string key,
        int timeoutSeconds, int maxTokens, CancellationToken token)
    {
        var instructions = await File.ReadAllTextAsync(Path.Combine(assetFolder, instructionsFile), token);
        var schema = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(assetFolder, schemaFile), token))!;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        using var request = model.Provider == "anthropic"
            ? AnthropicRequest(model.Model, instructions, schema, prompt, key, maxTokens)
            : OpenAiRequest(model.Model, instructions, schema, Path.GetFileNameWithoutExtension(schemaFile), prompt, key);
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException(Failure(model.Provider, response.StatusCode, body));
            return model.Provider == "anthropic" ? AnthropicOutput(body) : OpenAiOutput(body);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new TimeoutException($"{ServiceName(model.Provider)} 响应超时，请稍后重试。"); }
        catch (HttpRequestException)
        { throw new IOException($"无法连接 {ServiceName(model.Provider)}，请检查网络后重试。"); }
    }

    // OpenAI Responses API with the schema as strict structured output, as Codex uses it.
    private static HttpRequestMessage OpenAiRequest(string model, string instructions, JsonNode schema, string name, string prompt, string key)
    {
        var body = new JsonObject
        {
            ["model"] = model, ["instructions"] = instructions, ["input"] = prompt, ["store"] = false,
            ["text"] = new JsonObject { ["format"] = new JsonObject { ["type"] = "json_schema", ["name"] = name.Replace('.', '_').Replace('-', '_'), ["schema"] = schema, ["strict"] = true } }
        };
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses") { Content = Json(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return request;
    }

    // Anthropic Messages API: the schema is the input of one tool the model must call.
    private static HttpRequestMessage AnthropicRequest(string model, string instructions, JsonNode schema, string prompt, string key, int maxTokens)
    {
        var body = new JsonObject
        {
            ["model"] = model, ["max_tokens"] = maxTokens, ["system"] = instructions,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = prompt }),
            ["tools"] = new JsonArray(new JsonObject { ["name"] = "respond", ["description"] = "Return the requested JSON object.", ["input_schema"] = schema }),
            ["tool_choice"] = new JsonObject { ["type"] = "tool", ["name"] = "respond" }
        };
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages") { Content = Json(body) };
        request.Headers.Add("x-api-key", key);
        request.Headers.Add("anthropic-version", "2023-06-01");
        return request;
    }

    private static StringContent Json(JsonObject body) => new(body.ToJsonString(), Encoding.UTF8, "application/json");

    public static string OpenAiOutput(string body)
    {
        using var document = JsonDocument.Parse(body);
        var text = new StringBuilder();
        if (document.RootElement.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
            foreach (var item in output.EnumerateArray())
                if (item.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                    foreach (var part in content.EnumerateArray())
                    {
                        var type = part.TryGetProperty("type", out var kind) && kind.ValueKind == JsonValueKind.String ? kind.GetString() : "";
                        if (type == "refusal") throw new InvalidDataException("模型拒绝了这次请求。");
                        if (type == "output_text" && part.TryGetProperty("text", out var value)) text.Append(value.GetString());
                    }
        return text.Length > 0 ? text.ToString() : throw new InvalidDataException("模型未返回释义。");
    }

    public static string AnthropicOutput(string body)
    {
        using var document = JsonDocument.Parse(body);
        if (document.RootElement.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            foreach (var part in content.EnumerateArray())
                if (part.TryGetProperty("type", out var type) && type.GetString() == "tool_use" && part.TryGetProperty("input", out var input))
                    return input.GetRawText();
        throw new InvalidDataException("模型未返回释义。");
    }

    // Only an application-defined summary: provider messages can echo request details.
    public static string Failure(string provider, HttpStatusCode status, string body)
    {
        var service = ServiceName(provider);
        return (int)status switch
        {
            401 or 403 => $"{service} API Key 无效或没有权限，请在设置 → 模型中检查。",
            404 => $"{service} 找不到所选模型，请在设置 → 模型中重新选择。",
            429 => $"{service} 请求过于频繁或额度不足，请稍后重试或检查账户额度。",
            400 when body.Contains("model", StringComparison.OrdinalIgnoreCase) => $"{service} 不接受所选模型，请在设置 → 模型中重新选择。",
            >= 500 => $"{service} 暂时不可用，请稍后重试。",
            _ => $"{service} 未完成查询（HTTP {(int)status}）。"
        };
    }
}
