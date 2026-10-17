using System.Text.Json;

namespace Lexi.Core;

// Per-query presentation state shared by every front end. Each property is replaced by a new
// instance only when it changes, so a renderer can redraw just the affected section.
public sealed class QueryPresentation
{
    public static readonly SavedPronunciation NoPronunciation = new("", "", "", "", false);
    private readonly Dictionary<LookupModel, AiEntry> wordFormAnswers = [];
    private readonly Dictionary<LookupModel, string> modelEtymologies = [];
    private LookupModel[] wordFormModelOrder = [];
    private string wordFormQuery = "", etymologyIdentity = "";
    private LocalHit[] overviewLocal = [];
    private bool overviewFromModel, overviewCore;

    public string Query { get; private set; } = "";
    public string Heading { get; private set; } = "";
    public SavedPronunciation Pronunciation { get; private set; } = NoPronunciation;
    public WordFormSet[] LocalWordForms { get; private set; } = [];
    public WordFormSet[] OnlineWordForms { get; private set; } = [];
    public WordFormRow[] FormRows { get; private set; } = [];
    public EtymologyExcerpt[] SourceEtymologies { get; private set; } = [];
    public EtymologyView[] Etymology { get; private set; } = [];
    public QuickReadView QuickRead { get; private set; } = QuickReadView.Hidden;
    public LocalSourceView[] Local { get; private set; } = [];
    public string LocalHint { get; private set; } = "";
    public OnlineSourceView? Online { get; private set; }
    public string OnlineHint { get; private set; } = "";
    public AnswersView? Answers { get; private set; }
    public TranslationView? Translation { get; private set; }

    public void BeginDictionary(string query, string heading)
    {
        Query = query; Heading = heading; Pronunciation = NoPronunciation;
        ResetLexical(); ResetWordForms(); ClearSources();
    }

    public void BeginTranslation(string text, string target)
    {
        Query = text; Heading = "翻译";
        Pronunciation = new(target == "zh" ? "英文 → 中文" : "中文 → English", "", "", "", false);
        ResetLexical(); ResetWordForms(); ClearSources();
    }

    public void SetQuery(string query) => Query = query;
    private void ClearSources() { Local = []; LocalHint = ""; Online = null; OnlineHint = ""; Answers = null; Translation = null; }

    // ---- Word forms ----
    public void ResetWordForms()
    { LocalWordForms = []; OnlineWordForms = []; wordFormAnswers.Clear(); wordFormModelOrder = []; wordFormQuery = ""; FormRows = []; }
    private bool MatchWordFormQuery(string query)
    {
        if (wordFormQuery.Length > 0 && wordFormQuery != query) return false;
        wordFormQuery = query; return true;
    }
    public void ApplyLocalWordForms(string query, WordFormSet[] entries)
    { if (!MatchWordFormQuery(query)) return; LocalWordForms = Queries.AbbreviationCandidate(query) ? [] : entries; RefreshWordForms(query); }
    public void ApplyOnlineWordForms(string query, WordFormSet[] entries)
    { if (!MatchWordFormQuery(query)) return; OnlineWordForms = Queries.AbbreviationCandidate(query) ? entries.Where(e => e.Lemma == query).ToArray() : entries; RefreshWordForms(query); }
    public void ApplyModelWordForms(string query, LookupSnapshot snapshot)
    {
        if (query.Length == 0 || !MatchWordFormQuery(query)) return;
        wordFormModelOrder = snapshot.Answers.Select(a => a.Model).ToArray();
        var changed = false;
        foreach (var answer in snapshot.Answers)
        {
            // Keep the last complete data while a replacement streams. Partial morphology
            // never becomes visible or cached, even if a provider preview includes it.
            if (answer.Phase == ModelPhase.Streaming) continue;
            if (answer.Entry is { Found: true, WordForms: not null } entry && Queries.Normalize(entry.Word) == Queries.Normalize(query))
            { wordFormAnswers[answer.Model] = entry; changed = true; }
            else if (answer.Entry is { Found: false } || answer.Phase == ModelPhase.Failed && !answer.Cached)
                changed |= wordFormAnswers.Remove(answer.Model);
        }
        if (changed) RefreshWordForms(query);
    }
    private void RefreshWordForms(string query) => FormRows = Rows(query, ModelInflections.Supplement(query,
        WordForms.Combine(LocalWordForms, OnlineWordForms), wordFormModelOrder.Where(wordFormAnswers.ContainsKey).Select(m => (m, wordFormAnswers[m]))));

