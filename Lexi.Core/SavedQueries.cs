using System.Text.Json;

namespace Lexi.Core;

public sealed record SavedPronunciation(string Text, string Label, string Attribution, string LabelAttribution, bool Sourced);
public sealed record SavedDictionary(string Query, LocalHit[] Local, WordFormSet[] LocalForms, WordFormSet[] OnlineForms,
    EtymologyExcerpt[] Etymologies, OnlineEntry? Online, OnlineEntry? Evidence, LookupSnapshot Result,
    SavedPronunciation Pronunciation, bool MergeEnabled, bool SourceIncomplete, DateTimeOffset SavedAt, int Version = 1);
public sealed record SavedTranslation(TranslationEntry Entry, LookupModel Model, DateTimeOffset SavedAt, int Version = 1);

/// <summary>Completed query views are stable until explicitly replaced, independently of provider settings and age.</summary>
public sealed class SavedQueries(Cache cache)
{
    public const string DictionaryKind = "saved-dictionaries-v1";
    public const string TranslationKind = "saved-translations-v1";
    public SavedDictionary? Dictionary(string query, bool force = false)
    {
        if (force) return null;
        var saved = cache.Read<SavedDictionary>(DictionaryKind, Queries.LookupKey(query));
        return Valid(saved, Queries.LookupKey(query)) ? saved : null;
    }

    private static bool Valid(SavedDictionary? saved, string query) => saved is { Version: 1, Local: not null,
        LocalForms: not null, OnlineForms: not null, Etymologies: not null, Pronunciation: not null,
        Result: { Answers: not null, Groups: not null } result } && saved.Query == query &&
        saved.Local.All(l => l is { Dictionary: not null, Entry: not null }) &&
        result.Answers.All(a => a is { Model: not null } && (a.Entry == null || a.Entry.Senses != null)) && !result.Provisional &&
        result.Groups.All(g => g?.Variants is { Length: > 0 } && g.Variants.All(v => v is { Provisional: false, Sense: not null, Model: not null }));

    public async Task<bool> SaveDictionaryAsync(SavedDictionary saved, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Valid(saved, saved.Query)) return false;
        // No result yet: don't turn failed/unfinished attempts into permanent empty pages.
        if (saved.Local.Length == 0 && saved.Online == null && !saved.Result.Answers.Any(a => a.Entry != null) &&
            SpecialistReferences.Lookup(saved.Query).Length == 0) return false;
        if ((saved.SourceIncomplete || saved.Result.MergeFailed || saved.Result.Answers.Any(a => a.Phase == ModelPhase.Failed)) &&
            Dictionary(saved.Query) != null) return false;
        await cache.WriteAsync(DictionaryKind, saved.Query, saved, token); return true;
    }

    public SavedTranslation? TranslationResult(string text, string target, bool force = false)
    {
        if (force) return null;
        var saved = cache.Read<SavedTranslation>(TranslationKind, TranslationIdentity(text, target));
        if (saved is not { Version: 1, Model: not null, Entry: not null }) return null;
        try { Translation.Parse(JsonSerializer.Serialize(saved.Entry, Json.Options), text, target); return saved; }
        catch (InvalidDataException) { return null; }
    }
    public async Task SaveTranslationAsync(string text, string target, SavedTranslation saved, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Translation.Parse(JsonSerializer.Serialize(saved.Entry, Json.Options), text, target);
        await cache.WriteAsync(TranslationKind, TranslationIdentity(text, target), saved, token);
    }
    private static string TranslationIdentity(string text, string target) => JsonSerializer.Serialize(new TranslationIdentity(text, target), Json.Options);
}

internal sealed record TranslationIdentity(string Text, string Target);
