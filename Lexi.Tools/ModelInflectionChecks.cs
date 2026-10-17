using System.Text.Json;
using Lexi.Core;

internal static class ModelInflectionChecks
{
    public static async Task Run(string root)
    {
        int count = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception("FAIL: " + label); Console.WriteLine("PASS: " + label); count++; }
        var primary = new LookupModel("codex", "test"); var secondary = new LookupModel("claude", "test");
        var senses = new AiSense[] { new("noun", "a synthetic object", "测试名词", "", "", "", []), new("verb", "a synthetic action", "测试动词", "", "", "", []) };
        AiWordForms verbs = new("glim", "verb", [new("past", ["glimmed"]), new("pastParticiple", ["glimmed"]), new("presentParticiple", ["glimming"]), new("thirdPerson", ["glims"])]);
        AiWordForms nouns = new("glim", "noun", [new("plural", ["glims"])]);
        var entry = new AiEntry("glim", true, senses, "", "unverified", WordForms: [verbs, nouns]);
        var parsed = CodexDictionary.ParseAndValidate(JsonSerializer.Serialize(entry, Json.Flexible), "glim", null, true);
        Check(parsed.WordForms?.Length == 2, "a model can add verb morphology alongside a known noun usage");
        WordFormSet[] recorded = [new("glim", [new("s", "复数", ["glims"])], "Local dictionary", "https://example.org/glim")];
        var supplemented = ModelInflections.Supplement("glim", recorded, [(primary, parsed)]).Single();
        Check(supplemented.Forms.Length == 5 && supplemented.Forms.Single(f => f.Kind == "s").SourceName == "Local dictionary" &&
            supplemented.Forms.Single(f => f.Kind == "i").SourceName.Contains("模型补充"), "model-only verb forms fill gaps without relabeling recorded noun forms");
        var onlyVerb = entry with { Senses = [senses[1]], WordForms = [nouns, verbs] };
        Check(ModelInflections.Sanitize("glim", onlyVerb) is [{ PartOfSpeech: "verb" }], "morphology cannot introduce a word class absent from that model's definitions");
        var invalid = verbs with { Forms = [new("plural", ["wrong"]), new("past", ["<script>", "", "glimmed"]), new("unknown", ["fake"])] };
        var clean = ModelInflections.Sanitize("glim", entry with { WordForms = [invalid] })!;
        Check(clean.Single().Forms is [{ Kind: "past", Words: ["glimmed"] }], "invalid spelling and wrong part-of-speech fields are discarded without losing definitions");
        Check(ModelInflections.Sanitize("glim", entry with { WordForms = [verbs with { Lemma = "unrelated" }] })!.Length == 0,
            "unrelated lemmas cannot appear under the queried headword");
        Check(ModelInflections.Sanitize("glimming", entry with { Word = "glimming", WordForms = [verbs] }) is [{ Lemma: "glim" }],
            "model morphology resolves an inflected query to its base lemma");
        Check(ModelInflections.Sanitize("résumé", entry with { Word = "résumé", WordForms = [new("résumé", "noun", [new("plural", ["résumés"])])] }) is [{ Forms: [{ Words: ["résumés"] }] }],
            "English loanwords retain their accented spellings");
        var other = entry with { WordForms = [verbs with { Forms = [new("past", ["glamt"])] }] };
        var disagreement = ModelInflections.Supplement("glim", [], [(primary, entry), (secondary, other)]).Single().Forms.Single(f => f.Kind == "p");
        Check(disagreement.Words.SequenceEqual(["glimmed"]) && disagreement.Note.Contains("glamt") && disagreement.Note.Contains("Claude"),
            "the configured primary spelling stays visible while conflicting model proposals remain inspectable");
        var sourced = new WordFormSet("glim", [new("p", "过去式", ["glam"])], "Recorded source", "https://example.org/recorded");
        var lateSource = ModelInflections.Supplement("glim", [sourced], [(primary, entry)]).Single().Forms.Single(f => f.Kind == "p");
        Check(lateSource.Words.SequenceEqual(["glam"]) && lateSource.SourceName == "Recorded source" && lateSource.Note.Contains("glimmed"),
            "dictionary data takes precedence without discarding a conflicting model alternative");
        Check(ModelInflections.Supplement("glim", [], [(primary, entry), (secondary, entry)]).Single().Forms.All(f => f.SourceName.Contains("模型补充")),
            "agreement between models never converts generated morphology into dictionary evidence");
        Check(StreamingEntry.Read(JsonSerializer.Serialize(entry, Json.Flexible), "glim")?.WordForms == null,
            "streaming previews do not expose partially completed morphology");
        try { CodexDictionary.ParseAndValidate(JsonSerializer.Serialize(entry with { WordForms = null }, Json.Flexible), "glim", null, true); Check(false, "missing new morphology field"); }
        catch (InvalidDataException) { Check(true, "new model responses must explicitly supply morphology or an empty array"); }

        var settings = new AppSettings { Provider = "codex", Model = "test", ParallelLookup = false, MergeSenses = false };
        var cache = new Cache(Path.Combine(root, "model-forms-" + Guid.NewGuid().ToString("N")));
        var legacy = entry with { WordForms = null };
        await cache.WriteAsync("ai-v3", primary.CacheKey("glim", settings), new AiEnvelope(legacy, CodexDictionary.EvidenceHash(null), DateTimeOffset.UtcNow, primary.Model, settings.ReasoningEffort, primary.Provider));
        var calls = 0;
        var coordinator = new DictionaryCoordinator(cache, (_, _, _, _, _, _) => { calls++; return Task.FromResult(entry); },
            (_, _, _) => throw new Exception("No additional judge call should be needed for morphology"));
        Check(coordinator.Cached("glim", null, settings).Answers.Single().Entry?.Found == true, "legacy definitions remain available before morphology enrichment");
        var retained = await coordinator.LookupAsync("glim", null, settings, false, CancellationToken.None);
        Check(calls == 0 && retained.Answers.Single().Entry?.WordForms == null, "legacy morphology caches remain unchanged without explicit refresh");
        var result = await coordinator.LookupAsync("glim", null, settings, true, CancellationToken.None);
        Check(calls == 1 && result.Answers.Single().Entry?.WordForms?.Length == 2, "explicit refresh acquires generic morphology for a legacy definition");
        await coordinator.LookupAsync("glim", null, settings, false, CancellationToken.None);
        Check(calls == 1, "the enriched model cache avoids repeated provider calls");
        settings.Online = false;
        var offline = await coordinator.LookupAsync("glim", null, settings, false, CancellationToken.None);
        Check(calls == 1 && ModelInflections.Supplement("glim", recorded, offline.Answers.Select(a => (a.Model, a.Entry!))).Single().Forms.Length == 5,
            "completed model word forms survive cache and remain usable offline");
        Console.WriteLine($"{count} model inflection checks passed");
    }
}
