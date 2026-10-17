using System.Text.Json;
using Lexi.Core;

internal static class OverviewTimingChecks
{
    public static void Run(Action<bool, string> check)
    {
        var primary = new LookupModel("claude", "fixture");
        var secondary = new LookupModel("codex", "fixture");
        var peripheral = new AiSense("noun", "A specialized practitioner.", "从事相关技术的专业人员", "information security", "", "", [], Gloss: "相关技术人员");
        var central = peripheral with { English="A central concept.", Chinese="该词通常表示的概念", Register="", Gloss="核心概念" };
        var alternate = central with { Chinese="另一种核心概念表述", Gloss="核心概念的另一种说法" };
        ModelAnswer[] answers = [new(primary, ModelPhase.Complete, new("fixture", true, [peripheral,central], "", "unverified")),
            new(secondary, ModelPhase.Complete, new("fixture", true, [alternate], "", "unverified"))];
        var request = SenseMerging.Request("fixture", answers);
        var plan = new SenseMergePlan(request.InputHash, request.Pairs.Select(p => new SenseVerdict(p.A, p.B,
            p.A=="m0s1" && p.B=="m1s0" ? "equivalent" : "different", 1)).ToArray(), ["m1s0"]);
        var parsed = SenseMerging.Parse(JsonSerializer.Serialize(plan, Json.Flexible), request);
        var groups = SenseMerging.Group(answers, parsed);
        var summary = QuickRead.Create(groups, [], []);
        check(groups.Length==2 && groups[0].Representative.Sense==central && groups[0].Variants.Length==2,
            "a selected alternative moves its entire core-meaning group ahead of domain-prioritized meanings");
        check(summary is [{Text:"核心概念",Core:true}] && groups.Sum(g=>g.Variants.Length)==3,
            "one core meaning stays concise without deleting peripheral definitions or padding the summary");
        check(QuickRead.Create(SenseMerging.Group(answers,plan with {Overview=["m0s1","m1s0"]}),[],[]).Length==1,
            "two selected paraphrases in the same group cannot duplicate the overview");
        check(QuickRead.Create(SenseMerging.Group(answers,plan with {Overview=["m0s0","m0s1"]}),[],[]).Select(m=>m.Text).SequenceEqual(new[]{peripheral.Gloss,central.Gloss}),
            "two distinct selected core meanings retain the judge's explicit importance order");
        check(QuickRead.Create(SenseMerging.Group(answers),[],[]).All(m=>!m.Core),
            "legacy multi-sense output is an excerpt until core selection completes");
        var fresh = new[] { answers[0] with { Entry=answers[0].Entry! with { Senses=[peripheral with {CoreMeaning=false},central with {CoreMeaning=true}] } } };
        check(QuickRead.Create(SenseMerging.Group(fresh),[],[]) is [{Text:"核心概念",Core:true}],
            "completed model core metadata works during preview, disabled merging and judge failure without another request");
        var declines = SenseMerging.Group(fresh,new("fixture",[],[]));
        check(QuickRead.Create(declines,[],[]).All(m=>!m.Core), "an explicit uncertain overview overrides model guesses and stays an excerpt");
        check(QuickRead.Create(SenseMerging.Group([fresh[0] with { Phase=ModelPhase.Streaming }]),[],[]).Length==0,
            "provisional core flags never enter the overview");
        foreach(var invalid in new[]{new[]{"unknown"},new[]{"m0s1","m0s1"},new[]{"m0s0","m0s1","m1s0"}})
        {
            try { SenseMerging.Parse(JsonSerializer.Serialize(plan with {Overview=invalid},Json.Flexible),request); check(false,"bad overview accepted"); }
            catch(InvalidDataException) { check(true,"unknown, repeated or oversized overview IDs are rejected"); }
        }
        var parsedEntry = CodexDictionary.ParseAndValidate(JsonSerializer.Serialize(fresh[0].Entry,Json.Flexible),"fixture",null);
        check(parsedEntry.Senses.Count(s=>s.CoreMeaning==true)==1 && parsedEntry.Senses.Count(s=>s.CoreMeaning==false)==1,
            "core selection metadata survives validation and definition cache serialization");
        var general = central with { English="A general structured data representation.", Chinese="通用结构化数据表示", Gloss="通用数据表示", Register="software engineering", CoreMeaning=true };
        var application = general with { English="The same data representation used in token metadata.", Chinese="用于代币元数据的数据表示", Gloss="代币数据表示", Register="blockchain", CoreMeaning=false };
        var separate = general with { English="An independently established mechanism with a different referent.", Chinese="具有不同指称的独立机制", Gloss="独立机制", Register="cryptography", CoreMeaning=false };
        var scoped = new[] { new ModelAnswer(primary,ModelPhase.Complete,new("fixture",true,[application,general,separate],"","unverified")) };
        var scopedRequest = SenseMerging.Request("fixture",scoped);
        var scopedPlan = new SenseMergePlan(scopedRequest.InputHash, scopedRequest.Pairs.Select(p=>new SenseVerdict(p.A,p.B,
            p.A=="m0s0" && p.B=="m0s1" ? "b_covers_a" : "different",1,p.A=="m0s0" && p.B=="m0s1" ? "m0s1" : "")).ToArray(),["m0s1"]);
        var scopedGroups=SenseMerging.Group(scoped,scopedPlan);
        check(scopedGroups.Length==2 && scopedGroups[0].Representative.Sense==general && scopedGroups[0].Variants.Any(v=>v.Sense==application) && scopedGroups[1].Representative.Sense==separate,
            "general definitions head their application variants while independently established technical meanings survive");
        check(QuickRead.Create(scopedGroups,[],[]) is [{Text:"通用数据表示",Core:true}],
            "overview retains the general meaning rather than its favored-industry application");
        check(CodexDictionary.PrioritizeCurrent([application,general,separate]).SequenceEqual(new[]{general,application,separate}) &&
            CodexDictionary.PrioritizeCurrent([application with{Register="ordinary"},general,separate])[0]==general,
            "core semantics outrank both generation order and domain labels");
        check(LexicalLabels.Usage("software engineering, data interchange")=="软件工程 · 数据交换", "native technical fields have readable labels without being replaced by crypto tags");
        var clock = new ManualClock();
        var timing = new LookupTiming(clock);
        clock.Advance(100); timing.MarkFirst();
        clock.Advance(1900); timing.MarkFirst(); // later provider cannot reset the first result
        clock.Advance(400); // merging/pronunciation may finish after the last definition
        var footer = timing.Finish("查询完成");
        check(footer=="首条释义 0.1 秒 · 总耗时 2.4 秒 · 查询完成" && timing.FirstResult==TimeSpan.FromMilliseconds(100),
            "total time includes late stages while first visible result latency remains independent");
        clock.Advance(1000); timing.MarkFirst();
        check(timing.Finish("查询完成")==footer, "completed query timing freezes and ignores late updates");
        var cached = new LookupTiming(clock); cached.MarkFirst();
        check(cached.Finish("离线缓存").StartsWith("首条释义 0.0 秒 · 总耗时 0.0 秒"), "immediate offline and cached results report both durations");
        var missing = new LookupTiming(clock); clock.Advance(250);
        check(missing.Finish("没有结果")=="总耗时 0.3 秒 · 没有结果", "terminal empty and failed lookups never invent a first result");
        var translated = new LookupTiming(clock); clock.Advance(300); translated.MarkFirst(); clock.Advance(600);
        check(translated.Finish("翻译完成","首段译文")=="首段译文 0.3 秒 · 总耗时 0.9 秒 · 翻译完成",
            "translation shares full-query timing with a clear first-paragraph label");
        check(timing.Total==TimeSpan.FromMilliseconds(2400), "a new query cannot change a previous query's measurements");
    }
    private sealed class ManualClock : TimeProvider
    {
        private long ticks;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => ticks;
        public void Advance(long milliseconds) => ticks += milliseconds;
    }
}