    public static WordFormRow[] Rows(string query, WordFormSet[] entries) => entries.Select(entry =>
    {
        var cells = new List<WordFormCell>();
        var past = entry.Forms.FirstOrDefault(f => f.Kind == "p");
        var participle = entry.Forms.FirstOrDefault(f => f.Kind == "d");
        var sharedPast = past != null && participle != null && past.Words.SequenceEqual(participle.Words);
        var lemma = Queries.Normalize(query) == Queries.Normalize(entry.Lemma) ? entry.Lemma : query + " → " + entry.Lemma;
        foreach (var original in new[] { new WordForm("0", "原形", [lemma]) }.Concat(entry.Forms))
        {
            if (sharedPast && original.Kind == "d") continue;
            var form = sharedPast && original.Kind == "p" ? original with { Label = "过去式 / 过去分词",
                SourceName = string.Join(" / ", new[] { original.SourceName, participle!.SourceName }.Where(s => s.Length > 0).Distinct()),
                SourceUrl = string.Join("\n", new[] { original.SourceUrl, participle!.SourceUrl }.Where(s => s.Length > 0).Distinct()),
                Note = string.Join("\n", new[] { original.Note, participle!.Note }.Where(s => s.Length > 0).Distinct()) } : original;
            var source = form.SourceName.Length > 0 ? form.SourceName : entry.SourceName;
            var url = form.SourceUrl.Length > 0 ? form.SourceUrl : entry.SourceUrl;
            cells.Add(new(form.Label, string.Join(" / ", form.Words),
                string.Join("\n", new[] { "词形来源：" + source, url, form.Note }.Where(s => s.Length > 0)),
                form.Words.Any(w => Queries.Normalize(w) == Queries.Normalize(query)),
                string.Join(" / ", form.Kind == "0" ? [entry.Lemma] : form.Words)));
        }
        return new WordFormRow(cells.ToArray());
    }).ToArray();

    // ---- Etymology and overview ----
    public void ResetLexical()
    {
        SourceEtymologies = []; modelEtymologies.Clear(); etymologyIdentity = ""; Etymology = [];
        overviewLocal = []; overviewFromModel = false; overviewCore = false; QuickRead = QuickReadView.Hidden;
    }
    public void ApplySourceEtymology(EtymologyExcerpt[] excerpts) { SourceEtymologies = excerpts; RefreshEtymology(); }
    private void ApplyModelEtymology(LookupSnapshot snapshot)
    {
        foreach (var answer in snapshot.Answers)
        {
            if (answer.Phase == ModelPhase.Streaming) continue;
            if (answer.Entry is { Found: true, Etymology.Length: > 0 } entry) modelEtymologies[answer.Model] = entry.Etymology;
            else if (answer.Phase is ModelPhase.Complete or ModelPhase.Failed && !answer.Cached) modelEtymologies.Remove(answer.Model);
        }
        RefreshEtymology();
    }
    private void RefreshEtymology()
    {
        var identity = JsonSerializer.Serialize(new EtymologyIdentity(SourceEtymologies, modelEtymologies.Select(p => new ModelEtymology(p.Key, p.Value)).ToArray()), Json.Options);
        if (identity == etymologyIdentity) return;
        etymologyIdentity = identity;
        Etymology = modelEtymologies.OrderBy(p => p.Key.Provider == "codex" ? 0 : 1)
            .Select(p => new EtymologyView(p.Value, p.Key.Name + " · 模型语源说明，未逐项核对", "", "", true))
            .Concat(SourceEtymologies.Select(e => new EtymologyView(e.Text, e.Label + " · Wiktionary 原文", "Wiktionary · CC BY-SA 4.0", e.SourceUrl, false)))
            .ToArray();
    }

