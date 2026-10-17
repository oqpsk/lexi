using System.Net;
using System.Text.Json;
using Lexi.Core;

internal static class LexicalChecks
{
    public static async Task Run()
    {
        int count = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception("FAIL: " + label); Console.WriteLine("PASS: " + label); count++; }
        Check(Queries.LookupKey(" ＴＥＥ  ") == "TEE" && Queries.Normalize("TEE") == "tee", "lookup identity preserves uppercase while offline keys remain compatible");
        Check(Queries.AbbreviationCandidate("TEE") && Queries.AbbreviationCandidate("ZK") && !Queries.AbbreviationCandidate("tee") && !Queries.AbbreviationCandidate("The ship"), "uppercase candidates never consume lowercase words or sentences");
        var settings = new AppSettings(); var model = LookupModel.Primary(settings);
        Check(model.CacheKey("TEE",settings) != model.CacheKey("tee",settings), "abbreviation and word caches cannot contaminate one another");
        Check(Pronunciation.Format(@"n\\\:f") == "" && Pronunciation.Format(@"\u025c") == "" && Pronunciation.Format("/a/<b>") == "", "damaged legacy transcriptions and escaped codepoints stay out of the IPA display");
        Check(Pronunciation.Format("'miksə") == "/ˈmiksə/" && Pronunciation.Format("[nɜ:f]") == "/nɜːf/" && Pronunciation.Format("/nɜːf/") == "/nɜːf/", "local and sourced IPA use one pair of slash delimiters and standard stress and length marks");
        var html = "<h2 id=\"English\">English</h2><h3 id=\"Etymology_1\">Etymology 1</h3><p>From a <i>brand name</i>.<sup>1</sup></p><h3>Pronunciation</h3><span class=\"IPA-label\">UK</span><span class=\"IPA\">/nɜːf/</span><h3>Noun</h3><p>Unrelated definition.</p><h2 id=\"French\">French</h2><h3>Etymology</h3><p>French origin.</p><span class=\"IPA\">/fʁɑ̃/</span>";
        Check(Pronunciation.ParseEnglishHtml(html).SequenceEqual(new[] { "/nɜːf/" }), "expanded IPA templates exclude labels and other languages");
        var origins = Pronunciation.ParseEtymologies(html,"https://en.wiktionary.org/wiki/nerf#English");
        Check(origins.Length == 1 && origins[0].Text == "From a brand name." && origins[0].SourceUrl.EndsWith("#English"), "etymology extraction preserves attribution and excludes following definitions and other languages");
        Check(Pronunciation.ParseEnglishHtml("<h2><span id=\"English\">English</span></h2><span class=\"IPA\">/ɡəʊ/</span>").Single() == "/ɡəʊ/", "older Wiktionary heading structure still supplies IPA");
        using (var web = new OnlineDictionary(new Handler((request, _) =>
        {
            var upper = request.RequestUri!.AbsolutePath.EndsWith("/TEE");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new {
                en = new[] { new { language="English", partOfSpeech="Noun", definitions=new[] { new { definition=upper ? "Initialism of trusted execution environment." : "A golf peg." } } } }
            })) });
        })))
        {
            var result = await web.LookupAsync("TEE", CancellationToken.None);
            Check(result?.Word == "TEE" && result.Senses.Length == 2 && result.Senses[0].Headword == "TEE" && result.Senses[1].Headword == "tee" && result.Senses[0].SourceUrl != result.Senses[1].SourceUrl,
                "exact-case and lowercase pages retain separate spellings and source links");
        }
        using (var redirected = new Pronunciation(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { parse = new { title="tee",text=html } })) }))))
            Check(await redirected.LookupAsync("TEE",CancellationToken.None) == null, "redirected lowercase pages never lend pronunciation to uppercase queries");
        using (var capitalized = new Pronunciation(new Handler((request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new StringContent(request.RequestUri!.Query.Contains("page=Nerf") ? "{}" : JsonSerializer.Serialize(new { parse = new { title="nerf",text=html } }))
        }))))
        {
            var result = await capitalized.LookupAsync("Nerf",CancellationToken.None);
            Check(result?.Word == "Nerf" && result.Ipa.Single() == "/nɜːf/" && result.SourceUrl.Contains("/nerf#"),
                "missing title-case pages may use lowercase metadata without changing their cache identity or attribution");
        }
        var reference = SpecialistReferences.Lookup("TEE").Single();
        Check(reference.Expansion == "Trusted Execution Environment" && SpecialistReferences.Lookup("trusted execution environment").Single() == reference,
            "the acronym and full form resolve to the same attributed technical reference");
        foreach (var query in new[] { "mixer", "nonce", "TEE", "enclave", "MPC", "MEV", "bridge", "rollup", "oracle", "gas", "staking", "slashing" })
            Check(DictionaryEvidence.Combine(query,[],null)?.Senses.Any(s => LexicalCoverage.Specialist(s.PartOfSpeech) && s.SourceUrl.StartsWith("https://")) == true, query + " has usable offline technical evidence");
        Check(SpecialistReferences.Lookup("teapot").Length == 0 && CodexDictionary.DefineRequest("bundler",null).Contains("cryptography"), "unlisted terms use general domain coverage without fabricated reference matches");
        var ordinary = new AiSense("noun","A peg.","球钉","","","",[]);
        var abbreviation = ordinary with { LexicalForm="abbreviation", Expansion="Trusted Execution Environment", Register="information security" };
        var otherAbbreviation = abbreviation with { Expansion="Transesophageal Echocardiography", Register="medical" };
        var answers = new[] { new ModelAnswer(model,ModelPhase.Complete,new("TEE",true,[ordinary,abbreviation,otherAbbreviation],"","unverified")) };
        var request = SenseMerging.Request("TEE",answers);
        var falseMerge = new SenseMergePlan(request.InputHash,request.Pairs.Select(p => new SenseVerdict(p.A,p.B,"equivalent",1)).ToArray());
        Check(SenseMerging.Group(answers,falseMerge).Length == 3, "even a mistaken judge cannot merge distinct expansions or their ordinary homographs");
        var ordered = CodexDictionary.PrioritizeCurrent([ordinary, ordinary with { Register="slang" }, abbreviation]);
        Check(ordered[0] == ordinary && ordered[2] == abbreviation, "domain and slang labels do not override semantic generation order");
        Check(LexicalLabels.Usage("cryptography, blockchain") == "密码学 · 区块链", "domain tags remain specific and understandable");
        var inflected = new AiEntry("TEE",true,[abbreviation],"","unverified","",[new("tee","verb",[new("past",["teed"])])]);
        Check(ModelInflections.Sanitize("TEE",inflected)?.Length == 0, "abbreviations cannot inherit a lowercase verb paradigm");
        var parsed = CodexDictionary.ParseAndValidate(JsonSerializer.Serialize(inflected with { Etymology="首字母缩写。" },Json.Flexible),"TEE",null,true);
        Check(parsed.Etymology == "首字母缩写。" && parsed.Senses[0].Expansion == abbreviation.Expansion, "expanded lexical fields survive validated complete answers");
        var preview = StreamingEntry.Read(JsonSerializer.Serialize(parsed,Json.Flexible),"TEE");
        Check(preview?.Senses[0].LexicalForm == "abbreviation" && preview.Senses[0].Expansion == abbreviation.Expansion && preview.Etymology == "", "streaming retains abbreviation identity but waits for complete etymology");
        var etymologyEvidence = DictionaryEvidence.Combine("nerf", [], null, origins);
        Check(CodexDictionary.DefineRequest("nerf", etymologyEvidence).Contains("etymologyEvidence") &&
            CodexDictionary.DefineRequest("nerf", etymologyEvidence).Contains("brand name"), "source etymology joins the existing definition request without a separate model call");
        Check(CodexDictionary.EvidenceHash(etymologyEvidence) == CodexDictionary.EvidenceHash(etymologyEvidence! with { Etymologies = [] }),
            "late optional etymology does not invalidate already usable definition caches");
        try { CodexDictionary.ParseAndValidate(JsonSerializer.Serialize(inflected with { Word="tee" },Json.Flexible),"TEE",null); Check(false,"wrong-case acronym"); }
        catch (InvalidDataException) { Check(true,"a lowercase model response cannot replace an uppercase acronym entry"); }
        CheckQuickRead(Check);
        OverviewTimingChecks.Run(Check);
        Console.WriteLine($"{count} lexical coverage checks passed");
    }
    private static void CheckQuickRead(Action<bool,string> check)
    {
        var brief = SpecialistReferences.Lookup("TEE").Single();
        var evidence = DictionaryEvidence.Combine("TEE",[],null)!;
        var model = new LookupModel("codex","fixture");
        var sense = new AiSense("noun",brief.English,brief.Chinese,brief.Domain,"","",[1],"abbreviation",brief.Expansion,"可信执行环境");
        var candidate = new SenseCandidate("m0s0",model,sense,"source_based",false);
        SenseGroup[] groups = [new([candidate])];
        check(SpecialistReferences.Uncovered("TEE",groups,evidence).Length==0, "completed cited professional senses replace the corresponding fallback");
        check(SpecialistReferences.Uncovered("TEE",[new([candidate with {Provisional=true}])],evidence).Length==1, "partial model output cannot discard sourced fallback meanings");
        check(!SpecialistReferences.Covered(brief,[new([candidate with { Sense=sense with { Expansion="Transesophageal Echocardiography" } }])],evidence), "an unrelated abbreviation cannot consume a brief even with a mistaken citation");
        check(!SpecialistReferences.Covered(brief,[new([candidate with { Sense=sense with { Evidence=[] } }])],evidence), "unsupported model content does not silently discard a source brief");
        var alternate = candidate with { Id="m1s0", Sense=sense with {Evidence=[]} };
        check(SpecialistReferences.Covered(brief,[new([alternate,candidate])],evidence), "merged alternatives retain a reference even when the representative has no citation");
        check(QuickRead.Create(groups,[],[]).Single().Text=="可信执行环境", "overview reuses the completed sense gloss");
        check(QuickRead.Create([new([candidate with {Provisional=true}])],[],[]).Length==0, "streaming fragments never enter the overview");
        check(QuickRead.Create([new([candidate with {Sense=sense with {Gloss=""}}])],[],[]).Single().Text==brief.Chinese.TrimEnd('。'), "legacy caches retain a complete-text overview fallback");
        check(QuickRead.ValidGloss(new string('词',33))=="" && QuickRead.ValidGloss("English only")=="" && QuickRead.ValidGloss("词\n语")=="", "invalid glosses are ignored without failing valid definitions");
        check(QuickRead.Excerpt(new string('词',70)+"不能省略的限制。")=="", "long definitions are never clipped mid-clause to fabricate a short meaning");
        var other = candidate with { Id="m0s1",Sense=sense with {Gloss="经食道超声心动图",Expansion="Transesophageal Echocardiography"} };
        check(QuickRead.Create([groups[0],new([other])],[],[]).Length==2, "polysemous overviews can preserve two different full meanings");
        var before = new SenseMergeRequest("TEE",[candidate with {Sense=sense with {Gloss=""}}],[]);
        var after = new SenseMergeRequest("TEE",[candidate],[]);
        check(before.InputHash==after.InputHash, "presentation-only glosses do not invalidate existing sense-judge caches");
        var parsed=CodexDictionary.ParseAndValidate(JsonSerializer.Serialize(new AiEntry("TEE",true,[sense],"","source_based"),Json.Flexible),"TEE",evidence);
        check(parsed.Senses[0].Gloss==sense.Gloss, "short glosses survive provider validation and cache round trips");
    }
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token) => send(request,token); }
}
