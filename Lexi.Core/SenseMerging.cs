using System.Text.Json;

namespace Lexi.Core;

public sealed record SenseCandidate(string Id, LookupModel Model, AiSense Sense, string EvidenceStatus, bool Provisional);
public sealed record SenseGroup(SenseCandidate[] Variants, bool IncludesUsageVariants = false, int? OverviewRank = null, bool OverviewAssessed = false)
{
    // Prefer the wording covering the group's usages; equal coverage preserves
    // model order. Agreement never upgrades the original provenance.
    public SenseCandidate Representative => Variants[0];
}
public sealed record SensePair(string A, string B);
public sealed record SenseVerdict(string A, string B, string Relation, double Confidence, string Preferred = "");
public sealed record SenseMergePlan(string InputHash, SenseVerdict[] Pairs, string[]? Overview = null);
public sealed record SenseMergeRequest(string Query, SenseCandidate[] Candidates, SensePair[] Pairs)
{
    // Presentation metadata cannot influence the independent core-meaning judgment.
    public string InputHash => Cache.Hash(JsonSerializer.Serialize(new MergeIdentity(5, Query,
        Candidates.Select(c => new MergeCandidateIdentity(c.Id, c.Model,
            new MergeSenseIdentity(c.Sense.PartOfSpeech, c.Sense.English, c.Sense.Chinese, c.Sense.Register,
                c.Sense.Example, c.Sense.ExampleChinese, c.Sense.Evidence, c.Sense.LexicalForm, c.Sense.Expansion),
            c.EvidenceStatus, c.Provisional)).ToArray(), Pairs), Json.Options));
    public string Prompt => JsonSerializer.Serialize(new MergePrompt("compare_senses", InputHash, Query,
        Candidates.Select(c => new MergePromptSense(c.Id, c.Sense.PartOfSpeech, c.Sense.English, c.Sense.Chinese, c.Sense.Register,
            c.Sense.Example, c.Sense.LexicalForm, c.Sense.Expansion)).ToArray(), Pairs), Json.Options);
}

internal sealed record MergeIdentity(int Version, string Query, MergeCandidateIdentity[] Candidates, SensePair[] Pairs);
internal sealed record MergeCandidateIdentity(string Id, LookupModel Model, MergeSenseIdentity Sense, string EvidenceStatus, bool Provisional);
internal sealed record MergeSenseIdentity(string PartOfSpeech, string English, string Chinese, string Register,
    string Example, string ExampleChinese, int[] Evidence, string LexicalForm, string Expansion);
internal sealed record MergePrompt(string Task, string InputHash, string Query, MergePromptSense[] Senses, SensePair[] Pairs);
internal sealed record MergePromptSense(string Id, string PartOfSpeech, string English, string Chinese, string Register,
    string Example, string LexicalForm, string Expansion);

public static class SenseMerging
{
    public const string CacheKind = "sense-merges-v5";
    public static SenseCandidate[] Candidates(ModelAnswer[] answers) => answers.SelectMany((answer, modelIndex) =>
        (answer.Entry?.Senses ?? []).Select((sense, senseIndex) => new SenseCandidate($"m{modelIndex}s{senseIndex}",
            answer.Model, sense, answer.Entry!.EvidenceStatus, answer.Phase == ModelPhase.Streaming))).ToArray();

    public static bool Exact(SenseCandidate a, SenseCandidate b) =>
        LexicalCoverage.Compatible(a.Sense, b.Sense) && Queries.Normalize(a.Sense.English) == Queries.Normalize(b.Sense.English) &&
        Queries.Normalize(a.Sense.Chinese) == Queries.Normalize(b.Sense.Chinese) &&
        Queries.Normalize(a.Sense.PartOfSpeech) == Queries.Normalize(b.Sense.PartOfSpeech) &&
        Queries.Normalize(a.Sense.Register) == Queries.Normalize(b.Sense.Register);

    public static SenseMergeRequest Request(string query, ModelAnswer[] answers)
    {
        var candidates = Candidates(answers).Where(c => !c.Provisional).ToArray();
        var pairs = new List<SensePair>();
        for (int i = 0; i < candidates.Length; i++)
            for (int j = i + 1; j < candidates.Length; j++)
                if (!Exact(candidates[i], candidates[j]))
                    pairs.Add(new(candidates[i].Id, candidates[j].Id));
        return new(query, candidates, pairs.ToArray());
    }