    public void BeginOverview(LocalHit[] local) => overviewLocal = local;
    private void ApplyQuickRead(LookupSnapshot snapshot, SpecialistReference[] uncovered, bool finalized)
    {
        var generated = snapshot.Groups.Any(g => g.Variants.Any(v => !v.Provisional));
        if (!generated && overviewFromModel && !finalized && snapshot.Provisional)
        { QuickRead = Caption(QuickRead, "缓存预览"); return; }
        var meanings = QuickReadSelection(snapshot, uncovered);
        overviewFromModel = generated && meanings.Length > 0;
        if (meanings.Length == 0) { QuickRead = QuickRead with { Visible = false }; return; }
        var state = !generated ? "词典摘录" : !finalized || snapshot.Provisional ? "预览" :
            snapshot.MergeFailed || snapshot.Answers.Any(a => a.Phase == ModelPhase.Failed) ? "部分结果" : "已整理";
        overviewCore = meanings.All(m => m.Core);
        QuickRead = Caption(QuickRead, state) with { Visible = true, Text = string.Join("；", meanings.Select(m => m.Text)),
            Tooltip = string.Join("\n", meanings.Select(m => m.Label + "：" + m.Text)) };
    }
    private QuickMeaning[] QuickReadSelection(LookupSnapshot snapshot, SpecialistReference[] uncovered) =>
        Lexi.Core.QuickRead.Create(snapshot.Groups, uncovered, overviewLocal);
    private QuickReadView Caption(QuickReadView view, string state) => view with
    {
        State = state, Caption = "TL;DR · " + state, ShowState = state is "预览" or "缓存预览" or "部分结果",
        Explanation = overviewCore ? "从完整义项中选出的核心含义，不涵盖全部用法。详细释义和参考资料见下方。" :
            "当前可用释义的简短摘录，尚未选定核心含义。完整用法见下方。"
    };

    // ---- Pronunciation ----
    public void RestorePronunciation(SavedPronunciation pronunciation) => Pronunciation = pronunciation;
    public void ApplySourcePronunciation(string query, PronunciationEntry entry)
    {
        if (entry.Forms is { Length: > 0 }) ApplyOnlineWordForms(query, entry.Forms);
        ApplySourceEtymology(entry.Etymologies ?? []);
        var values = entry.Ipa.Select(Lexi.Core.Pronunciation.Format).Where(p => p.Length > 0).Distinct().ToArray();
        if (values.Length == 0) return;
        Pronunciation = new(string.Join(" · ", values), "", "Wiktionary 音标 · CC BY-SA 4.0\n" + entry.SourceUrl, "", true);
    }
    private void ApplyModelPronunciation(LookupSnapshot snapshot)
    {
        if (Pronunciation.Sourced) return;
        var answer = snapshot.Answers.FirstOrDefault(a => a.Entry is { Found: true } && Lexi.Core.Pronunciation.Valid(a.Entry.Phonetic));
        if (answer?.Entry is not { } entry) return;
        var attribution = answer.Model.Name + " 提供的音标 · 未经词典资料核对";
        Pronunciation = new(Lexi.Core.Pronunciation.Format(entry.Phonetic), "  AI", attribution, attribution, false);
    }

    // ---- Sources ----
    public void ShowLocal(LocalHit[] hits, string query, bool offlineEnabled)
    {
        Local = []; LocalHint = "";
        if (!offlineEnabled) { LocalHint = "离线词典已停用，可在设置中重新启用。"; return; }
        if (hits.Length == 0) { LocalHint = "已启用的离线词典未收录此词。"; return; }
        var pronunciationHit = Queries.AbbreviationCandidate(query) ? null : hits.FirstOrDefault(h => Lexi.Core.Pronunciation.Format(h.Entry.Phonetic).Length > 0);
        Pronunciation = Pronunciation with { Text = pronunciationHit == null ? "" : Lexi.Core.Pronunciation.Format(pronunciationHit.Entry.Phonetic),
            Sourced = pronunciationHit != null, Attribution = pronunciationHit == null ? "" : "音标来源：" + pronunciationHit.Dictionary.Name };
        Local = hits.Select(hit => new LocalSourceView(hit.Dictionary.Name,
            Queries.Normalize(hit.Entry.Word) != Queries.Normalize(query) ? "词条：" + hit.Entry.Word : "",
            hit.Entry.Chinese, hit.Entry.English, hit.Entry.Original, hit.Dictionary.SourceUrl,
            hit.Dictionary.Attribution + " · " + hit.Dictionary.License)).ToArray();
    }

