using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Lexi.Core;

public sealed class CodexDictionary(string assetFolder, string userRoot)
{
    public static string EvidenceText(OnlineEntry? online) => online == null ? "No external evidence. Use reliable knowledge and mark it unverified." :
        string.Join("\n", online.Senses.Take(20).Select((s, i) => $"[{i + 1}] {s.SourceName} [{s.Headword}] {s.PartOfSpeech}: {s.Definition}"));
    public static string EvidenceHash(OnlineEntry? online) => Cache.Hash("bilingual-v4-specialist-case\n" + EvidenceText(online));
    public static string LegacyEvidenceHash(OnlineEntry? online) => Cache.Hash("bilingual-v3\n" + (online == null ?
        "No external evidence. Use reliable knowledge and mark it unverified." : string.Join("\n", online.Senses.Take(20).Select((s,i) => $"[{i+1}] {s.SourceName} {s.PartOfSpeech}: {s.Definition}"))));
    public static string DefineRequest(string query, OnlineEntry? evidence)
    {
        var terminologyOnly = evidence?.Senses is { Length: > 0 } && evidence.Senses.All(s => s.PartOfSpeech.StartsWith("terminology pair"));
        return "Define the complete English or Chinese term. Use established specialist meanings when appropriate. " +
            (terminologyOnly ? "The evidence contains terminology equivalents, not explanatory definitions. Give the dominant technical meaning in ONE sense. Do not invent a functional distinction between synonymous names. Mark explanatory details unverified. " : "") + "\n" +
            JsonSerializer.Serialize(new DefinePrompt
            {
                Task = "define", Query = query, MaxSenses = terminologyOnly ? 1 : 6,
                InputCase = Queries.AbbreviationCandidate(query) ? "uppercase: check established abbreviations and give their full expansions before lowercase homographs" : "preserve the query spelling",
                CoverageInterests = new[] { "cryptoassets and blockchain", "cryptography", "information security and confidential computing" },
                CoverageEvidence = evidence?.Senses.Select((s, i) => new { sense = s, number = i + 1 })
                    .Where(s => LexicalCoverage.Specialist(s.sense.PartOfSpeech + " " + s.sense.Definition) || SenseMerging.IsCurrent(s.sense.PartOfSpeech + " " + s.sense.Definition)).Select(s => s.number).ToArray() ?? [],
                Coverage = "Define the established core meaning first, in its natural domain. Check coverageInterests for additional independently established meanings, even when general dictionaries omit them. These interests are a recall checklist, not the context of the query or a ranking instruction. Being used in an industry is not a new lexical meaning: keep such applications as examples under the general definition. Do not narrow general software, computing, scientific or everyday concepts to blockchain/security, or copy interest labels onto unrelated meanings. Preserve established specialist meanings when their referent or defining mechanism differs. Distinguish slang, literal and proper-name senses; preserve common meanings rather than spending the limit on obscure names or language families. Case-insensitive input may still refer to a capitalized meme name. Do not invent a slang meaning when none is known.",
                Overview = "Mark coreMeaning independently of coverage order: identify the central meaning of the whole term, normally one, at most two. Industry application examples cannot replace the standalone definition. Coverage interests ensure recall; they must not displace the word's core concept with a peripheral person or specialized reuse.",
                Translation = "Keep the established Chinese name in chineseTerm, at the start of chinese, and at the start of the short gloss. The translation takes priority over a descriptive paraphrase; never invent an unfamiliar name.",
                EtymologyEvidence = evidence?.Etymologies ?? [],
                Evidence = EvidenceText(evidence)
            }, Json.Options);
    }
    public static AiEntry ParseAndValidate(string text, string query, OnlineEntry? online, bool requireWordForms = false)
    {
        var entry = JsonSerializer.Deserialize<AiEntry>(text, Json.Options) ?? throw new InvalidDataException("Codex 未返回释义。");
        if (string.IsNullOrWhiteSpace(entry.Word) || Queries.Normalize(entry.Word) != Queries.Normalize(query) || Queries.AbbreviationCandidate(query) && Queries.LookupKey(entry.Word) != query || entry.Senses == null)
            throw new InvalidDataException("Codex 返回了不匹配的词条。");
        if (requireWordForms)
        {
            using var document = JsonDocument.Parse(text);
            if (!document.RootElement.TryGetProperty("etymology", out var origin) || origin.ValueKind != JsonValueKind.String ||
                !document.RootElement.TryGetProperty("senses", out var meanings) || meanings.EnumerateArray().Any(s =>
                    !s.TryGetProperty("lexicalForm", out _) || !s.TryGetProperty("expansion", out _) ||
                    !s.TryGetProperty("chineseTerm", out var term) || term.ValueKind != JsonValueKind.String))
                throw new InvalidDataException("模型未返回完整的词条信息，请重新生成。");
        }
        if (requireWordForms && entry.WordForms == null) throw new InvalidDataException("模型未返回词形信息，请重新生成。");
        if (!entry.Found || entry.Senses.Length == 0) return entry with { Found = false, Senses = [], EvidenceStatus = "unknown", WordForms = [] };
        if (entry.Senses.Length > 8) throw new InvalidDataException("Codex 返回的词条过长。");
        foreach (var s in entry.Senses)
        {
            if (s == null || string.IsNullOrWhiteSpace(s.English) || string.IsNullOrWhiteSpace(s.Chinese) ||
                s.PartOfSpeech == null || s.Register == null || s.Example == null || s.ExampleChinese == null || s.Evidence == null ||
                s.LexicalForm is not ("word" or "abbreviation") || s.Expansion == null || s.Expansion.Length > 200 || s.Expansion.Any(char.IsControl) ||
                s.Evidence.Any(n => n < 1 || n > (online?.Senses.Length ?? 0)))
                throw new InvalidDataException("Codex 返回的释义或来源编号无效。");
        }
        // Source-based is a provenance label, never a claim that AI correctness was verified.
        var supported = online != null && entry.EvidenceStatus == "source_based" && entry.Senses.All(s =>
            s.Evidence.Any(n => !online.Senses[n - 1].PartOfSpeech.StartsWith("terminology pair")));
        return entry with { EvidenceStatus = supported ? "source_based" : "unverified", Senses = PrioritizeCurrent(entry.Senses.Select(s => s with { Gloss = QuickRead.ValidGloss(s.Gloss), ChineseTerm = QuickRead.ValidGloss(s.ChineseTerm) })),
            Phonetic = Pronunciation.Format(entry.Phonetic), WordForms = ModelInflections.Sanitize(query, entry),
            Etymology = (entry.Etymology ?? "").Length <= 600 && Queries.HasChinese(entry.Etymology ?? "") ? entry.Etymology ?? "" : "" };
    }
    public static AiSense[] PrioritizeCurrent(IEnumerable<AiSense> senses) => senses.OrderBy(s =>
        s.CoreMeaning == true ? 0 : 1).ToArray();
    public async Task<AiEntry> LookupAsync(string query, OnlineEntry? online, AppSettings settings, CancellationToken token)
    {
        var result = await RunAsync(DefineRequest(query, online), "dictionary.schema.json", settings, token);
        return ParseAndValidate(result, query, online, true);
    }
    public async Task<TranslationEntry> TranslateAsync(string text, string target, AppSettings settings, CancellationToken token)
    {
        var result = await RunAsync(Translation.Request(text, target), "translation.schema.json", settings, token);
        return Translation.Parse(result, text, target);
    }
    public async Task<SenseMergePlan> JudgeSensesAsync(SenseMergeRequest request, AppSettings settings, CancellationToken token)
    {
        var result = await RunAsync(request.Prompt, "sense-merge.schema.json", settings, token, "sense-merge.instructions.txt", 25);
        return SenseMerging.Parse(result, request);
    }
    private async Task<string> RunAsync(string prompt, string schemaFile, AppSettings settings, CancellationToken token,
        string instructionsFile = "dictionary.instructions.txt", int timeoutSeconds = 70)
    {
        var tool = ToolLocator.Codex(settings.CodexPath) ?? throw new ModelAccessException(ModelAccessFailure.ClientMissing);
        var dir = Path.Combine(userRoot, "codex-work");
        Directory.CreateDirectory(dir);
        var info = tool.StartInfo();
        info.UseShellExecute = false; info.CreateNoWindow = true; info.RedirectStandardInput = true;
        info.RedirectStandardOutput = true; info.RedirectStandardError = true; info.StandardInputEncoding = new UTF8Encoding(false);
        info.StandardOutputEncoding = Encoding.UTF8; info.StandardErrorEncoding = Encoding.UTF8; info.WorkingDirectory = dir;
        foreach (var arg in new[] { "exec", "--ignore-user-config", "--ephemeral", "--skip-git-repo-check", "--sandbox", "read-only", "--json",
            "--color", "never", "--output-schema", Path.Combine(assetFolder, schemaFile) }) info.ArgumentList.Add(arg);
        foreach (var config in new[] { "approval_policy=\"never\"", "web_search=\"disabled\"", "project_doc_max_bytes=0", "model_reasoning_effort=" + JsonSerializer.Serialize(settings.ReasoningEffort),
            "service_tier=" + JsonSerializer.Serialize(settings.ServiceTier), "features.fast_mode=true",
            "features.shell_tool=false", "features.apps=false", "features.plugins=false", "features.memories=false", "features.multi_agent=false",
            "features.computer_use=false", "features.browser_use=false", "features.skip_host_skill_discovery=true",
            "model_instructions_file=" + JsonSerializer.Serialize(Path.Combine(assetFolder, instructionsFile)) })
        { info.ArgumentList.Add("-c"); info.ArgumentList.Add(config); }
        if (!string.IsNullOrWhiteSpace(settings.Model)) { info.ArgumentList.Add("--model"); info.ArgumentList.Add(settings.Model.Trim()); }
        info.ArgumentList.Add("-");
        // The CLI manages OAuth itself. No access token, refresh token, or auth file is read by Lexi.
        using var process = new Process { StartInfo = info };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        token.ThrowIfCancellationRequested();
        process.Start();
        using var registration = timeout.Token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
        var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
        string? final = null;
        string? failure = null;
        try
        {
            await process.StandardInput.WriteLineAsync(prompt.AsMemory(), timeout.Token);
            process.StandardInput.Close();
            while (await process.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
            {
                if (line.Length > 250000) throw new InvalidDataException("Codex 返回内容过长。");
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
                if (root.TryGetProperty("item", out var item))
                {
                    var itemType = item.GetProperty("type").GetString();
                    if (itemType is "command_execution" or "mcp_tool_call" or "file_change" or "web_search")
                    { process.Kill(true); throw new InvalidDataException("词典查询意外请求了额外工具，已停止本次调用。"); }
                    if (type == "item.completed" && itemType == "agent_message") final = item.GetProperty("text").GetString();
                }
                if (type == "turn.failed") failure = "Codex 查询失败。请检查登录状态或账户额度。";
            }
            await process.WaitForExitAsync(timeout.Token);
            var error = await errorTask;
            timeout.Token.ThrowIfCancellationRequested();
            if (process.ExitCode != 0 || failure != null)
            {
                if (error.Contains("401") || error.Contains("not logged", StringComparison.OrdinalIgnoreCase)) throw new ModelAccessException(ModelAccessFailure.SignInRequired);
                throw new ModelAccessException(ModelAccessFailure.Unavailable);
            }
            return final ?? throw new InvalidDataException("Codex 未返回可读取的结果。");
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException($"Codex 查询超过 {timeoutSeconds} 秒，已取消。可以稍后重试。"); }
        finally
        {
            try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { }
            try { await errorTask; } catch (Exception ex) when (ex is OperationCanceledException or IOException) { }
        }
    }
}

internal sealed record DefinePrompt
{
    public required string Task { get; init; }
    public required string Query { get; init; }
    public int MaxSenses { get; init; }
    public required string InputCase { get; init; }
    public required string[] CoverageInterests { get; init; }
    public required int[] CoverageEvidence { get; init; }
    public required string Coverage { get; init; }
    public required string Overview { get; init; }
    public required string Translation { get; init; }
    public required EtymologyExcerpt[] EtymologyEvidence { get; init; }
    public required string Evidence { get; init; }
}
