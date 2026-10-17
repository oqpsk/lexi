using System.Diagnostics;
using System.Text.Json;
using Lexi.Core;

internal static class ParallelLive
{
    public static async Task Run(string data, string assets, string work, IEnumerable<string> queries)
    {
        data = Path.GetFullPath(data); assets = Path.GetFullPath(assets); work = Path.GetFullPath(work);
        using var library = new OfflineLibrary(data);
        using var web = new OnlineDictionary();
        using var claude = new ClaudeDictionary(assets, work);
        var codex = new CodexDictionary(assets, work);
        var arguments = queries.ToArray();
        const string modelOption = "--codex-model=";
        var codexModel = arguments.FirstOrDefault(a => a.StartsWith(modelOption, StringComparison.Ordinal))?[modelOption.Length..] ?? "gpt-6-luna";
        var settings = new AppSettings { Provider = "codex", Model = codexModel, ParallelLookup = true, MergeSenses = true };
        claude.Prepare("haiku");
        var json = new JsonSerializerOptions(Json.Flexible) { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        foreach (var query in arguments.Where(a => !a.StartsWith(modelOption, StringComparison.Ordinal)))
        {
            var online = await web.LookupAsync(query, CancellationToken.None);
            var evidence = DictionaryEvidence.Combine(query, library.Lookup(query, settings), online);
            var watch = Stopwatch.StartNew();
            long firstMilliseconds = -1;
            long? judgeMs = null;
            var coordinator = new DictionaryCoordinator(new Cache(work), async (model, word, sources, options, token, progress) =>
            {
                var result = model.Provider == "claude" ? await claude.LookupAsync(word, sources, model.Model, token, progress)
                    : await codex.LookupAsync(word, sources, options, token);
                Console.WriteLine(JsonSerializer.Serialize(new { query, provider = model.Name, elapsedMs = watch.ElapsedMilliseconds, result }, json));
                return result;
            }, async (request, options, token) =>
            {
                var timer = Stopwatch.StartNew();
                var plan = await codex.JudgeSensesAsync(request, options, token); judgeMs = timer.ElapsedMilliseconds;
                Console.WriteLine(JsonSerializer.Serialize(new { query, judgeMs, plan }, json));
                return plan;
            });
            var merged = await coordinator.LookupAsync(query, evidence, settings, true, CancellationToken.None, new CallbackProgress<LookupSnapshot>(s =>
            { if (s.Groups.Length > 0) Interlocked.CompareExchange(ref firstMilliseconds, watch.ElapsedMilliseconds, -1); }));
            var firstMs = firstMilliseconds;
            Console.WriteLine(JsonSerializer.Serialize(new { query, firstMs, totalMs = watch.ElapsedMilliseconds, judgeMs, merged.MergeNote,
                models = merged.Answers.Select(a => new { a.Model.Name, a.Phase, a.Error }),
                groups = merged.Groups.Select(g => new { g.Representative.Sense.Chinese, g.Representative.Sense.English, variants = g.Variants.Length }) }, json));
            if (merged.Answers.Any(a => a.Phase == ModelPhase.Failed)) throw new InvalidDataException("A live dictionary provider failed.");
            if (!merged.Groups.Any()) throw new InvalidDataException("Expected definitions for the live fixture.");
            if (merged.MergeNote.Contains("未完成")) throw new InvalidDataException("The live sense judge failed.");
            if (query is "chad" or "mog" && !merged.Groups[0].Variants.Any(v => SenseMerging.IsCurrent(v.Sense.Register)))
                throw new InvalidDataException("The modern slang meaning must be visible first.");
        }
    }
}