    public void ShowOnline(OnlineEntry entry, bool cached)
    {
        OnlineHint = "";
        Online = new($"Wiktionary 原文 · {entry.Senses.Length} 条释义",
            entry.Senses.Select((s, i) => new OnlineSenseView($"[{i + 1}]  {s.PartOfSpeech.ToLowerInvariant()}", s.Definition,
                s.Examples.Select(e => new OnlineExampleView(e, Queries.HasChinese(e))).ToArray())).ToArray(),
            entry.Url, $"{(cached ? "缓存于" : "查询于")} {entry.FetchedAt.ToLocalTime():yyyy-MM-dd HH:mm} · CC BY-SA 4.0", cached);
    }
    public void ShowNoOnline(bool noLocalEntries) { Online = null; OnlineHint = noLocalEntries ? "Wiktionary 暂无可用词条。" : ""; }

    // ---- Model answers ----
    public void ApplyAnswers(LookupSnapshot snapshot, OnlineEntry? evidence, bool finalized = true)
    {
        var firstWord = snapshot.Answers.FirstOrDefault(a => a.Entry != null)?.Entry?.Word;
        ApplyModelPronunciation(snapshot);
        ApplyModelWordForms(Query.Length > 0 ? Query : Queries.LookupKey(firstWord ?? ""), snapshot);
        ApplyModelEtymology(snapshot);
        var query = Query.Length > 0 ? Query : firstWord ?? evidence?.Word ?? "";
        var uncovered = SpecialistReferences.Uncovered(query, snapshot.Groups, evidence);
        ApplyQuickRead(snapshot, uncovered, finalized);
        Answers = BuildAnswers(snapshot, evidence, uncovered);
    }

    public static AnswersView BuildAnswers(LookupSnapshot snapshot, OnlineEntry? evidence, SpecialistReference[] uncovered)
    {
        if (snapshot.Groups.Length == 0 && uncovered.Length == 0 && snapshot.Answers.All(a => a.Phase == ModelPhase.Offline))
            return new(true, [], [], [], [], "", OriginalsMode.Keep);
        var references = uncovered.Select((brief, i) => new ReferenceView("reference:" + brief.Word,
            $"{i + 1:00}  " + LexicalLabels.Usage(brief.Domain) + " · 资料摘要", brief.Expansion, brief.Chinese, brief.English,
            "LeXi 根据原始资料整理；模型尚未提供对应释义。", brief.SourceName, brief.SourceUrl)).ToArray();
        var senses = snapshot.Groups.Select((group, i) => Sense(group.Representative, $"{i + uncovered.Length + 1:00}", evidence,
            group.Variants.Where(v => v.Id != group.Representative.Id).ToArray(), group.IncludesUsageVariants)).ToArray();
        var notices = new List<NoticeView>();
        foreach (var answer in snapshot.Answers)
        {
            if (answer.Phase == ModelPhase.Failed) notices.Add(new(answer.Model.Name + "：" + answer.Error + (answer.Cached ? "（已保留缓存）" : ""), true));
            else if (answer.Phase == ModelPhase.Complete && answer.Entry?.Found == false)
                notices.Add(new(answer.Model.Name + "：" + (string.IsNullOrWhiteSpace(answer.Entry.Note) ? "未返回可靠释义" : answer.Entry.Note), false));
        }
        var notes = snapshot.Answers.Where(a => a.Entry?.Found == true && !string.IsNullOrWhiteSpace(a.Entry.Note))
            .Select(a => a.Model.Name + "：" + a.Entry!.Note).ToArray();
        // Original dictionary text collapses once definitions exist and reopens when none arrive.
        var originals = snapshot.Groups.Length > 0 ? OriginalsMode.Collapse : !snapshot.Provisional ? OriginalsMode.Expand : OriginalsMode.Keep;
        return new(false, references, senses, notices.ToArray(), notes, snapshot.MergeFailed ? snapshot.MergeNote : "", originals);
    }

