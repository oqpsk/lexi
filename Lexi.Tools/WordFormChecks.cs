using System.Text.Json;
using Lexi.Core;

internal static class WordFormChecks
{
    public static void Run()
    {
        int count = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception("FAIL: " + label); Console.WriteLine("PASS: " + label); count++; }
        string Field(string label, params string[] words) => "<i>" + label + "</i> " + string.Join(" <i>or</i> ", words.Select(w =>
            "<b lang=\"en\" class=\"Latn form-of lang-en test-form-of\"><a>" + w + "</a></b>"));
        string Head(string word, string language, string body) => "<p><span class=\"headword-line\"><strong class=\"Latn headword\" lang=\"" + language + "\">" + word + "</strong> (" + body + ")</span></p>";
        var verb = Head("mog", "en", Field("third-person singular simple present", "mogs") + ", " + Field("present participle", "mogging") + ", " + Field("simple past and past participle", "mogged"));
        var html = verb + verb + Head("mog", "en", Field("plural", "mogs")) + Head("unrelated", "fr", Field("plural", "wrong"));
        var forms = WordForms.ParseEnglishHtml(html, "https://example.org/mog");
        Check(forms.Length == 1 && forms[0].Forms.Length == 5 && forms[0].Forms.Single(f => f.Kind == "i").Words.SequenceEqual(["mogging"]) &&
            forms[0].Roles("mogged").SequenceEqual(["过去式", "过去分词"]), "online headwords supply doubled-consonant slang verb forms alongside the separate noun plural");
        Check(forms[0].Forms.Single(f => f.Kind == "3").Words.SequenceEqual(["mogs"]) && forms[0].Forms.All(f => !f.Words.Contains("wrong")),
            "duplicate headwords collapse and non-English headwords cannot supply English inflections");
        WordFormSet[] local = [new("mog", [new("s", "复数", ["mogs"])], "Offline", "https://example.org/offline")];
        var combined = WordForms.Combine(local, forms).Single();
        Check(combined.Forms.Length == 5 && combined.Forms.Single(f => f.Kind == "s").Words.Length == 1 &&
            combined.Forms.Single(f => f.Kind == "s").SourceName.Contains("Offline") && combined.Forms.Single(f => f.Kind == "i").SourceName.Contains("Wiktionary"),
            "online verbs fill missing local forms without replacing noun data or losing per-form sources");
        var irregular = WordForms.ParseEnglishHtml(Head("go", "en", Field("simple past", "went") + Field("past participle", "gone")), "source").Single();
        Check(irregular.Forms.Single(f => f.Kind == "p").Words.SequenceEqual(["went"]) && irregular.Forms.Single(f => f.Kind == "d").Words.SequenceEqual(["gone"]),
            "past and participle spellings stay distinct for irregular verbs");
        var variants = WordForms.ParseEnglishHtml(Head("learn", "en", Field("simple past and past participle", "learned", "learnt")), "source").Single();
        Check(variants.Forms.All(f => f.Words.SequenceEqual(["learned", "learnt"])), "online inflections retain alternative spellings");
        Check(WordForms.ParseEnglishHtml(Head("unknown", "en", "no inflection data"), "source").Length == 0 &&
            WordForms.ParseEnglishHtml("<p>ordinary mention: " + Field("plural", "imagined") + "</p>", "source").Length == 0,
            "missing headword metadata never guesses or extracts forms from ordinary prose");
        var entry = new PronunciationEntry("mog", [], "https://example.org/mog", DateTimeOffset.UtcNow, forms);
        var saved = JsonSerializer.Deserialize<PronunciationEntry>(JsonSerializer.Serialize(entry, Json.Flexible), Json.Flexible)!;
        Check(saved.Forms!.Single().Forms.Single(f => f.Kind == "i").Words.Single() == "mogging" && saved.Forms![0].SourceUrl == entry.SourceUrl,
            "word forms and attribution survive the same cache round trip used for offline metadata");
        var legacy = JsonSerializer.Deserialize<PronunciationEntry>("{\"word\":\"word\",\"ipa\":[\"/wɜːd/\"],\"sourceUrl\":\"source\",\"fetchedAt\":\"2026-01-01T00:00:00Z\"}", Json.Flexible);
        Check(legacy is { Forms: null, Ipa.Length: 1 }, "legacy pronunciation caches remain readable without forms");
        Console.WriteLine($"{count} online word-form checks passed");
    }
}
