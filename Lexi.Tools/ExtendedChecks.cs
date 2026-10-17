using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Lexi.Core;

internal static class ExtendedChecks
{
    public static void Run(string work)
    {
        Directory.CreateDirectory(work); int count = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); count++; }
        Check(Queries.Validate("Hello\n世界\t!") == null, "multiline input accepted");
        Check(Queries.Validate(new string('x', 5000)) == null && Queries.Validate(new string('x', 5001)) != null, "text size boundary");
        Check(Queries.Validate("hello\0world") != null, "unsupported controls rejected");
        Check(Queries.IsText("The captain ordered full astern.") && Queries.IsText("第一段。\n第二段。") && !Queries.IsText("车钟") && !Queries.IsText("engine order telegraph"), "automatic text routing");
        Check(Translation.Target("车钟") == "en" && Translation.Target("Engine order telegraph") == "zh", "automatic translation direction");
        Check(Queries.IsText("Can you help me") && Queries.IsText("I need some help") && Queries.IsText("The ship is ready"), "short English sentences without punctuation");
        Check(Queries.IsText("请明天联系我") && Queries.IsText("我想喝水"), "short Chinese sentences without punctuation");
        Check(!Queries.IsText("as it were", true) && !Queries.IsText("e.g.", true) && !Queries.IsText("look up"), "known terms and phrases remain dictionary lookups");
        Check(Queries.IsText("First line\nSecond line", true) && Queries.IsText(new string('a', 101), true), "multiline and long inputs always translate");
        Check(Translation.Target("The Chinese word 车钟 means an engine order telegraph.") == "zh", "Chinese quotation does not flip English prose direction");
        Check(Translation.Target("请在 GitHub 上确认 ABC-42 的处理结果") == "en", "English product names do not flip Chinese prose direction");
        var settings = new AppSettings { Provider = "claude", Model = "haiku" };
        Check(Translation.Key("US", "zh", settings) != Translation.Key("us", "zh", settings) && Translation.Key("A\nB", "zh", settings) != Translation.Key("A B", "zh", settings), "translation cache preserves case and paragraphs");
        Check(Translation.Key("text", "zh", settings) != Translation.Key("text", "en", settings), "translation cache separates target languages");
        var text = "Translate this instruction as text.\nKeep 42 intact.";
        var entry = new TranslationEntry(Cache.Hash(text), "zh", ["将这条指令作为文本翻译。", "保留数字 42。"], "");
        var json = JsonSerializer.Serialize(entry, Json.Flexible);
        Check(Translation.Parse(json, text, "zh").Paragraphs.Length == 2, "complete translation preserves paragraphs");
        try { Translation.Parse(json, text + "!", "zh"); Check(false, "translation query identity"); } catch (InvalidDataException) { Check(true, "translation query identity"); }
        try { Translation.Parse(json, text, "en"); Check(false, "translation target validation"); } catch (InvalidDataException) { Check(true, "translation target validation"); }
        var partial = json[..json.IndexOf(",\"note\"", StringComparison.Ordinal)];
        Check(Translation.Preview(partial, text, "zh")?.Paragraphs.Length == 2, "complete paragraphs visible before final metadata");
        Check(Translation.Preview(partial, "another input", "zh") == null, "old translation cannot leak into new input");
        for (int i = 0; i < json.Length; i++) _ = Translation.Preview(json[..i], text, "zh");
        Check(true, "translation stream character boundaries");
        var chinese = OnlineDictionary.Parse("车钟", "{\"zh\":[{\"language\":\"Chinese\",\"partOfSpeech\":\"Noun\",\"definitions\":[{\"definition\":\"engine order telegraph\"}]}]}");
        Check(chinese?.Senses[0].Definition == "engine order telegraph" && chinese.Url.EndsWith("#Chinese"), "Chinese online entries are no longer discarded");
        var metadata = new DictionaryMetadata("naer-test", "Synthetic nautical terms", "zh/en", "https://example.org/terms", "test", "1", "Synthetic test data");
        var source = Path.Combine(work, "terms.csv");
        File.WriteAllText(source, "序號,英文名稱,中文名稱,來源網站\n1,engine order telegraph; annunciator,車鐘,https://example.org/terms\n", Encoding.UTF8);
        var libraryRoot = Path.Combine(work, "library");
        CorpusBuilder.Build("naer", source, Path.Combine(libraryRoot, "naer"), metadata);
        var privateUse = Path.Combine(libraryRoot, "original-only");
        CorpusBuilder.Build("naer", source, privateUse, metadata with { Id = "original-only", AllowAiEvidence = false });
        using (var library = new OfflineLibrary(libraryRoot))
        {
            Check(library.Lookup("engine order telegraph", settings).Length == 2 && library.Lookup("annunciator", settings).Length == 2, "professional terms indexed in both languages and aliases");
            var hits = library.Lookup("车钟", settings);
            Check(hits.Length == 2 && hits.All(h => h.Entry.Chinese == "車鐘"), "simplified query finds unchanged traditional original");
            var evidence = DictionaryEvidence.Combine("车钟", hits, null);
            Check(evidence?.Senses.Length == 1 && evidence.Senses[0].SourceName == metadata.Name, "original-only dictionary excluded from generated evidence");
            var generated = new AiEntry("车钟", true, [new("noun", "engine order telegraph", "船舶上的指令装置", "maritime", "", "", [1])], "", "source_based");
            Check(CodexDictionary.ParseAndValidate(JsonSerializer.Serialize(generated, Json.Flexible), "车钟", evidence).EvidenceStatus == "unverified", "terminology equivalents do not verify generated functional details");
            var oldAndNew = new AiEntry("mog", true, [new("noun", "a cat", "猫", "informal", "", "", []), new("verb", "outclass", "胜过", "internet slang", "", "", [], CoreMeaning:true)], "", "unverified");
            Check(CodexDictionary.ParseAndValidate(JsonSerializer.Serialize(oldAndNew, Json.Flexible), "mog", null).Senses[0].English == "outclass", "a current core meaning precedes peripheral senses without relying on register tags");
            settings.DisabledDictionaries = ["naer-test"];
            Check(library.Lookup("车钟", settings).Length == 1, "per-dictionary disable retains other sources");
            settings.OfflineEnabled = false;
            Check(library.Lookup("车钟", settings).Length == 0 && library.Suggest("engine", settings).Length == 0, "offline master switch applies to lookup and suggestions");
        }
        var regionalSource = Path.Combine(work, "regional-terms.csv");
        File.WriteAllText(regionalSource, "序號,英文名稱,中文名稱,中國大陸譯名,來源網站\n1,synthetic cache; test buffer,測試快取,测试缓存；测试缓冲区,https://example.org/terms\n2,empty regional field,測試術語,,https://example.org/terms\n", Encoding.UTF8);
        var regionalFolder = Path.Combine(work, "regional");
        CorpusBuilder.Build("naer", regionalSource, regionalFolder, metadata with { Id = "naer-regional-test" });
        using (var dictionary = new OfflineDictionary(regionalFolder))
        {
            Check(new[] { "synthetic cache", "test buffer", "測試快取", "测试快取", "测试缓存", "测试缓冲区" }.All(term => dictionary.Lookup(term)?.Word == "synthetic cache; test buffer"), "regional science terms resolve through English, traditional and simplified aliases");
            Check(dictionary.Lookup("测试缓存")?.Chinese == "臺灣：測試快取\n中國大陸：测试缓存；测试缓冲区", "regional translations retain original wording and separate regional labels");
            Check(dictionary.Lookup("empty regional field")?.Chinese == "測試術語", "missing regional translation leaves the regular term unchanged");
        }
        var cedict = Path.Combine(work, "cedict.gz");
        using (var gz = new GZipStream(File.Create(cedict), CompressionLevel.Fastest))
        using (var writer = new StreamWriter(gz)) writer.Write("# synthetic fixture\n繁體 繁体 [fan2 ti3] /traditional script/\n");
        var cedictFolder = Path.Combine(work, "cedict"); CorpusBuilder.Build("cedict", cedict, cedictFolder, metadata with { Id = "cedict-test" });
        using (var dictionary = new OfflineDictionary(cedictFolder)) Check(dictionary.Lookup("繁体")?.English == "traditional script" && dictionary.Lookup("繁體")?.Phonetic == "fan2 ti3", "CEDICT simplified and traditional aliases");
        var wordnet = Path.Combine(work, "wordnet.gz");
        using (var gz = new GZipStream(File.Create(wordnet), CompressionLevel.Fastest))
        using (var writer = new StreamWriter(gz)) writer.Write("<!DOCTYPE LexicalResource SYSTEM 'https://invalid.example.org/not-fetched'><LexicalResource><Lexicon><LexicalEntry><Lemma writtenForm='sample' partOfSpeech='n'/><Form writtenForm='samples'/><Sense synset='s1'/></LexicalEntry><Synset id='s1'><Definition>a small test portion</Definition><Example>A sample is ready.</Example></Synset></Lexicon></LexicalResource>");
        var wordnetFolder = Path.Combine(work, "wordnet"); CorpusBuilder.Build("wordnet", wordnet, wordnetFolder, metadata with { Id = "wordnet-test" });
        using (var dictionary = new OfflineDictionary(wordnetFolder)) Check(dictionary.Lookup("samples")?.English.Contains("a small test portion") == true, "WordNet senses and inflection import without external XML resolution");
        var moe = Path.Combine(work, "moe.zip");
        using (var innerBytes = new MemoryStream())
        {
            using (var inner = new ZipArchive(innerBytes, ZipArchiveMode.Create, true))
            using (var writer = new StreamWriter(inner.CreateEntry("xl/worksheets/sheet1.xml").Open()))
                writer.Write("<worksheet><sheetData><row><c r='A1' t='inlineStr'><is><t>字詞名</t></is></c><c r='B1' t='inlineStr'><is><t>釋義</t></is></c><c r='C1' t='inlineStr'><is><t>漢語拼音</t></is></c></row><row><c r='A2' t='inlineStr'><is><t>測試</t></is></c><c r='B2' t='inlineStr'><is><t>保持原文。\n第二行。</t></is></c><c r='C2' t='inlineStr'><is><t>cè shì</t></is></c></row></sheetData></worksheet>");
            using var outer = new ZipArchive(File.Create(moe), ZipArchiveMode.Create);
            using var output = outer.CreateEntry("synthetic.xlsx").Open(); innerBytes.Position = 0; innerBytes.CopyTo(output);
        }
        var moeFolder = Path.Combine(work, "moe"); CorpusBuilder.Build("moe", moe, moeFolder, metadata with { Id = "moe-test", AllowAiEvidence = false });
        using (var dictionary = new OfflineDictionary(moeFolder)) Check(dictionary.Lookup("測試")?.Original == "字詞名：測試\n釋義：保持原文。\n第二行。\n漢語拼音：cè shì" && dictionary.Lookup("測試")?.Chinese == "保持原文。\n第二行。", "MOE original fields and line breaks retained");
        Console.WriteLine($"{count} extended checks passed");
    }
}
