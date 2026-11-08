using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Harbor
{
    /// <summary>
    /// Recent Claude Code sessions from its own history files (%USERPROFILE%\.claude\projects\*\*.jsonl), for the island's
    /// AI Coding tab: project, title, branch, model, messages and tokens. Read in the background and incrementally: a file
    /// is only read from where we stopped last time (they can be tens of MB). Nothing leaves the PC.
    /// </summary>
    static class ClaudeHistory
    {
        public sealed class Entry
        {
            public string File, SessionId, Cwd, Title, Branch, Model;
            public int Messages;
            public long Tokens;
            public DateTime LastActive;
            internal long Offset;
            internal string LastMsgId;
            public string Project { get { return string.IsNullOrEmpty(Cwd) ? "Claude Code" : Path.GetFileName(Cwd.TrimEnd('\\', '/')); } }
        }

        static readonly Dictionary<string, Entry> cache = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        static List<Entry> latest = new List<Entry>();
        static bool scanning;
        static DateTime lastScan;
        public static event Action Changed;

        public static bool Available { get { return ClaudeBridge.ConfigDirs().Count > 0; } }

        /// <summary>The newest sessions (as of the last scan). Starts a background refresh if the last one is a few seconds old.</summary>
        public static List<Entry> Recent()
        {
            if (!demo && !scanning && (DateTime.Now - lastScan).TotalSeconds > 5) Scan();
            return latest;
        }

        static bool demo;

        /// <summary>--snapshot --demo: sample sessions; your real history is never read.</summary>
        public static void UseDemo()
        {
            demo = true;
            var now = DateTime.Now;
            latest = new List<Entry>
            {
                new Entry { SessionId = "demo-1", Cwd = @"C:\code\aurora-shop", Title = "Add cart persistence and guest checkout", Branch = "feature/guest-checkout", Model = "claude-opus-5-5", Messages = 28, Tokens = 50400, LastActive = now.AddMinutes(-2) },
                new Entry { SessionId = "demo-2", Cwd = @"C:\code\northwind-api", Title = "Sketch OpenAPI for inventory webhooks", Branch = "docs/inventory-webhooks", Model = "claude-sonnet-5-5", Messages = 12, Tokens = 21300, LastActive = now.AddMinutes(-18) },
                new Entry { SessionId = "demo-3", Cwd = @"C:\code\orbit-app", Title = "Fix map clustering on zoom", Branch = "fix/map-clustering", Model = "claude-sonnet-5-5", Messages = 16, Tokens = 18900, LastActive = now.AddMinutes(-8) },
            };
        }

        static void Scan()
        {
            scanning = true;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                var result = new List<Entry>();
                try
                {
                    var files = new List<FileInfo>();
                    // every Claude Code folder: .claude, .claude-vscode, .claude-antigravity...
                    foreach (var cfg in ClaudeBridge.ConfigDirs())
                    {
                        string root = Path.Combine(cfg, "projects");
                        if (!Directory.Exists(root)) continue;
                        foreach (var dir in new DirectoryInfo(root).GetDirectories())
                            foreach (var f in dir.GetFiles("*.jsonl")) // top level only: subagent transcripts live in subfolders
                                if ((DateTime.Now - f.LastWriteTime).TotalDays < 3) files.Add(f);
                    }
                    files.Sort((a, b) => b.LastWriteTime.CompareTo(a.LastWriteTime));
                    for (int i = 0; i < files.Count && i < 8; i++)
                    {
                        Entry e;
                        lock (cache)
                            if (!cache.TryGetValue(files[i].FullName, out e))
                                cache[files[i].FullName] = e = new Entry { File = files[i].FullName, SessionId = Path.GetFileNameWithoutExtension(files[i].Name) };
                        Read(e, files[i]);
                        result.Add(e);
                    }
                }
                catch (Exception ex) { App.Log(ex); }
                App.Post(() =>
                {
                    latest = result;
                    scanning = false;
                    lastScan = DateTime.Now;
                    if (Changed != null) Changed();
                });
            });
        }

        static readonly byte[] kUser = JsonlScan.Bytes("\"type\":\"user\""), kAssistant = JsonlScan.Bytes("\"type\":\"assistant\""),
            kTitleType = JsonlScan.Bytes("\"type\":\"ai-title\""), kCwd = JsonlScan.Bytes("\"cwd\":\""), kBranch = JsonlScan.Bytes("\"gitBranch\":\""),
            kModel = JsonlScan.Bytes("\"model\":\"claude-"), kMsgId = JsonlScan.Bytes("\"id\":\"msg_"), kTitle = JsonlScan.Bytes("\"aiTitle\":\""),
            kIn = JsonlScan.Bytes("\"input_tokens\":"), kOut = JsonlScan.Bytes("\"output_tokens\":"), kCacheNew = JsonlScan.Bytes("\"cache_creation_input_tokens\":");

        /// <summary>Reads what was appended since last time.</summary>
        static void Read(Entry e, FileInfo f)
        {
            if (f.Length < e.Offset) { e.Offset = 0; e.Messages = 0; e.Tokens = 0; } // rewritten
            e.LastActive = f.LastWriteTime;
            if (f.Length == e.Offset) return;
            e.Offset = JsonlScan.Read(f.FullName, e.Offset, (b, a, z) => Line(e, b, a, z));
        }

        static void Line(Entry e, byte[] b, int a, int z)
        {
            // the line's kind is its first "type" of these three (nested content has other types)
            int pu = JsonlScan.Find(b, a, z, kUser), pa = JsonlScan.Find(b, a, z, kAssistant), pt = JsonlScan.Find(b, a, z, kTitleType);
            int first = -1; string kind = null;
            if (pu >= 0) { first = pu; kind = "user"; }
            if (pa >= 0 && (first < 0 || pa < first)) { first = pa; kind = "assistant"; }
            if (pt >= 0 && (first < 0 || pt < first)) { first = pt; kind = "ai-title"; }
            if (kind == null) return;
            string v;
            if ((v = JsonlScan.Str(b, a, z, kCwd)) != null) e.Cwd = v;
            if ((v = JsonlScan.Str(b, a, z, kBranch)) != null && v != "") e.Branch = v;
            switch (kind)
            {
                case "ai-title": if ((v = JsonlScan.Str(b, a, z, kTitle)) != null) e.Title = v; break;
                case "user": e.Messages++; break;
                case "assistant":
                    if ((v = JsonlScan.Str(b, a, z, kModel)) != null) e.Model = "claude-" + v;
                    // one reply is written as several lines that repeat the same usage: count it once
                    string mid = JsonlScan.Str(b, a, z, kMsgId);
                    if (mid != null && mid == e.LastMsgId) break;
                    e.LastMsgId = mid;
                    e.Messages++;
                    e.Tokens += JsonlScan.Num(b, a, z, kIn) + JsonlScan.Num(b, a, z, kOut) + JsonlScan.Num(b, a, z, kCacheNew);
                    break;
            }
        }

        /// <summary>"claude-opus-5-5" → "Opus 5.5", "claude-sonnet-4-5-20250929" → "Sonnet 4.5".</summary>
        public static string ShortModel(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var m = Regex.Match(id, @"claude-([a-z]+)-(\d+)(?:-(\d{1,2}))?(?:-|$)");
            if (!m.Success) return id;
            string name = char.ToUpper(m.Groups[1].Value[0]) + m.Groups[1].Value.Substring(1);
            return name + " " + m.Groups[2].Value + (m.Groups[3].Success && m.Groups[3].Value != "" ? "." + m.Groups[3].Value : "");
        }

        public static string Tokens(long t)
        {
            return t >= 1000000 ? (t / 1000000.0).ToString("0.0") + "M" : t >= 1000 ? (t / 1000.0).ToString("0.0") + "k" : t.ToString();
        }
    }
}
