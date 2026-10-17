using System.Text.Json;
using Lexi.Core;

internal static class ParallelChecks
{
    public static async Task Run(string root)
    {
        await ModelInflectionChecks.Run(root);
        await HistoryGlossChecks.Run(root);
        NavigationChecks.Run(root);
        await SavedQueryChecks.Run(root);
        var work = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        int count = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); count++; }
        var settings = new AppSettings { Provider = "claude", Model = "haiku" };
        var primary = LookupModel.Primary(settings); var second = LookupModel.Secondary(settings);
        Check(LookupModel.Selected(settings).SequenceEqual(new[] { primary, new LookupModel("codex", "gpt-6-luna") }), "legacy settings gain a complementary second model");
        var clone = settings.ForModel(second); clone.Model = "custom";
        Check(settings.Model == "haiku" && clone.Provider == "codex", "model requests use isolated settings");
        Check(primary.CacheKey("chad", settings) != second.CacheKey("chad", settings), "query caches are separate per provider and model");
        var duplicate = settings.ForModel(primary); duplicate.SecondaryProvider = primary.Provider; duplicate.SecondaryModel = primary.Model;
        Check(LookupModel.Selected(duplicate).Length == 1, "duplicate model configurations never run twice");
        var single = settings.ForModel(primary); single.ParallelLookup = false;
        Check(LookupModel.Selected(single).Length == 1 && Translation.Key("text", "zh", single) == Translation.Key("text", "zh", settings), "parallel lookup does not change translation model or cache");

        AiSense Sense(string english, string chinese, string register = "slang", int[]? evidence = null) => new("noun", english, chinese, register, "An example.", "一个例句。", evidence ?? []);
        var slang = Sense("a confident, attractive male archetype", "自信、有魅力的男性典型");
        var reworded = Sense("an archetypal confident and attractive man", "自信且富有魅力的男性典型");
        var country = Sense("a country in Africa", "非洲的一个国家", "geography");
        AiEntry Entry(params AiSense[] senses) => new("chad", true, senses, "", "unverified");
        var answers = new[] { new ModelAnswer(primary, ModelPhase.Complete, Entry(slang, country)), new ModelAnswer(second, ModelPhase.Complete, Entry(reworded)) };
        var request = SenseMerging.Request("chad", answers);
        var plan = new SenseMergePlan(request.InputHash, request.Pairs.Select(p => new SenseVerdict(p.A, p.B, p.A == "m0s0" && p.B == "m1s0" ? "equivalent" : "different", 0.99)).ToArray());
        var groups = SenseMerging.Group(answers, SenseMerging.Parse(JsonSerializer.Serialize(plan, Json.Flexible), request));
        Check(groups.Length == 2 && groups[0].Variants.Length == 2 && groups[1].Representative.Sense == country, "equivalent slang is grouped while the country meaning remains distinct");
        Check(groups[0].Variants.Any(v => v.Sense == reworded) && groups[0].Representative.Sense == slang, "grouping keeps original definitions and alternative wording");
        Check(groups[0].Representative.EvidenceStatus == "unverified", "model agreement never upgrades provenance");
        var citedSecondary = new[] { answers[0], answers[1] with { Entry = Entry(reworded) with { EvidenceStatus = "source_based" } } };
        Check(SenseMerging.Group(citedSecondary, plan)[0].Representative.Sense == slang,
            "source labels do not override the user's preferred primary wording");
        var differentlyLabeled = new[] { answers[0] with { Entry = Entry(country, slang with { Register = "informal" }) }, answers[1] };
        var labelPlan = new SenseMergePlan("unused", [new("m0s1", "m1s0", "equivalent", 0.99)]);
        Check(SenseMerging.Group(differentlyLabeled, labelPlan)[0].Representative.Sense == country && SenseMerging.Group(differentlyLabeled, labelPlan)[1].Variants.Length == 2, "labels do not promote secondary variants ahead of semantic order");
        var uncertain = plan with { Pairs = plan.Pairs.Select(p => p with { Relation = "uncertain" }).ToArray() };
        Check(SenseMerging.Group(answers, uncertain).Length == 3, "uncertain meanings stay visible");
        var lowConfidence = plan with { Pairs = plan.Pairs.Select(p => p with { Confidence = 0.7 }).ToArray() };
        Check(SenseMerging.Group(answers, lowConfidence).Length == 3, "low-confidence equivalence cannot hide a sense");
        var bridgeAnswers = new[] { answers[0] with { Entry = Entry(slang) }, answers[1], new ModelAnswer(new("codex", "third"), ModelPhase.Complete, Entry(country)) };
        var bridge = new SenseMergePlan("unused", [new("m0s0", "m1s0", "equivalent", 0.99), new("m1s0", "m2s0", "equivalent", 0.99)]);
        Check(SenseMerging.Group(bridgeAnswers, bridge).Length == 2, "non-transitive comparisons cannot collapse conflicting meanings");
        var exact = new[] { answers[0] with { Entry = Entry(slang) }, answers[1] with { Entry = Entry(slang) } };
        Check(SenseMerging.Request("chad", exact).Pairs.Length == 0 && SenseMerging.Group(exact).Length == 1, "identical definitions need no judge request");
        var narrowA = Sense("an unpleasant person", "令人反感的人");
        var narrowB = Sense("a physically unappealing person", "外形难看的人");
        var broad = Sense("an unpleasant or physically unappealing person", "令人反感或外形难看的人");
        var facets = new[] { new ModelAnswer(primary, ModelPhase.Complete, Entry(narrowA, narrowB, country)), new ModelAnswer(second, ModelPhase.Complete, Entry(broad)) };
        var facetRequest = SenseMerging.Request("fixture", facets);
        Check(facetRequest.Pairs.Any(p => p.A == "m0s0" && p.B == "m0s1"), "the judge compares related senses within the same model");
        var facetPlan = new SenseMergePlan(facetRequest.InputHash, facetRequest.Pairs.Select(p => p.A == "m0s2" || p.B == "m0s2"
            ? new SenseVerdict(p.A, p.B, "different", 0.99)
            : new SenseVerdict(p.A, p.B, "same_usage", 0.99, p.B == "m1s0" ? p.B : p.A)).ToArray());
        var facetGroups = SenseMerging.Group(facets, SenseMerging.Parse(JsonSerializer.Serialize(facetPlan, Json.Flexible), facetRequest));
        Check(facetGroups.Length == 2 && facetGroups[0].Representative.Sense == broad && facetGroups[0].Variants.Length == 3 && facetGroups[0].IncludesUsageVariants,
            "related facets share the comprehensive original wording while distinct meanings remain separate");
        Check(facetGroups.Sum(g => g.Variants.Length) == facets.Sum(a => a.Entry!.Senses.Length), "usage grouping retains every original variant and its provenance");
        var singleFacets = facets.Take(1).ToArray();
        Check(SenseMerging.Request("fixture", singleFacets).Pairs.Length == 3, "single-model queries still receive sense comparison");
        Check(SenseMerging.CacheKind == "sense-merges-v5", "revised grouping never reuses legacy comparison caches");
        foreach (var bad in new[]
        {
            plan with { InputHash = "different query" }, plan with { Pairs = [] },
            plan with { Pairs = [plan.Pairs[0], plan.Pairs[0]] },
            plan with { Pairs = plan.Pairs.Select(p => p with { A = "unknown" }).ToArray() },
            plan with { Pairs = plan.Pairs.Select(p => p with { Confidence = 2 }).ToArray() }
        })
        {
            try { SenseMerging.Parse(JsonSerializer.Serialize(bad, Json.Flexible), request); Check(false, "malformed merge plan"); }
            catch (InvalidDataException) { Check(true, "incomplete, mismatched or invalid merge plan rejected"); }
        }
        var manyEvidence = new OnlineEntry("chad", Enumerable.Range(0, 15).Select(i => new Sense("noun", "old meaning " + i, Array.Empty<string>()))
            .Append(new("noun", "Internet slang: an attractive male archetype", [])).ToArray(), "https://example.org/chad", DateTimeOffset.UtcNow);
        var combined = DictionaryEvidence.Combine("chad", [], manyEvidence)!;
        Check(combined.Senses[0].Definition.Contains("Internet slang"), "modern evidence survives evidence truncation and is prioritized");
        using (var prompt = JsonDocument.Parse(CodexDictionary.DefineRequest("chad", combined).Split('\n').Last()))
            Check(prompt.RootElement.GetProperty("coverageEvidence")[0].GetInt32() == 1 && prompt.RootElement.GetProperty("maxSenses").GetInt32() == 6, "definition request explicitly requires modern sense coverage");

        var cache = new Cache(Path.Combine(work, "parallel"));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var slow = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstVisible = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int starts = 0, judgeCalls = 0;
        async Task<AiEntry> Lookup(LookupModel model, string query, OnlineEntry? evidence, AppSettings option, CancellationToken token, IProgress<AiEntry> progress)
        {
            if (Interlocked.Increment(ref starts) == 2) started.TrySetResult();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
            if (model == second) await slow.Task.WaitAsync(token);
            return model == primary ? Entry(slang, country) : Entry(reworded);
        }
        Task<SenseMergePlan> Judge(SenseMergeRequest input, AppSettings option, CancellationToken token)
        {
            judgeCalls++;
            return Task.FromResult(new SenseMergePlan(input.InputHash, input.Pairs.Select(p => new SenseVerdict(p.A, p.B, p.A == "m0s0" && p.B == "m1s0" ? "equivalent" : "different", 0.99)).ToArray(), ["m0s0"]));
        }
        var coordinator = new DictionaryCoordinator(cache, Lookup, Judge);
        var run = coordinator.LookupAsync("chad", null, settings, false, CancellationToken.None, new CallbackProgress<LookupSnapshot>(s =>
        { if (s.Answers.Any(a => a.Phase == ModelPhase.Complete) && s.Answers.Any(a => a.Phase == ModelPhase.Pending)) firstVisible.TrySetResult(); }));
        await firstVisible.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check(starts == 2 && !run.IsCompleted, "both providers start together and the fast result appears before the slow result");
        slow.TrySetResult(); var result = await run;
        Check(result.Groups.Length == 2 && judgeCalls == 1, "completed model results are judged once as a batch");
        _ = await coordinator.LookupAsync("chad", null, settings, false, CancellationToken.None);
        Check(starts == 2 && judgeCalls == 1, "repeat queries reuse both model caches and the merge plan");
        Check(coordinator.Cached("chad", null, settings).Groups.Length == 2, "offline cache view retains completed semantic grouping");
        Check(QuickRead.Create(coordinator.Cached("chad",null,settings).Groups,[],[]) is [{Core:true}],
            "legacy definitions gain cached core selection through the existing judge without extra provider calls");
        var changed = settings.ForModel(primary); changed.SecondaryProvider = "codex"; changed.SecondaryModel = "other-model";
        Check(coordinator.Cached("chad", null, changed).Answers[1].Entry == null, "changing a model cannot reuse another model's cached answer");
        Check(coordinator.Cached("chad", combined, settings).Groups.Length == 0, "changing dictionary evidence invalidates stale generated answers");
        _ = await coordinator.LookupAsync("chad", null, settings, true, CancellationToken.None);
        Check(starts == 4 && judgeCalls == 2, "refresh reruns both chosen models and their judge");

        var failure = new DictionaryCoordinator(new Cache(Path.Combine(work, "failed")), (m, q, e, s, t, p) => m == primary
            ? Task.FromResult(Entry(slang)) : Task.FromException<AiEntry>(new InvalidOperationException("synthetic provider failure")), Judge);
        var partial = await failure.LookupAsync("chad", null, settings, false, CancellationToken.None);
        Check(partial.Groups.Length == 1 && partial.Answers[1].Phase == ModelPhase.Failed && partial.Answers[0].Phase == ModelPhase.Complete, "one provider failure preserves the other's completed definitions");
        var judgeFailure = new DictionaryCoordinator(new Cache(Path.Combine(work, "judge-failed")), (m, q, e, s, t, p) => Task.FromResult(m == primary ? Entry(slang) : Entry(reworded)),
            (r, s, t) => Task.FromException<SenseMergePlan>(new TimeoutException("synthetic timeout")));
        var retained = await judgeFailure.LookupAsync("chad", null, settings, false, CancellationToken.None);
        Check(retained.Groups.Length == 2 && retained.MergeFailed && retained.MergeFailure==MergeFailureKind.Timeout &&
            retained.MergeNote.Contains("响应超时") && retained.MergeNote.Contains("保留全部"), "judge timeout preserves every original definition and reports an explicit incomplete merge");
        foreach (var (error, expectedFailure) in new (Exception, MergeFailureKind)[]
        {
            (new InvalidDataException("private response content"), MergeFailureKind.InvalidResponse),
            (new JsonException("private response content"), MergeFailureKind.InvalidResponse),
            (new ModelAccessException(ModelAccessFailure.SignInRequired), MergeFailureKind.SignInRequired),
            (new ModelAccessException(ModelAccessFailure.ClientMissing), MergeFailureKind.ClientUnavailable),
            (new ModelAccessException(ModelAccessFailure.Unavailable), MergeFailureKind.ServiceUnavailable),
            (new IOException("private local path"), MergeFailureKind.LocalAccess),
            (new InvalidOperationException("private provider content"), MergeFailureKind.ServiceUnavailable),
            (new OperationCanceledException("private timeout"), MergeFailureKind.Timeout)
        })
        {
            var failedJudge = new DictionaryCoordinator(new Cache(Path.Combine(work, "diagnostic-" + Guid.NewGuid().ToString("N"))),
                (m,q,e,s,t,p)=>Task.FromResult(m==primary?Entry(slang):Entry(reworded)),
                (r,s,t)=>Task.FromException<SenseMergePlan>(error));
            var preserved = await failedJudge.LookupAsync("chad",null,settings,false,CancellationToken.None);
            Check(preserved.MergeFailure==expectedFailure && preserved.MergeFailed && preserved.Groups.Length==2 &&
                preserved.Answers.All(a=>a.Phase==ModelPhase.Complete) && !preserved.MergeNote.Contains("private"),
                "merge failure keeps complete model answers and records only a safe category: " + expectedFailure);
            var reloaded=JsonSerializer.Deserialize<LookupSnapshot>(JsonSerializer.Serialize(preserved,Json.Flexible),Json.Flexible)!;
            Check(reloaded.MergeFailure==expectedFailure, "merge failure category survives saved-view serialization");
        }
        using var cancellation = new CancellationTokenSource();
        var previewed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelCache = new Cache(Path.Combine(work, "cancel"));
        int canceledProviders = 0;
        var cancelRun = new DictionaryCoordinator(cancelCache, async (m, q, e, s, t, p) =>
        {
            p.Report(Entry(slang)); previewed.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, t); }
            catch (OperationCanceledException) { Interlocked.Increment(ref canceledProviders); throw; }
            return Entry(slang);
        }, Judge).LookupAsync("chad", null, settings, false, cancellation.Token);
        await previewed.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancellation.Cancel();
        try { await cancelRun; Check(false, "cancellation propagates"); } catch (OperationCanceledException) { Check(true, "cancellation propagates"); }
        Check(canceledProviders == 2, "query cancellation stops both providers");
        Check(LookupModel.Selected(settings).All(m => cancelCache.Read<AiEnvelope>(DictionaryCoordinator.CacheKind, m.CacheKey("chad", settings)) == null), "streaming previews are never cached after cancellation");
        int lateReports = 0; IProgress<AiEntry>? delayed = null;
        var late = new DictionaryCoordinator(new Cache(Path.Combine(work, "late")), (m, q, e, s, t, p) => { delayed = p; return Task.FromResult(Entry(slang)); }, Judge);
        await late.LookupAsync("chad", null, single, false, CancellationToken.None, new CallbackProgress<LookupSnapshot>(_ => lateReports++));
        var before = lateReports; delayed!.Report(Entry(country));
        Check(lateReports == before, "late streaming callback cannot overwrite a completed answer");
        Console.WriteLine($"{count} parallel lookup and sense-merging checks passed");
    }
}
