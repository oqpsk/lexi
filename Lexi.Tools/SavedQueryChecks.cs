using System.Text.Json;
using Lexi.Core;

internal static class SavedQueryChecks
{
    public static async Task Run(string root)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); }
        var path = Path.Combine(root, "saved-queries-" + Guid.NewGuid().ToString("N"));
        var cache = new Cache(path); var results = new SavedQueries(cache);
        var model = new LookupModel("codex", "saved-model");
        var entry = new AiEntry("TEE", true, [new("noun", "A synthetic concept.", "合成概念", "", "", "", [], Gloss: "合成概念", CoreMeaning: true)], "", "unverified", "/tiː/", [], "测试语源");
        var answer = new ModelAnswer(model, ModelPhase.Complete, entry);
        var snapshot = new LookupSnapshot([answer], SenseMerging.Group([answer]));
        var old = DateTimeOffset.UtcNow.AddYears(-5);
        var saved = new SavedDictionary("TEE", [], [], [], [], null, null, snapshot, new("/tiː/", " AI", "old model", "old model", false), true, false, old);
        Check(await results.SaveDictionaryAsync(saved, CancellationToken.None), "completed dictionary views persist with all metadata");
        var reopened = new SavedQueries(new Cache(path));
        var restored = reopened.Dictionary("TEE")!;
        Check(restored.SavedAt == old && restored.Result.Answers[0].Model == model && restored.Pronunciation.Text == "/tiː/" &&
            restored.Result.Answers[0].Entry!.Etymology == "测试语源", "saved views survive restart and age without losing producing model, IPA or etymology");
        Check(reopened.Dictionary("tee") == null && reopened.Dictionary("TEE", true) == null && reopened.Dictionary(" TEE ") != null,
            "dictionary cache preserves case and only explicit regeneration bypasses a valid saved page");
        Check(!await results.SaveDictionaryAsync(saved with {Result = snapshot with {Merging = true}}, CancellationToken.None) &&
            !await results.SaveDictionaryAsync(saved with {Result = snapshot with {Answers = [answer with {Phase = ModelPhase.Streaming}]}}, CancellationToken.None),
            "streaming and still-merging output cannot replace a stable saved view");
        var failed = snapshot with {Answers = [answer with {Phase = ModelPhase.Failed}], MergeFailed = true};
        Check(!await results.SaveDictionaryAsync(saved with {Result = failed}, CancellationToken.None) && !results.Dictionary("TEE")!.Result.MergeFailed,
            "failed regeneration retains the previously completed result");
        Check(!await results.SaveDictionaryAsync(saved with {Query = "empty", Result = new([], [])}, CancellationToken.None),
            "an empty failed attempt is not frozen as a reusable result");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        try { await results.SaveDictionaryAsync(saved with {Pronunciation = saved.Pronunciation with {Text = "changed"}}, canceled.Token); Check(false,"canceled save"); }
        catch (OperationCanceledException) { Check(results.Dictionary("TEE")!.Pronunciation.Text == "/tiː/", "canceled persistence cannot replace the previous query view"); }
        var updated = saved with {Pronunciation = saved.Pronunciation with {Text = "/nuː/"}, SavedAt = DateTimeOffset.UtcNow};
        await results.SaveDictionaryAsync(updated, CancellationToken.None);
        Check(new SavedQueries(new Cache(path)).Dictionary("TEE")!.Pronunciation.Text == "/nuː/", "successful explicit refresh replaces the stable saved view atomically");
        await cache.WriteAsync(SavedQueries.DictionaryKind, "broken", JsonDocument.Parse("{\"version\":1,\"query\":\"broken\"}").RootElement);
        Check(results.Dictionary("broken") == null, "incomplete saved-view schemas fall back safely");
        string text = "One paragraph.\n\nAnother paragraph.";
        var translation = new TranslationEntry(Cache.Hash(text), "zh", ["一段。", "另一段。"], "");
        await results.SaveTranslationAsync(text, "zh", new(translation, model, old), CancellationToken.None);
        var translated = reopened.TranslationResult(text, "zh");
        Check(translated?.Model == model && translated.SavedAt == old && translated.Entry.Paragraphs.Length == 2,
            "complete translations retain the original model across age and current model changes");
        Check(results.TranslationResult(text.ToLowerInvariant(), "zh") == null && results.TranslationResult(text.Replace("\n\n", " "), "zh") == null &&
            results.TranslationResult(text, "en") == null && results.TranslationResult(text, "zh", true) == null,
            "translation identities preserve case, paragraph boundaries and direction, with an explicit refresh bypass");
        Check(results.Dictionary(text) == null, "dictionary and translation views never collide");
        var settings = new AppSettings { Provider = model.Provider, Model = model.Model, ParallelLookup = false, MergeSenses = false };
        await cache.WriteAsync(DictionaryCoordinator.CacheKind, model.CacheKey("TEE", settings),
            new AiEnvelope(entry, CodexDictionary.EvidenceHash(null), old, model.Model, settings.ReasoningEffort, model.Provider));
        int calls = 0;
        var coordinator = new DictionaryCoordinator(cache, (_,_,_,_,_,_) => { calls++; return Task.FromResult(entry); }, (_,_,_) => throw new Exception("unexpected judge"));
        await coordinator.LookupAsync("TEE", null, settings, false, CancellationToken.None);
        Check(calls == 0, "years-old valid provider caches do not automatically regenerate");
        await coordinator.LookupAsync("TEE", null, settings, true, CancellationToken.None);
        Check(calls == 1, "explicit refresh invokes the selected model despite its existing cache");
    }
}
