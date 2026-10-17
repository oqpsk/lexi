using System.Text.Json;

namespace Lexi.Core;

public sealed class DictionaryCoordinator(Cache cache,
    Func<LookupModel, string, OnlineEntry?, AppSettings, CancellationToken, IProgress<AiEntry>, Task<AiEntry>> lookup,
    Func<SenseMergeRequest, AppSettings, CancellationToken, Task<SenseMergePlan>> judge)
{
    public const string CacheKind = "ai-v6";
    public static bool ExpectedFailure(Exception e) => e is InvalidOperationException or TimeoutException or InvalidDataException or
        JsonException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception;

    private AiEnvelope? Read(string query, OnlineEntry? evidence, LookupModel model, AppSettings settings, out bool legacy)
    {
        var saved = cache.Read<AiEnvelope>(CacheKind, model.CacheKey(query, settings));
        legacy = saved == null;
        if (legacy) saved = cache.Read<AiEnvelope>("ai-v5", model.CacheKey(query, settings)) ?? cache.Read<AiEnvelope>("ai-v4", model.CacheKey(query, settings)) ?? cache.Read<AiEnvelope>("ai-v3", model.CacheKey(query, settings));
        if (saved == null || saved.Provider != model.Provider || saved.Model != model.Model ||
            saved.ReasoningEffort != settings.ReasoningEffort || (saved.EvidenceHash != CodexDictionary.EvidenceHash(evidence) && !(legacy && query == Queries.Normalize(query) && saved.EvidenceHash == CodexDictionary.LegacyEvidenceHash(evidence)))) return null;
        try { return saved with { Entry = CodexDictionary.ParseAndValidate(JsonSerializer.Serialize(saved.Entry, Json.Options), query, evidence) }; }
        catch (Exception e) when (ExpectedFailure(e)) { return null; }
    }

    public LookupSnapshot Cached(string query, OnlineEntry? evidence, AppSettings settings)
    {
        var answers = LookupModel.Selected(settings).Select(model =>
        {
            var saved = Read(query, evidence, model, settings, out _);
            return new ModelAnswer(model, ModelPhase.Offline, saved?.Entry, saved != null);
        }).ToArray();
        var request = SenseMerging.Request(query, answers);
        SenseMergePlan? plan = null;
        if (settings.MergeSenses && cache.Read<SenseMergePlan>(SenseMerging.CacheKind, request.InputHash + ":" + settings.JudgeModel) is { } savedPlan)
        {
            try { plan = SenseMerging.Parse(JsonSerializer.Serialize(savedPlan, Json.Options), request); }
            catch (Exception ex) when (ExpectedFailure(ex)) { }
        }
        return new(answers, SenseMerging.Group(answers, plan));
    }

    public async Task<LookupSnapshot> LookupAsync(string query, OnlineEntry? evidence, AppSettings options, bool force,
        CancellationToken token, IProgress<LookupSnapshot>? progress = null)
    {
        var settings = options.ForModel(LookupModel.Primary(options));
        var models = LookupModel.Selected(settings);
        var loaded = models.Select(m => { var saved = Read(query, evidence, m, settings, out var legacy); return (Saved: saved, Legacy: legacy); }).ToArray();
        var saved = loaded.Select(s => s.Saved).ToArray();
        var answers = models.Select((m, i) => new ModelAnswer(m, ModelPhase.Pending, saved[i]?.Entry, saved[i] != null)).ToArray();
        var closed = new bool[models.Length];
        var sync = new object();
        LookupSnapshot Snapshot() => new([.. answers], SenseMerging.Group(answers));
        void Publish() { if (!token.IsCancellationRequested) progress?.Report(Snapshot()); }
        lock (sync) Publish();
        await Task.WhenAll(models.Select(async (model, i) =>
        {
            try
            {
                token.ThrowIfCancellationRequested();
                if (!settings.Online || !force && saved[i] != null)
                {
                    lock (sync) { answers[i] = answers[i] with { Phase = settings.Online ? ModelPhase.Complete : ModelPhase.Offline }; closed[i] = true; Publish(); }
                    return;
                }
                var preview = new InlineProgress<AiEntry>(entry =>
                {
                    lock (sync)
                    {
                        if (closed[i] || token.IsCancellationRequested) return;
                        answers[i] = new(model, ModelPhase.Streaming, entry); Publish();
                    }
                });
                var result = await lookup(model, query, evidence, settings.ForModel(model), token, preview);
                token.ThrowIfCancellationRequested();
                result = CodexDictionary.ParseAndValidate(JsonSerializer.Serialize(result, Json.Options), query, evidence);
                lock (sync) { closed[i] = true; answers[i] = new(model, ModelPhase.Complete, result); Publish(); }
                if (result.Found)
                    await Write(CacheKind, model.CacheKey(query, settings), new AiEnvelope(result, CodexDictionary.EvidenceHash(evidence),
                        DateTimeOffset.UtcNow, model.Model, settings.ReasoningEffort, model.Provider), token);
            }
            catch (Exception ex) when (ExpectedFailure(ex) || ex is OperationCanceledException && !token.IsCancellationRequested)
            {
                lock (sync)
                {
                    closed[i] = true;
                    answers[i] = new(model, ModelPhase.Failed, saved[i]?.Entry, saved[i] != null,
                        ex is OperationCanceledException ? "查询超时" : ex.Message);
                    Publish();
                }
            }
            finally { lock (sync) closed[i] = true; }
        }));
        token.ThrowIfCancellationRequested();
        var snapshot = Snapshot();
        var request = SenseMerging.Request(query, answers);
        if (!settings.Online || !settings.MergeSenses || request.Pairs.Length == 0) return snapshot;
        progress?.Report(snapshot with { Merging = true });
        var mergeKey = request.InputHash + ":" + settings.JudgeModel;
        try
        {
            SenseMergePlan? plan = null;
            if (!force && cache.Read<SenseMergePlan>(SenseMerging.CacheKind, mergeKey) is { } cached)
            {
                try { plan = SenseMerging.Parse(JsonSerializer.Serialize(cached, Json.Options), request); }
                catch (Exception ex) when (ExpectedFailure(ex)) { }
            }
            if (plan == null)
            {
                plan = await judge(request, settings.ForModel(ModelCatalog.Judge(settings)), token);
                plan = SenseMerging.Parse(JsonSerializer.Serialize(plan, Json.Options), request);
                token.ThrowIfCancellationRequested();
                await Write(SenseMerging.CacheKind, mergeKey, plan, token);
            }
            token.ThrowIfCancellationRequested();
            return snapshot with { Groups = SenseMerging.Group(answers, plan), MergeNote = "同义项已整理" };
        }
        catch (Exception ex) when (ExpectedFailure(ex) || ex is OperationCanceledException && !token.IsCancellationRequested)
        {
            token.ThrowIfCancellationRequested();
            var failure = MergeFailures.Classify(ex);
            return snapshot with { MergeNote = MergeFailures.Describe(failure) + "；已保留全部释义", MergeFailed = true, MergeFailure = failure };
        }
    }

    private async Task Write<T>(string kind, string key, T value, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try { await cache.WriteAsync(kind, key, value, token); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
