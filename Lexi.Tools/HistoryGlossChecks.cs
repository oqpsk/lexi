using System.Text.Json;
using Lexi.Core;

internal static class HistoryGlossChecks
{
    public static async Task Run(string root)
    {
        void Check(bool ok,string name) { if(!ok) throw new Exception("FAIL: "+name); Console.WriteLine("PASS: "+name); }
        var workspace = Path.Combine(root,"history-gloss-"+Guid.NewGuid().ToString("N"));
        var history = new QueryHistory(workspace);
        Check(history.Entries.Count==0 && history.Error=="", "new history starts empty without invented or reimported examples");
        var now=DateTimeOffset.UtcNow;
        Check(history.Record("  TEE  ",false,now) && history.Record("tee",false,now.AddSeconds(1)), "valid explicit queries are persisted");
        history.Record("TEE",false,now.AddSeconds(2));
        Check(history.Entries.Select(e=>e.Query).SequenceEqual(new[]{"TEE","tee"}), "repeated history queries move to front and preserve acronym versus lowercase identity");
        var text="First paragraph.\r\n\r\nSecond paragraph.";
        history.Record(text,true,now.AddSeconds(3));
        var reopened=new QueryHistory(workspace);
        Check(reopened.Entries[0].Query=="First paragraph.\n\nSecond paragraph." && reopened.Entries[0].Preview=="First paragraph. Second paragraph." && reopened.Entries[0].Translation,
            "history persists full translation paragraphs while exposing only a one-line preview");
        var cache=new Cache(workspace);
        await cache.WriteAsync("fixture-cache","TEE",JsonDocument.Parse("{\"value\":\"retained\"}").RootElement);
        Check(reopened.Remove(reopened.Entries.Single(e=>e.Query=="TEE")), "individual history deletion succeeds");
        var afterDelete=new QueryHistory(workspace);
        Check(afterDelete.Entries.All(e=>e.Query!="TEE") && afterDelete.Entries.Any(e=>e.Query=="tee") && cache.Read<JsonElement>("fixture-cache","TEE").GetProperty("value").GetString()=="retained",
            "deletion persists across restart, preserves other rows and leaves definition caches untouched");
        Check(!afterDelete.Record("   ",false) && !afterDelete.Record(new string('x',Queries.MaxLength+1),true), "blank or invalid submissions never enter history");
        for(int i=0;i<40;i++) afterDelete.Record("query-"+i,false,now.AddMinutes(i+1));
        Check(afterDelete.Entries.Count==QueryHistory.Capacity && new QueryHistory(workspace).Entries[0].Query=="query-39" && afterDelete.Entries.All(e=>e.Query!="query-0"),
            "history bounds retention and keeps most recent submissions after reload");
        var broken=Path.Combine(workspace,"broken"); Directory.CreateDirectory(broken);
        File.WriteAllText(Path.Combine(broken,"history.json"),"invalid json");
        var brokenHistory=new QueryHistory(broken);
        Check(brokenHistory.Entries.Count==0 && brokenHistory.Error.Length>0 && brokenHistory.Record("recovered",false), "damaged history cannot block lookup and a later explicit submission can restore it");
        var blocked=Path.Combine(workspace,"blocked"); Directory.CreateDirectory(Path.Combine(blocked,"history.json"));
        var blockedHistory=new QueryHistory(blocked);
        Check(!blockedHistory.Record("unsaved",false) && blockedHistory.Entries.Count==0 && blockedHistory.Error.Length>0,
            "failed persistence is reported without pretending the history mutation succeeded");
        var sense=new AiSense("noun","A concept.","一个已经命名的概念", "", "", "", [], Gloss:"简短解释", CoreMeaning:true, ChineseTerm:"约定译名");
        Check(QuickRead.SummaryText(sense)=="约定译名：简短解释" && QuickRead.ChineseDefinition(sense)=="约定译名；一个已经命名的概念",
            "both overview and full definition retain a supplied Chinese equivalent before explanatory prose");
        Check(QuickRead.SummaryText(sense with {Gloss="约定译名：简短解释"})=="约定译名：简短解释" && QuickRead.ChineseDefinition(sense with {Chinese="约定译名；已有解释"})=="约定译名；已有解释",
            "translation-first responses are not prefixed twice");
        Check(QuickRead.SummaryText(sense with {Gloss=new string('词',30)})=="约定译名", "tight summaries keep the name instead of truncating a descriptive clause");
        Check(QuickRead.SummaryText(sense with {ChineseTerm=""})=="简短解释" && QuickRead.SummaryText(sense with {ChineseTerm="Unknown English"})=="简短解释",
            "missing or malformed equivalents retain a description without inventing a translation");
        var model=new LookupModel("codex","fixture");
        var unnamed=new SenseCandidate("m0s0",model,sense with {ChineseTerm=""},"unverified",false);
        var named=unnamed with {Id="m1s0",Sense=sense};
        Check(QuickRead.Create([new([unnamed,named])],[],[]).Single().Text=="约定译名：简短解释",
            "merged equivalent wording can supply a translation omitted by the representative");
        Check(QuickRead.Create([new([unnamed,named with {Provisional=true}])],[],[]).Single().Text=="简短解释",
            "unfinished translations never enter a completed overview");
        var entry=new AiEntry("fixture",true,[sense],"","unverified",WordForms:[]);
        var parsed=CodexDictionary.ParseAndValidate(JsonSerializer.Serialize(entry,Json.Flexible),"fixture",null,true);
        Check(parsed.Senses[0].ChineseTerm=="约定译名" && StreamingEntry.Read(JsonSerializer.Serialize(entry,Json.Flexible),"fixture")!.Senses[0].ChineseTerm=="约定译名",
            "structured equivalents survive complete validation and streaming definition display");
        var settings=new AppSettings {Provider=model.Provider,Model=model.Model,ParallelLookup=false,MergeSenses=false};
        var legacyCache=new Cache(Path.Combine(workspace,"legacy"));
        await legacyCache.WriteAsync("ai-v5",model.CacheKey("fixture",settings),new AiEnvelope(entry with {Senses=[sense with {ChineseTerm=""}]},CodexDictionary.EvidenceHash(null),now,model.Model,settings.ReasoningEffort,model.Provider));
        int calls=0;
        var coordinator=new DictionaryCoordinator(legacyCache,(_,_,_,_,_,_)=>{calls++;return Task.FromResult(entry);},(_,_,_)=>throw new Exception("No separate summary call"));
        settings.Online=false;
        var offline=await coordinator.LookupAsync("fixture",null,settings,false,CancellationToken.None);
        Check(calls==0 && offline.Groups.Length==1, "older gloss caches remain available offline before equivalent enrichment");
        settings.Online=true;
        var retained=await coordinator.LookupAsync("fixture",null,settings,false,CancellationToken.None);
        Check(calls==0 && retained.Answers.Single().Entry!.Senses[0].ChineseTerm=="", "legacy caches stay unchanged online until explicit regeneration");
        var refreshed=await coordinator.LookupAsync("fixture",null,settings,true,CancellationToken.None);
        await coordinator.LookupAsync("fixture",null,settings,false,CancellationToken.None);
        Check(calls==1 && refreshed.Answers.Single().Entry!.Senses[0].ChineseTerm=="约定译名", "explicit regeneration enriches legacy results and subsequent queries reuse them");
    }
}
