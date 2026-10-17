using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Lexi.Core;
using Microsoft.VisualBasic.FileIO;

internal static class CorpusBuilder
{
    public static void Build(string format, string source, string folder, DictionaryMetadata metadata)
    {
        var entries = new Dictionary<string, LocalEntry>(StringComparer.Ordinal);
        void Add(string key, LocalEntry entry)
        {
            key = Queries.Normalize(key);
            AddKey(key, entry);
            // Extra index keys do not change any original headword, definition or source field.
            var simplified = Queries.SimplifiedKey(key);
            if (simplified != key) AddKey(simplified, entry);
        }
        void AddKey(string key, LocalEntry entry)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            if (entries.TryGetValue(key, out var old))
                entries[key] = old with { English = Join(old.English, entry.English), Chinese = Join(old.Chinese, entry.Chinese), Original = Join(old.Original, entry.Original) };
            else entries[key] = entry;
        }
        if (format == "cedict")
        {
            using var gzip = new GZipStream(File.OpenRead(source), CompressionMode.Decompress);
            using var reader = new StreamReader(gzip);
            while (reader.ReadLine() is { } line)
            {
                if (line.StartsWith('#')) continue;
                var match = Regex.Match(line, @"^(\S+) (\S+) \[(.*?)\] /(.*)/$");
                if (!match.Success) continue;
                var traditional = match.Groups[1].Value; var simplified = match.Groups[2].Value;
                var entry = new LocalEntry(traditional, match.Groups[3].Value, match.Groups[4].Value.Replace("/", "; "), "", "", 0);
                Add(traditional, entry); if (simplified != Queries.SimplifiedKey(traditional)) Add(simplified, entry);
            }
        }
        else if (format == "naer")
        {
            using var csv = new TextFieldParser(source, Encoding.UTF8) { TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true };
            csv.SetDelimiters(","); var header = csv.ReadFields() ?? [];
            int en = Array.IndexOf(header, "英文名稱"), zh = Array.IndexOf(header, "中文名稱"), mainland = Array.IndexOf(header, "中國大陸譯名");
            if (en < 0 || zh < 0) throw new InvalidDataException("Unrecognized NAER columns");
            while (!csv.EndOfData)
            {
                var row = csv.ReadFields()!;
                if (row.Length <= Math.Max(en, zh)) continue;
                var mainlandTerm = mainland >= 0 && mainland < row.Length ? row[mainland] : "";
                var chinese = mainlandTerm.Length == 0 ? row[zh] : $"臺灣：{row[zh]}\n中國大陸：{mainlandTerm}";
                var entry = new LocalEntry(row[en], "", row[en], chinese, "", 0);
                foreach (var term in new[] { row[en], row[zh], mainlandTerm }.SelectMany(t => t.Split([';', '；', '、'], StringSplitOptions.RemoveEmptyEntries)).Select(t => t.Trim()).Distinct()) Add(term, entry);
            }
        }
        else if (format == "wordnet")
        {
            var synsets = new Dictionary<string, string>();
            foreach (var node in ReadGzipElements(source, "Synset"))
                synsets[(string)node.Attribute("id")!] = string.Join("\n", node.Elements().Where(e => e.Name.LocalName is "Definition" or "Example").Select(e => e.Name.LocalName == "Example" ? "Example: " + e.Value : e.Value));
            foreach (var node in ReadGzipElements(source, "LexicalEntry"))
            {
                var lemma = node.Element("Lemma"); if (lemma == null) continue;
                var word = (string?)lemma.Attribute("writtenForm") ?? "";
                var pos = (string?)lemma.Attribute("partOfSpeech") ?? "";
                var definitions = node.Elements("Sense").Select(s => synsets.GetValueOrDefault((string?)s.Attribute("synset") ?? "", "")).Where(s => s.Length > 0).Distinct().ToArray();
                if (definitions.Length == 0) continue;
                var body = string.Join("\n\n", definitions.Select((s, i) => $"{i + 1}. ({pos}) {s}"));
                var entry = new LocalEntry(word, "", body, "", "", 0);
                Add(word, entry);
                foreach (var form in node.Elements("Form").Select(f => (string?)f.Attribute("writtenForm")).Where(f => f != null && f != word).Distinct()) Add(form!, entry);
            }
        }
        else if (format == "moe")
        {
            using var outer = ZipFile.OpenRead(source);
            var xlsx = outer.Entries.Where(e => e.FullName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)).OrderByDescending(e => e.Length).First();
            using var bytes = new MemoryStream(); using (var input = xlsx.Open()) input.CopyTo(bytes); bytes.Position = 0;
            using var archive = new ZipArchive(bytes, ZipArchiveMode.Read);
            var sharedEntry = archive.GetEntry("xl/sharedStrings.xml");
            string[] shared = [];
            if (sharedEntry != null)
            {
                using var stream = sharedEntry.Open();
                shared = ReadElements(stream, "si").Select(e => XmlConvert.DecodeName(string.Concat(e.Descendants().Where(x => x.Name.LocalName == "t").Select(x => x.Value)))).ToArray();
            }
            foreach (var sheet in archive.Entries.Where(e => e.FullName.StartsWith("xl/worksheets/sheet") && e.FullName.EndsWith(".xml")))
            {
                using var stream = sheet.Open(); string[]? headers = null;
                foreach (var node in ReadElements(stream, "row"))
                {
                    var values = new SortedDictionary<int, string>();
                    foreach (var cell in node.Elements().Where(c => c.Name.LocalName == "c"))
                    {
                        var reference = (string?)cell.Attribute("r") ?? "A1"; int column = 0;
                        foreach (var c in reference.TakeWhile(char.IsLetter)) column = column * 26 + c - 'A' + 1;
                        var value = cell.Elements().FirstOrDefault(x => x.Name.LocalName == "v")?.Value ?? "";
                        if ((string?)cell.Attribute("t") == "s") value = shared[int.Parse(value)];
                        else if ((string?)cell.Attribute("t") == "inlineStr") value = string.Concat(cell.Descendants().Where(x => x.Name.LocalName == "t").Select(x => x.Value));
                        values[column - 1] = value;
                    }
                    if (values.Count == 0) continue;
                    if (headers == null) { headers = Enumerable.Range(0, values.Keys.Max() + 1).Select(i => values.GetValueOrDefault(i, "")).ToArray(); continue; }
                    int wordColumn = Array.FindIndex(headers, h => h is "字詞名" or "字詞" or "詞目");
                    if (wordColumn < 0) throw new InvalidDataException("Unrecognized MOE columns: " + string.Join(",", headers));
                    var word = values.GetValueOrDefault(wordColumn, ""); if (word.Length == 0) continue;
                    // Every original field is retained; no simplified conversion or AI rewriting of MOE text.
                    var original = string.Join("\n", values.Where(v => v.Value.Length > 0).Select(v => (v.Key < headers.Length ? headers[v.Key] : $"Column {v.Key + 1}") + "：" + v.Value));
                    var pinyin = Array.FindIndex(headers, h => h.Contains("漢語拼音"));
                    int definition = Array.IndexOf(headers, "釋義");
                    Add(word, new(word, values.GetValueOrDefault(pinyin, ""), "", values.GetValueOrDefault(definition, original), "", 0, original));
                }
            }
        }
        else throw new ArgumentException("Unknown corpus format: " + format);
        if (entries.Count == 0) throw new InvalidDataException("Empty dictionary; nothing was installed");
        Write(folder, entries);
        File.WriteAllText(Path.Combine(folder, "dictionary.json"), JsonSerializer.Serialize(metadata, Json.Flexible));
        Console.WriteLine($"{metadata.Id}: built {entries.Count:N0} lookup entries");
    }
    private static string Join(string left, string right) => string.IsNullOrEmpty(right) || left == right ? left : string.IsNullOrEmpty(left) ? right : left + "\n\n" + right;
    private static IEnumerable<XElement> ReadGzipElements(string path, string name)
    {
        using var gzip = new GZipStream(File.OpenRead(path), CompressionMode.Decompress);
        foreach (var node in ReadElements(gzip, name)) yield return node;
    }
    private static IEnumerable<XElement> ReadElements(Stream input, string name)
    {
        using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
        while (!reader.EOF)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == name) yield return (XElement)XNode.ReadFrom(reader);
            else reader.Read();
        }
    }
    private static void Write(string folder, Dictionary<string, LocalEntry> entries)
    {
        Directory.CreateDirectory(folder);
        using var data = File.Create(Path.Combine(folder, "lexicon.dat"));
        using var index = new BinaryWriter(File.Create(Path.Combine(folder, "lexicon.idx")), Encoding.UTF8);
        index.Write("LEXI1"); index.Write(entries.Count);
        foreach (var (key, entry) in entries.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(entry, Json.Flexible);
            index.Write(key); index.Write(data.Position); index.Write(bytes.Length); index.Write(0); data.Write(bytes);
        }
        index.Write(0);
    }
}