    public static SenseMergePlan Parse(string json, SenseMergeRequest request)
    {
        var plan = JsonSerializer.Deserialize<SenseMergePlan>(StreamingEntry.Unwrap(json), Json.Options);
        if (plan == null || plan.InputHash != request.InputHash || plan.Pairs == null || plan.Pairs.Length != request.Pairs.Length)
            throw new InvalidDataException("义项判断结果不完整或与当前查询不匹配。");
        var expected = request.Pairs.Select(p => PairKey(p.A, p.B)).ToHashSet();
        foreach (var pair in plan.Pairs)
            if (pair == null || !expected.Remove(PairKey(pair.A, pair.B)) ||
                pair.Relation is not ("equivalent" or "same_usage" or "a_covers_b" or "b_covers_a" or "different" or "uncertain") ||
                pair.Preferred != "" && pair.Preferred != pair.A && pair.Preferred != pair.B ||
                !double.IsFinite(pair.Confidence) || pair.Confidence < 0 || pair.Confidence > 1)
                throw new InvalidDataException("义项判断包含无效或重复的比较结果。");
        if (plan.Overview is { } overview && (overview.Length > 2 ||
            overview.Distinct().Count() != overview.Length ||
            overview.Any(id => !request.Candidates.Any(c => c.Id == id))))
            throw new InvalidDataException("核心义项选择包含无效或重复的标识。");
        return plan;
    }

    public static SenseGroup[] Group(ModelAnswer[] answers, SenseMergePlan? plan = null)
    {
        var candidates = Candidates(answers);
        var verdicts = (plan?.Pairs ?? []).Where(p => p.Confidence >= 0.95).ToArray();
        var approved = verdicts.Where(p => p.Relation is "equivalent" or "same_usage" or "a_covers_b" or "b_covers_a")
            .Select(p => PairKey(p.A, p.B)).ToHashSet();
        bool Same(SenseCandidate a, SenseCandidate b) => LexicalCoverage.Compatible(a.Sense, b.Sense) && (Exact(a, b) ||
            !a.Provisional && !b.Provisional && approved.Contains(PairKey(a.Id, b.Id)));
        var groups = new List<List<SenseCandidate>>();
        foreach (var candidate in candidates)
        {
            // Complete-link grouping avoids A=B, B=C incorrectly hiding A!=C.
            var group = groups.FirstOrDefault(g => g.All(existing => Same(existing, candidate)));
            if (group == null) groups.Add([candidate]); else group.Add(candidate);
        }
        return groups.Select(g =>
        {
            int Coverage(SenseCandidate c) => verdicts.Count(p =>
                (p.A == c.Id && p.Relation == "a_covers_b" && g.Any(v => v.Id == p.B)) ||
                (p.B == c.Id && p.Relation == "b_covers_a" && g.Any(v => v.Id == p.A)) ||
                (p.Preferred == c.Id && p.Relation == "same_usage" && g.Any(v => v.Id == p.A) && g.Any(v => v.Id == p.B)));
            var ids = g.Select(c => c.Id).ToHashSet();
            return new SenseGroup(g.OrderByDescending(Coverage).ToArray(), verdicts.Any(p => ids.Contains(p.A) && ids.Contains(p.B) &&
                p.Relation is "same_usage" or "a_covers_b" or "b_covers_a"),
                plan?.Overview?.Select((id, rank) => (id, rank)).Where(x => ids.Contains(x.id)).Select(x => (int?)x.rank).FirstOrDefault(),
                plan?.Overview != null);
        }).OrderBy(g => g.OverviewAssessed ? g.OverviewRank ?? int.MaxValue :
                g.Variants.Any(v => !v.Provisional && v.Sense.CoreMeaning == true) ? 0 : int.MaxValue).ToArray();
    }
    private static string PairKey(string a, string b) => string.CompareOrdinal(a, b) < 0 ? a + ":" + b : b + ":" + a;
    public static bool IsCurrent(string? label) => new[] { "slang", "internet", "网络", "俚语" }
        .Any(word => (label ?? "").Contains(word, StringComparison.OrdinalIgnoreCase));
}
