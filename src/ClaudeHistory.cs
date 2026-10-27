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
            internal string LastMsgId, Partial = "";
            public string Project { get { return string.IsNullOrEmpty(Cwd) ? "Claude Code" : Path.GetFileName(Cwd.TrimEnd('\\', '/')); } }
        }

        static readonly Dictionary<string, Entry> cache = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        static List<Entry> latest = new List<Entry>();
        static bool scanning;
        static DateTime lastScan;
        public static event Action Changed;

        public static bool Available { get { return Directory.Exists(Root); } }
        static string Root { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects"); } }

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
                    if (Directory.Exists(Root))
                        foreach (var dir in new DirectoryInfo(Root).GetDirectories())
                            foreach (var f in dir.GetFiles("*.jsonl")) // top level only: subagent transcripts live in subfolders
                                if ((DateTime.Now - f.LastWriteTime).TotalDays < 3) files.Add(f);
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

        static readonly Regex rxType = new Regex("\"type\":\"(user|assistant|ai-title)\""),
            rxCwd = new Regex("\"cwd\":\"((?:[^\"\\\\]|\\\\.)*)\""),
            rxBranch = new Regex("\"gitBranch\":\"((?:[^\"\\\\]|\\\\.)*)\""),
            rxModel = new Regex("\"model\":\"(claude-[^\"]+)\""),
            rxMsgId = new Regex("\"id\":\"(msg_[^\"]+)\""),
            rxTitle = new Regex("\"aiTitle\":\"((?:[^\"\\\\]|\\\\.)*)\""),
            rxIn = new Regex("\"input_tokens\":(\\d+)"),
            rxOut = new Regex("\"output_tokens\":(\\d+)"),
            rxCacheNew = new Regex("\"cache_creation_input_tokens\":(\\d+)");

        /// <summary>Reads what was appended since last time.</summary>
        static void Read(Entry e, FileInfo f)
        {
            if (f.Length < e.Offset) { e.Offset = 0; e.Messages = 0; e.Tokens = 0; e.Partial = ""; } // rewritten
            e.LastActive = f.LastWriteTime;
            if (f.Length == e.Offset) return;
            using (var fs = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                fs.Seek(e.Offset, SeekOrigin.Begin);
                var buf = new byte[1 << 20];
                int n;
                while ((n = fs.Read(buf, 0, buf.Length)) > 0)
                {
                    string chunk = e.Partial + Encoding.UTF8.GetString(buf, 0, n);
                    int start = 0, nl;
                    while ((nl = chunk.IndexOf('\n', start)) >= 0) { Line(e, chunk.Substring(start, nl - start)); start = nl + 1; }
                    e.Partial = chunk.Substring(start);
                    e.Offset += n;
                }
            }
        }

        static void Line(Entry e, string line)
        {
            var t = rxType.Match(line);
            if (!t.Success) return;
            Match m;
            if ((m = rxCwd.Match(line)).Success) e.Cwd = Unescape(m.Groups[1].Value);
            if ((m = rxBranch.Match(line)).Success && m.Groups[1].Value != "") e.Branch = Unescape(m.Groups[1].Value);
            switch (t.Groups[1].Value)
            {
                case "ai-title": if ((m = rxTitle.Match(line)).Success) e.Title = Unescape(m.Groups[1].Value); break;
                case "user": e.Messages++; break;
                case "assistant":
                    if ((m = rxModel.Match(line)).Success) e.Model = m.Groups[1].Value;
                    // one reply is written as several lines that repeat the same usage: count it once
                    var id = rxMsgId.Match(line);
                    string mid = id.Success ? id.Groups[1].Value : null;
                    if (mid != null && mid == e.LastMsgId) break;
                    e.LastMsgId = mid;
                    e.Messages++;
                    e.Tokens += Num(rxIn, line) + Num(rxOut, line) + Num(rxCacheNew, line);
                    break;
            }
        }

        static long Num(Regex rx, string line)
        {
            var m = rx.Match(line);
            long v;
            return m.Success && long.TryParse(m.Groups[1].Value, out v) ? v : 0;
        }

        static string Unescape(string s)
        {
            return s.IndexOf('\\') < 0 ? s : Regex.Unescape(s);
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
