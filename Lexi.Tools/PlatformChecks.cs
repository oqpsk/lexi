using System.Text;
using Lexi.Core;

// Platform services shared by the Windows and macOS front ends, checked with synthetic
// folders only: no installed clients, user profile or network access are consulted.
internal static class PlatformChecks
{
    public static void Run(string root)
    {
        int count = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); count++; }
        var work = Path.Combine(Path.GetFullPath(root), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);

        Check(Queries.TraditionalKeys("车钟").Contains("車鐘") && Queries.TraditionalKey("车钟").StartsWith('車') &&
            Queries.SimplifiedKey("車鐘") == "车钟" && Queries.SimplifiedKey("測試快取") == "测试快取",
            "simplified and traditional keys use the same embedded table on every platform");
        Check(Queries.SimplifiedKey("hello 世界") == "hello 世界" && Queries.TraditionalKeys("hello") is ["hello"] &&
            Queries.SimplifiedKey("𠀀測") == "𠀀测", "non-Chinese text and supplementary-plane characters pass through unchanged");
        Check(ChineseVariants.TraditionalCandidates(string.Concat(Enumerable.Repeat("发干钟", 6))).Length <= ChineseVariants.MaxCandidates,
            "ambiguous long phrases produce a bounded set of traditional lookup keys");
        var legacy = Path.Combine(work, "legacy-index");
        WriteIndex(legacy, ("車鐘", new LocalEntry("車鐘", "", "engine order telegraph", "車鐘", "", 0)));
        File.WriteAllText(Path.Combine(legacy, "dictionary.json"), "{\"id\":\"legacy\",\"name\":\"Legacy\",\"languages\":\"zh\",\"sourceUrl\":\"https://example.org\",\"license\":\"test\",\"version\":\"1\",\"attribution\":\"test\"}");
        using (var library = new OfflineLibrary(work))
            Check(library.Lookup("车钟", new AppSettings()).SingleOrDefault()?.Entry.Chinese == "車鐘",
                "older traditional-only indexes still answer simplified queries through every candidate spelling");

        var windows = LexiPaths.For(true, false, Path.Combine(work, "LeXi"), Path.Combine(work, "home"));
        Check(windows.UserRoot == Path.Combine(work, "LeXi", "userdata") && windows.DataRoot == Path.Combine(work, "LeXi", "data") &&
            windows.AssetRoot == Path.Combine(work, "LeXi", "assets"), "Windows keeps portable userdata and data folders for upgrades");
        var mac = LexiPaths.For(false, true, Path.Combine(work, "LeXi.app"), Path.Combine(work, "home"));
        Check(mac.UserRoot == Path.Combine(work, "home", "Library", "Application Support", "LeXi") &&
            mac.DataRoot == Path.Combine(mac.UserRoot, "data") && mac.AssetRoot == Path.Combine(work, "LeXi.app", "assets"),
            "macOS stores settings, caches and dictionaries in Application Support/LeXi");
        Check(LexiPaths.For(false, false, work, Path.Combine(work, "home")).UserRoot == Path.Combine(work, "home", ".local", "share", "LeXi") &&
            LexiPaths.For(false, false, work, "/unused", Path.Combine(work, "xdg")).UserRoot == Path.Combine(work, "xdg", "LeXi") &&
            mac.With(user: Path.Combine(work, "override")).UserRoot == Path.Combine(work, "override") && mac.With().DataRoot == mac.DataRoot,
            "other systems follow XDG and explicit host overrides replace only the named folder");

        // A Finder-launched app's PATH holds only system folders without these clients. System
        // locations (/opt/homebrew, /usr/local, /Applications) live under a synthetic root.
        var home = Path.Combine(work, "home");
        var system = Path.Combine(work, "system");
        var unix = new ToolEnvironment(false, home, "", "", "", Path.Combine(work, "npm"), system);
        var native = Touch(Path.Combine(home, ".local", "bin", "claude"), "\u00cf\u00fa\u00ed\u00fe binary");
        Check(ToolLocator.Claude(unix) is { Script: null } claude && claude.FileName == native &&
            claude.StartInfo().Environment["PATH"]!.Split(Path.PathSeparator).Contains(Path.Combine(home, ".local", "bin")),
            "native Claude in ~/.local/bin is found without a login-shell PATH and its folder joins the child PATH");
        File.Delete(native);
        var npmBin = Path.Combine(work, "npm", "bin");
        var cli = Touch(Path.Combine(work, "npm", "lib", "node_modules", "@anthropic-ai", "claude-code", "cli.js"), "#!/usr/bin/env node\n");
        var node = Touch(Path.Combine(npmBin, "node"), "binary");
        // Homebrew's node comes earlier in the search order but belongs to another prefix.
        Touch(Path.Combine(system, "opt", "homebrew", "bin", "node"), "binary");
        Check(ToolLocator.Claude(unix) is { } npmClaude && npmClaude.FileName == node && npmClaude.Script == cli,
            "an npm-global Claude runs through node from the same prefix instead of relying on the shebang PATH");
        var shim = Touch(Path.Combine(npmBin, "codex"), "#!/usr/bin/env node\nimport('../lib/codex.js');\n");
        var login = ProviderConnection.Command("codex", shim, true)!;
        Check(ToolLocator.Codex("", unix) is { } found && found.FileName == node && found.Script == shim &&
            (OperatingSystem.IsWindows() ? login.FileName == shim && login.ArgumentList.SequenceEqual(["login"]) :
                login.FileName == node && login.ArgumentList.SequenceEqual([shim, "login"])),
            "an npm Codex shim is launched through node for queries and the explicit login command");
        var nvm = Path.Combine(home, ".nvm", "versions", "node", "v24.0.0", "bin");
        var nvmCodex = Touch(Path.Combine(nvm, "codex-native"), "binary");
        Check(ToolLocator.SearchDirectories(unix).Contains(nvm) && ToolLocator.SearchDirectories(unix).Contains(Path.Combine(system, "opt", "homebrew", "bin")) &&
            ToolLocator.SearchDirectories(unix).Contains(Path.Combine(home, ".local", "bin")) && ToolLocator.Codex(nvmCodex, unix)?.Script == null,
            "Homebrew, ~/.local/bin, nvm and npm global folders are searched; configured native paths run directly");

        // Clients that ship inside the ChatGPT and Claude desktop apps.
        var desktopHome = Path.Combine(work, "desktop-home");
        var desktop = unix with { Home = desktopHome, NpmPrefix = "" };
        Check(ToolLocator.Codex("", desktop) == null && ToolLocator.Claude(desktop) == null, "without any install the clients stay recoverably missing");
        var chatgpt = Touch(Path.Combine(system, "Applications", "ChatGPT.app", "Contents", "Resources", "codex-cli", "bin", "codex"), "#!/bin/sh\nexec true\n");
        var claudeData = Path.Combine(desktopHome, "Library", "Application Support", "Claude");
        string Build(string version, string build, bool verified)
        {
            var app = Touch(Path.Combine(claudeData, "claude-code", version, build, "claude.app", "Contents", "MacOS", "claude"), "binary");
            if (verified) Touch(Path.Combine(claudeData, "claude-code", version, build, ".verified"), "");
            return app;
        }
        Build("2.1.9", "older", true);
        var current = Build("2.1.10", "current", true);
        Build("2.1.11", "downloading", false);
        Touch(Path.Combine(claudeData, "claude-code-vm", "2.1.11", "claude"), "\u007fELF linux build");
        Check(ToolLocator.Codex("", desktop) is { Script: null } desktopCodex && desktopCodex.FileName == chatgpt && ToolLocator.Claude(desktop)?.FileName == current,
            "the ChatGPT app's Codex and the Claude app's newest verified macOS Claude Code are used when nothing else is installed");
        var preferred = Touch(Path.Combine(desktopHome, ".local", "bin", "claude"), "binary");
        Check(ToolLocator.Claude(desktop)?.FileName == preferred, "a command-line install still takes precedence over a desktop app's copy");

        var appData = Path.Combine(work, "AppData", "Roaming"); var localAppData = Path.Combine(work, "AppData", "Local");
        var winPath = Path.Combine(work, "WinPath");
        var win = new ToolEnvironment(true, Path.Combine(work, "WinHome"), winPath, appData, localAppData);
        Check(ToolLocator.Codex("", win) == null && ToolLocator.Claude(win) == null, "missing Windows clients stay recoverable");
        var bundled = Touch(Path.Combine(localAppData, "OpenAI", "Codex", "bin", "1.0", "codex.exe"), "binary");
        var winCli = Touch(Path.Combine(appData, "npm", "node_modules", "@anthropic-ai", "claude-code", "cli.js"), "#!/usr/bin/env node\n");
        var winNode = Touch(Path.Combine(winPath, "node.exe"), "binary");
        Check(ToolLocator.Codex("", win) is { FileName: var exe, Script: null, SearchPath: null } && exe == bundled &&
            ToolLocator.Claude(win) is { } winClaude && winClaude.FileName == winNode && winClaude.Script == winCli,
            "Windows discovery keeps codex.exe, the Codex app folder and npm cli.js with node.exe");
        var winNative = Touch(Path.Combine(work, "WinHome", ".local", "bin", "claude.exe"), "binary");
        Check(ToolLocator.Claude(win)?.FileName == winNative && !ToolLocator.SearchDirectories(win).Contains("/opt/homebrew/bin"),
            "Windows prefers the native Claude installer and never searches Unix folders");
        Console.WriteLine($"{count} platform checks passed");
    }

    private static string Touch(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    // The LEXI1 index format, written without the simplified aliases newer builds add.
    private static void WriteIndex(string folder, params (string Key, LocalEntry Entry)[] entries)
    {
        Directory.CreateDirectory(folder);
        using var data = File.Create(Path.Combine(folder, "lexicon.dat"));
        using var index = new BinaryWriter(File.Create(Path.Combine(folder, "lexicon.idx")), Encoding.UTF8);
        index.Write("LEXI1"); index.Write(entries.Length);
        foreach (var (key, entry) in entries.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(entry, Json.Flexible);
            index.Write(key); index.Write(data.Position); index.Write(bytes.Length); index.Write(entry.Frequency); data.Write(bytes);
        }
        index.Write(0);
    }
}
