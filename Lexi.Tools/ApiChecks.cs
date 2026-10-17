using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lexi.Core;

// Model access with API keys (iOS), against a synthetic network: request shapes for the OpenAI
// Responses and Anthropic Messages APIs with the shipped instructions and schemas, validation and
// retry, error wording without keys or provider text, key handling, API-mode model rules, and a
// whole lookup routed through a session. Nothing reaches a real service.
internal static class ApiChecks
{
    private const string Key = "synthetic-api-key-0123456789";

    public static async Task Run(string fixture, string root)
    {
        int count = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); count++; }
        var work = Path.GetFullPath(root);
        if (Directory.Exists(work)) Directory.Delete(work, true);
        var assets = Path.Combine(work, "assets");
        Directory.CreateDirectory(assets);
        var core = Path.Combine(Repository(), "Lexi.Core");
        foreach (var file in new[] { "dictionary.instructions.txt", "dictionary.schema.json", "translation.schema.json", "sense-merge.instructions.txt", "sense-merge.schema.json" })
            File.Copy(Path.Combine(core, file), Path.Combine(assets, file));
        var schema = JsonNode.Parse(File.ReadAllText(Path.Combine(assets, "dictionary.schema.json")));
        var instructions = File.ReadAllText(Path.Combine(assets, "dictionary.instructions.txt"));

        var openai = new Network(); var api = new ApiDictionary(assets, openai);
        openai.Reply = _ => OpenAi(Entry("serendipity", "机缘巧合：偶然发现珍贵之物。"));
        var entry = await api.LookupAsync(new("openai", "gpt-6-luna"), "serendipity", null, Key, CancellationToken.None);
        var sent = openai.Requests.Single();
        var body = JsonNode.Parse(sent.Body)!;
        Check(sent.Uri == "https://api.openai.com/v1/responses" && sent.Authorization == "Bearer " + Key && !sent.Body.Contains(Key) &&
            (string?)body["model"] == "gpt-6-luna" && (string?)body["instructions"] == instructions && ((string?)body["input"])!.Contains("\"task\":\"define\"") &&
            (string?)body["text"]!["format"]!["type"] == "json_schema" && (bool?)body["text"]!["format"]!["strict"] == true &&
            JsonNode.DeepEquals(body["text"]!["format"]!["schema"], schema) && (bool?)body["store"] == false,
            "OpenAI requests use the Responses API with the shipped instructions and the dictionary schema as strict output");
        Check(entry.Found && entry.Senses[0].Chinese.StartsWith("机缘巧合") && entry.EvidenceStatus == "unverified", "OpenAI output text is validated like Codex output");

        var anthropic = new Network(); api = new ApiDictionary(assets, anthropic);
        anthropic.Reply = _ => Anthropic(Entry("serendipity", "意外发现的好运。"));
        entry = await api.LookupAsync(new("anthropic", "claude-haiku-5-5"), "serendipity", null, Key, CancellationToken.None);
        sent = anthropic.Requests.Single(); body = JsonNode.Parse(sent.Body)!;
        Check(sent.Uri == "https://api.anthropic.com/v1/messages" && sent.Headers["x-api-key"] == Key && sent.Headers["anthropic-version"] == "2023-06-01" &&
            sent.Authorization == "" && !sent.Body.Contains(Key) && (string?)body["system"] == instructions && (string?)body["tool_choice"]!["name"] == "respond" &&
            JsonNode.DeepEquals(body["tools"]![0]!["input_schema"], schema) && (string?)body["messages"]![0]!["role"] == "user",
            "Anthropic requests force one tool whose input is the dictionary schema");
        Check(entry.Senses[0].Chinese == "意外发现的好运。", "Anthropic tool input is validated like Claude Code output");

        var retry = new Network(); api = new ApiDictionary(assets, retry);
        retry.Reply = n => OpenAi(Entry(n == 1 ? "serendip" : "serendipity", "机缘巧合。"));
        entry = await api.LookupAsync(new("openai", "gpt-6-luna"), "serendipity", null, Key, CancellationToken.None);
        Check(retry.Requests.Count == 2 && JsonNode.Parse(retry.Requests[1].Body)!["input"]!.ToString().Contains("failed structural validation") && entry.Word == "serendipity",
            "a reply for the wrong word is retried once with a validation note");

        async Task<string> Failure(Func<int, HttpResponseMessage> reply, string provider = "openai")
        {
            var network = new Network { Reply = reply };
            try { await new ApiDictionary(assets, network).LookupAsync(new(provider, "x"), "serendipity", null, Key, CancellationToken.None); return ""; }
            catch (Exception e) when (e is InvalidOperationException or IOException) { return e.Message; }
        }
        var unauthorized = await Failure(_ => new(HttpStatusCode.Unauthorized) { Content = new StringContent("{\"error\":{\"message\":\"secret-detail " + Key + "\"}}") });
        Check(unauthorized == "OpenAI API Key 无效或没有权限，请在设置 → 模型中检查。", "a rejected key is reported without the provider's message or the key");
        Check(await Failure(_ => new(HttpStatusCode.NotFound), "anthropic") == "Anthropic 找不到所选模型，请在设置 → 模型中重新选择。" &&
            (await Failure(_ => new(HttpStatusCode.TooManyRequests))).Contains("额度") &&
            await Failure(_ => throw new HttpRequestException("socket detail")) == "无法连接 OpenAI，请检查网络后重试。",
            "missing models, rate limits and network failures have their own wording");

        var keys = new ApiKeys();
        string Missing(string provider) { try { keys.For(provider); return ""; } catch (InvalidOperationException e) { return e.Message; } }
        keys.Set("openai", "  " + Key + "  ");
        Check(keys.For("openai") == Key && Missing("anthropic") == "请在设置 → 模型中填写 Anthropic API Key。", "keys are trimmed and a missing key names its service");
        keys.Set("openai", "");
        Check(!keys.Has("openai") && !ApiKeys.Supports("codex"), "an empty key removes it; only API services take keys");

        var desktop = new AppSettings();
        var apiSettings = new AppSettings(); ModelCatalog.UseApiDefaults(apiSettings);
        Check(ModelCatalog.Choices(apiSettings, true).All(c => c.Provider is "openai" or "anthropic") && ModelCatalog.Choices(desktop, false).All(c => c.Provider is "codex" or "claude"),
            "API platforms offer only API models and desktop keeps the subscription clients");
        Check(apiSettings is { Provider: "openai", Model: "gpt-6-luna" } && LookupModel.Secondary(apiSettings) == new LookupModel("anthropic", "claude-haiku-5-5") &&
            ModelCatalog.Validate(apiSettings, true) == null && ModelCatalog.Validate(desktop, true) == "请选择受支持的模型服务。" &&
            ModelCatalog.Validate(apiSettings, false) == "请选择受支持的模型服务。",
            "API defaults pair an OpenAI model with an Anthropic second opinion, and each mode refuses the other's services");
        apiSettings.JudgeModel = "claude-haiku-5-5"; apiSettings.Model = "bad model";
        Check(ModelCatalog.Judge(apiSettings, true) == new LookupModel("anthropic", "claude-haiku-5-5") && ModelCatalog.Judge(desktop, false).Provider == "codex" &&
            ModelCatalog.Validate(apiSettings, true) == "请填写有效的模型 ID。" && ModelCatalog.JudgeChoices(ModelCatalog.Choices(desktop, false)).All(c => c.Provider == "codex"),
            "the judge follows its model's service and model IDs are checked");

        // A whole lookup through the session: each model goes to its own API with its own key.
        var user = Path.Combine(work, "session");
        Directory.CreateDirectory(user);
        File.WriteAllText(Path.Combine(user, "settings.json"), "{\"provider\":\"openai\",\"model\":\"gpt-6-luna\",\"secondaryProvider\":\"anthropic\",\"secondaryModel\":\"claude-haiku-5-5\",\"mergeSenses\":false}");
        var services = new Network();
        services.Reply = _ => services.Requests[^1].Uri switch
        {
            "https://api.openai.com/v1/responses" => OpenAi(Entry("serendipity", "机缘巧合：偶然发现珍贵之物。")),
            "https://api.anthropic.com/v1/messages" => Anthropic(Entry("serendipity", "意外之喜：意外遇到的好事。")),
            _ => new(HttpStatusCode.NotFound)
        };
        using (var session = new QuerySession(new(assets, Path.GetFullPath(fixture), user), network: services))
        {
            session.Keys.Set("openai", Key); session.Keys.Set("anthropic", Key + "-anthropic");
            await session.InitializeAsync();
            await session.SubmitAsync("serendipity");
            var senses = session.View.Answers?.Senses.Select(s => s.Chinese).ToArray() ?? [];
            Check(senses.Any(s => s.StartsWith("机缘巧合")) && senses.Any(s => s.StartsWith("意外之喜")) &&
                services.Requests.Single(r => r.Uri.Contains("openai")).Authorization == "Bearer " + Key &&
                services.Requests.Single(r => r.Uri.Contains("anthropic")).Headers["x-api-key"] == Key + "-anthropic",
                "a session sends each model to its own API with its own key and shows both answers");
            session.Keys.Set("anthropic", "");
            await session.SubmitAsync("serendipitous", force: true);
            var notices = session.View.Answers?.Notices.Select(n => n.Text).ToArray() ?? [];
            Check(notices.Any(n => n.Contains("Anthropic API Key")) && !string.Join("\n", notices).Contains(Key),
                "a missing key fails only that model, with a notice naming the key to add");
        }
        Console.WriteLine($"{count} API checks passed without contacting any service");
    }

    private static string Repository()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder != null; folder = folder.Parent)
            if (File.Exists(Path.Combine(folder.FullName, "Lexi.Core", "dictionary.schema.json"))) return folder.FullName;
        return Directory.GetCurrentDirectory();
    }

    private static string Entry(string word, string chinese) => new JsonObject
    {
        ["word"] = word, ["found"] = true, ["note"] = "", ["evidenceStatus"] = "unverified", ["phonetic"] = "", ["wordForms"] = new JsonArray(), ["etymology"] = "",
        ["senses"] = new JsonArray(new JsonObject
        {
            ["partOfSpeech"] = "noun", ["english"] = "Finding something good without looking for it.", ["chinese"] = chinese, ["register"] = "general",
            ["example"] = "Meeting her was pure serendipity.", ["exampleChinese"] = "遇见她纯属机缘巧合。", ["evidence"] = new JsonArray(),
            ["lexicalForm"] = "word", ["expansion"] = "", ["gloss"] = chinese.Split('：')[0].TrimEnd('。'), ["coreMeaning"] = true, ["chineseTerm"] = chinese.Split('：')[0].TrimEnd('。')
        })
    }.ToJsonString();

    private static HttpResponseMessage OpenAi(string text) => Json(new JsonObject
    {
        ["output"] = new JsonArray(new JsonObject { ["type"] = "message", ["content"] = new JsonArray(new JsonObject { ["type"] = "output_text", ["text"] = text }) })
    });
    private static HttpResponseMessage Anthropic(string input) => Json(new JsonObject
    {
        ["content"] = new JsonArray(new JsonObject { ["type"] = "tool_use", ["name"] = "respond", ["input"] = JsonNode.Parse(input) })
    });
    private static HttpResponseMessage Json(JsonObject body) => new(HttpStatusCode.OK) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };

    private sealed record Sent(string Uri, string Authorization, Dictionary<string, string> Headers, string Body);
    private sealed class Network : HttpMessageHandler
    {
        public List<Sent> Requests { get; } = [];
        public Func<int, HttpResponseMessage> Reply { get; set; } = _ => new(HttpStatusCode.NotFound);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(token);
            lock (Requests)
                Requests.Add(new(request.RequestUri!.ToString(), request.Headers.Authorization?.ToString() ?? "",
                    request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value)), body));
            return Reply(Requests.Count);
        }
    }
}