    // Content identity survives streaming, reordered senses and the final merge.
    public static string SenseKey(SenseCandidate candidate) => Cache.Hash(JsonSerializer.Serialize(
        new SenseIdentity(candidate.Model, candidate.Sense.Chinese, candidate.Sense.English), Json.Options));

    private static string Provenance(SenseCandidate candidate) => candidate.Provisional ? "释义仍在生成，来源信息尚未完整。" :
        candidate.EvidenceStatus == "source_based" ? "模型参考下列资料整理；释义与例句由模型生成。" :
        "这条解释由模型补充，现有词典原文不足以核对全部内容；这不表示释义错误。";

    public static SenseView Sense(SenseCandidate candidate, string prefix, OnlineEntry? evidence, SenseCandidate[]? alternatives = null, bool usageVariants = false)
    {
        var sense = candidate.Sense;
        var usage = LexicalLabels.Usage(sense.Register);
        var heading = new SenseHeadingView(prefix, LexicalLabels.PartOfSpeech(sense.PartOfSpeech), LexicalLabels.Form(sense.PartOfSpeech), usage,
            LexicalCoverage.Abbreviation(sense) && !usage.Contains("缩写"), candidate.Provisional, sense.PartOfSpeech + " · " + sense.Register);
        var numbers = candidate.Provisional || evidence == null ? [] : new[] { candidate }.Concat(alternatives ?? [])
            .Where(v => !v.Provisional).SelectMany(v => v.Sense.Evidence).Distinct().Where(n => n >= 1 && n <= evidence.Senses.Length).ToArray();
        var cited = numbers.Select(n => (Number: n, Sense: evidence!.Senses[n - 1])).ToArray();
        var briefs = cited.SelectMany(c => SpecialistReferences.Cited(c.Sense)).ToArray();
        alternatives ??= [];
        return new(SenseKey(candidate), heading,
            LexicalCoverage.Abbreviation(sense) && sense.Expansion.Length > 0 ? sense.Expansion : "",
            Lexi.Core.QuickRead.ChineseDefinition(sense), sense.English, sense.Example ?? "", sense.ExampleChinese ?? "",
            candidate.Model.Name + " · " + Provenance(candidate), "原始用法标签：" + sense.PartOfSpeech + " · " + sense.Register,
            cited.Select(c => new CitationView(c.Number, $"[{c.Number}] {c.Sense.SourceName}", c.Sense.Definition,
                string.IsNullOrEmpty(c.Sense.SourceUrl) ? evidence!.Url : c.Sense.SourceUrl)).ToArray(),
            briefs.Select(b => new BriefView(b.SourceName + " · LeXi 参考摘要", b.Chinese, b.English)).ToArray(),
            briefs.Select(b => "reference:" + b.Word).ToArray(),
            numbers.Length == 0 && !candidate.Provisional ? "当前解释来自模型知识，未附可直接核对的资料。" : "",
            alternatives.Length > 0 ? "资料链接包含同义项的其他表述所引用的内容。" : "",
            numbers.Length > 0 ? $"参考资料 · {numbers.Length}" : "参考资料",
            alternatives.Select(v => Sense(v, v.Model.Name, evidence)).ToArray(),
            alternatives.Length > 0 ? $"{(usageVariants ? "用法细分" : "其他表述")} · {alternatives.Length}" : "");
    }

    // ---- Translation ----
    public void ShowTranslation(TranslationEntry entry, bool cached, bool streaming, string model) =>
        Translation = new(model + (cached ? " · 缓存" : streaming ? " · 生成中" : ""), streaming ? "生成中的内容尚未完成" : "AI 翻译",
            entry.Paragraphs, entry.Note ?? "", !streaming);
    public void ShowTranslationError(string message) => Translation = new("", "", [], "", false, message);
}

internal sealed record EtymologyIdentity(EtymologyExcerpt[] SourceEtymologies, ModelEtymology[] Models);
internal sealed record ModelEtymology(LookupModel Key, string Value);
internal sealed record SenseIdentity(LookupModel Model, string Chinese, string English);
