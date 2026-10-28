using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Harbor
{
    /// <summary>
    /// Claude usage for the island. Two local sources, nothing is sent anywhere and no login is touched:
    /// - your plan limits (current session, this week), as Claude Code itself last saved them in its own .claude.json
    ///   (the VS Code extension's is in %USERPROFILE%\.claude-vscode). Claude Code refreshes them when it checks, e.g. /usage.
    /// - tokens per day for the last 7 days, counted from Claude Code's transcripts on this PC, per editor and model.
    /// Read in the background, only while the Usage tab is open, and transcripts only from where we stopped last time.
    /// </summary>
    static class ClaudeUsage
    {
        public sealed class Limit
        {
            public string Name;
            public int Percent;
            public DateTime? Resets;
        }

        public sealed class Stats
        {
            public long[] Week = new long[7];      // tokens per day, 6 days ago .. today
            public int[] Replies = new int[7];
            public long Last5h;
            public List<KeyValuePair<string, long>> Sources = new List<KeyValuePair<string, long>>(); // this week, by editor
            public List<KeyValuePair<string, long>> Models = new List<KeyValuePair<string, long>>();  // this week, by model
        }

        public static List<Limit> Limits = new List<Limit>();
        public static DateTime? LimitsAt;         // when Claude Code fetched them
        public static string LimitsFrom;          // "VS Code", "Terminal"...
        public static Stats Week = new Stats();
        public static bool Loaded;
        public static event Action Changed;

        static bool busy, demo;
        static DateTime lastRefresh;

        /// <summary>Called while the tab is drawn: refreshes in the background at most every 20 seconds.</summary>
        public static void Refresh()
        {
            if (demo || busy || (App.SnapshotMode && !demo) || (DateTime.Now - lastRefresh).TotalSeconds < 20) return;
            busy = true;
            lastRefresh = DateTime.Now;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                List<Limit> lim = null; DateTime? at = null; string from = null; Stats st = null;
                try { ReadLimits(out lim, out at, out from); } catch (Exception ex) { App.Log(ex); }
                try { st = ReadStats(); } catch (Exception ex) { App.Log(ex); }
                App.Post(() =>
                {
                    busy = false;
                    Loaded = true;
                    if (lim != null) { Limits = lim; LimitsAt = at; LimitsFrom = from; }
                    if (st != null) Week = st;
                    if (Changed != null) Changed();
                    App.ScheduleTrim(); // the first read goes through every transcript of the week
                });
            });
        }

        /// <summary>".claude-vscode" → "VS Code", ".claude" → "Terminal".</summary>
        public static string EditorName(string configDir)
        {
            string n = Path.GetFileName(configDir.TrimEnd('\\', '/')).ToLowerInvariant();
            if (n == ".claude") return "Desktop & CLI";
            if (n == ".claude-vscode") return "VS Code";
            if (n == ".claude-antigravity") return "Antigravity";
            if (n == ".claude-cursor") return "Cursor";
            n = n.StartsWith(".claude-") ? n.Substring(8) : n.TrimStart('.');
            return n.Length > 0 ? char.ToUpper(n[0]) + n.Substring(1) : "Claude Code";
        }

        // ------------------------------------------------------------------ plan limits

        static readonly Dictionary<string, DateTime> seenWrite = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, object[]> parsed = new Dictionary<string, object[]>(StringComparer.OrdinalIgnoreCase); // file → { fetchedAt, limits }

        static void ReadLimits(out List<Limit> best, out DateTime? bestAt, out string bestFrom)
        {
            best = null; bestAt = null; bestFrom = null;
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach (var dir in ClaudeBridge.ConfigDirs())
            {
                // the default profile keeps it next to the folder (%USERPROFILE%\.claude.json), the others inside theirs
                string f = string.Equals(Path.GetFileName(dir.TrimEnd('\\')), ".claude", StringComparison.OrdinalIgnoreCase)
                    ? Path.Combine(home, ".claude.json") : Path.Combine(dir, ".claude.json");
                if (!File.Exists(f)) continue;
                DateTime w = File.GetLastWriteTimeUtc(f), prev;
                object[] r;
                if (!seenWrite.TryGetValue(f, out prev) || prev != w || !parsed.TryGetValue(f, out r))
                {
                    seenWrite[f] = w;
                    parsed[f] = r = ParseLimits(f);
                }
                if (r == null) continue;
                var at = (DateTime)r[0];
                if (bestAt == null || at > bestAt.Value) { bestAt = at; best = (List<Limit>)r[1]; bestFrom = EditorName(dir); }
            }
        }

        /// <summary>Only the cachedUsageUtilization block is used; the rest of the file is left alone.</summary>
        static object[] ParseLimits(string file)
        {
            string text;
            using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var sr = new StreamReader(fs, Encoding.UTF8)) text = sr.ReadToEnd();
            if (text.IndexOf("cachedUsageUtilization", StringComparison.Ordinal) < 0) return null;
            var js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 200 };
            var root = js.DeserializeObject(text) as Dictionary<string, object>;
            if (root == null) return null;
            var cu = Get(root, "cachedUsageUtilization") as Dictionary<string, object>;
            if (cu == null) return null;
            var u = Get(cu, "utilization") as Dictionary<string, object>;
            if (u == null) return null;
            DateTime at = DateTime.MinValue;
            object ms = Get(cu, "fetchedAtMs");
            if (ms != null) at = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(Convert.ToDouble(ms, CultureInfo.InvariantCulture)).ToLocalTime();

            var list = new List<Limit>();
            var limits = Get(u, "limits") as IList;
            if (limits != null)
                foreach (var o in limits)
                {
                    var l = o as Dictionary<string, object>;
                    if (l == null || Get(l, "percent") == null) continue;
                    list.Add(new Limit { Name = LimitName(Get(l, "kind") as string), Percent = ToInt(Get(l, "percent")), Resets = ToTime(Get(l, "resets_at")) });
                }
            if (list.Count == 0) // older layout
                foreach (var k in new[] { "five_hour", "seven_day", "seven_day_opus", "seven_day_sonnet" })
                {
                    var l = Get(u, k) as Dictionary<string, object>;
                    if (l == null || Get(l, "utilization") == null) continue;
                    list.Add(new Limit { Name = LimitName(k), Percent = ToInt(Get(l, "utilization")), Resets = ToTime(Get(l, "resets_at")) });
                }
            return list.Count == 0 ? null : new object[] { at, list };
        }

        static object Get(Dictionary<string, object> d, string k) { object v; return d.TryGetValue(k, out v) ? v : null; }

        static int ToInt(object o) { try { return (int)Math.Round(Convert.ToDouble(o, CultureInfo.InvariantCulture)); } catch { return 0; } }

        static DateTime? ToTime(object o)
        {
            DateTimeOffset t;
            var s = o as string;
            return s != null && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out t) ? t.LocalDateTime : (DateTime?)null;
        }

        static string LimitName(string kind)
        {
            switch (kind ?? "")
            {
                case "session": case "five_hour": return "Current session";
                case "weekly_all": case "seven_day": return "This week · all models";
                case "weekly_opus": case "seven_day_opus": return "This week · Opus";
                case "weekly_sonnet": case "seven_day_sonnet": return "This week · Sonnet";
            }
            string k = (kind ?? "Limit").Replace('_', ' ');
            return char.ToUpper(k[0]) + k.Substring(1);
        }

        // ------------------------------------------------------------------ tokens from transcripts

        sealed class FileState
        {
            public string Source;
            public long Offset;
            public string Partial = "", LastMsgId;
            public readonly Dictionary<DateTime, long> Hours = new Dictionary<DateTime, long>();  // local hour → tokens
            public readonly Dictionary<DateTime, int> Replies = new Dictionary<DateTime, int>();  // local day → replies
            public readonly Dictionary<string, long> Models = new Dictionary<string, long>();     // "yyyyMMdd|model" → tokens
        }

        static readonly Dictionary<string, FileState> files = new Dictionary<string, FileState>(StringComparer.OrdinalIgnoreCase);

        static readonly Regex rxAssistant = new Regex("\"type\":\"assistant\""),
            rxTime = new Regex("\"timestamp\":\"([^\"]+)\""),
            rxModel = new Regex("\"model\":\"(claude-[^\"]+)\""),
            rxMsgId = new Regex("\"id\":\"(msg_[^\"]+)\""),
            rxIn = new Regex("\"input_tokens\":(\\d+)"),
            rxOut = new Regex("\"output_tokens\":(\\d+)"),
            rxCacheNew = new Regex("\"cache_creation_input_tokens\":(\\d+)");

        static Stats ReadStats()
        {
            DateTime today = DateTime.Today, from = today.AddDays(-6);
            var live = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dir in ClaudeBridge.ConfigDirs())
            {
                string root = Path.Combine(dir, "projects");
                if (!Directory.Exists(root)) continue;
                string source = EditorName(dir);
                foreach (var path in Directory.GetFiles(root, "*.jsonl", SearchOption.AllDirectories)) // includes subagents
                {
                    var fi = new FileInfo(path);
                    if (fi.LastWriteTime < from) continue;
                    live.Add(path);
                    FileState s;
                    if (!files.TryGetValue(path, out s)) files[path] = s = new FileState { Source = source };
                    try { ReadNew(s, fi); } catch (Exception ex) { App.Log(ex); }
                }
            }
            foreach (var k in new List<string>(files.Keys)) if (!live.Contains(k)) files.Remove(k);

            var st = new Stats();
            var bySource = new Dictionary<string, long>();
            var byModel = new Dictionary<string, long>();
            DateTime cut5 = DateTime.Now.AddHours(-5);
            foreach (var s in files.Values)
            {
                foreach (var h in s.Hours)
                {
                    int d = (int)(h.Key.Date - from).TotalDays;
                    if (d < 0 || d > 6) continue;
                    st.Week[d] += h.Value;
                    if (h.Key.AddHours(1) > cut5) st.Last5h += h.Value;
                    long v; bySource.TryGetValue(s.Source, out v); bySource[s.Source] = v + h.Value;
                }
                foreach (var r in s.Replies) { int d = (int)(r.Key - from).TotalDays; if (d >= 0 && d <= 6) st.Replies[d] += r.Value; }
                foreach (var m in s.Models)
                {
                    DateTime day;
                    if (!DateTime.TryParseExact(m.Key.Substring(0, 8), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day) || day < from) continue;
                    string name = ClaudeHistory.ShortModel(m.Key.Substring(9)) ?? "Other";
                    long v; byModel.TryGetValue(name, out v); byModel[name] = v + m.Value;
                }
            }
            st.Sources = Sorted(bySource);
            st.Models = Sorted(byModel);
            return st;
        }

        static List<KeyValuePair<string, long>> Sorted(Dictionary<string, long> d)
        {
            var l = new List<KeyValuePair<string, long>>();
            foreach (var kv in d) if (kv.Value > 0) l.Add(kv);
            l.Sort((a, b) => b.Value.CompareTo(a.Value));
            return l;
        }

        static void ReadNew(FileState s, FileInfo f)
        {
            if (f.Length < s.Offset) { s.Offset = 0; s.Partial = ""; s.LastMsgId = null; s.Hours.Clear(); s.Replies.Clear(); s.Models.Clear(); } // rewritten
            if (f.Length == s.Offset) return;
            using (var fs = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                fs.Seek(s.Offset, SeekOrigin.Begin);
                var buf = new byte[1 << 20];
                int n;
                while ((n = fs.Read(buf, 0, buf.Length)) > 0)
                {
                    string chunk = s.Partial + Encoding.UTF8.GetString(buf, 0, n);
                    int start = 0, nl;
                    while ((nl = chunk.IndexOf('\n', start)) >= 0) { Line(s, chunk.Substring(start, nl - start)); start = nl + 1; }
                    s.Partial = chunk.Substring(start);
                    s.Offset += n;
                }
            }
        }

        static void Line(FileState s, string line)
        {
            if (!rxAssistant.IsMatch(line)) return;
            // one reply is written as several lines repeating the same usage: count it once
            var id = rxMsgId.Match(line);
            string mid = id.Success ? id.Groups[1].Value : null;
            if (mid != null && mid == s.LastMsgId) return;
            s.LastMsgId = mid;
            var tm = rxTime.Match(line);
            DateTimeOffset t;
            if (!tm.Success || !DateTimeOffset.TryParse(tm.Groups[1].Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) return;
            DateTime local = t.LocalDateTime;
            if (local < DateTime.Today.AddDays(-7)) return;
            long tokens = Num(rxIn, line) + Num(rxOut, line) + Num(rxCacheNew, line);
            var hour = new DateTime(local.Year, local.Month, local.Day, local.Hour, 0, 0);
            long v; s.Hours.TryGetValue(hour, out v); s.Hours[hour] = v + tokens;
            int r; s.Replies.TryGetValue(local.Date, out r); s.Replies[local.Date] = r + 1;
            var mm = rxModel.Match(line);
            if (mm.Success)
            {
                string key = local.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "|" + mm.Groups[1].Value;
                long m; s.Models.TryGetValue(key, out m); s.Models[key] = m + tokens;
            }
        }

        static long Num(Regex rx, string line)
        {
            var m = rx.Match(line);
            long v;
            return m.Success && long.TryParse(m.Groups[1].Value, out v) ? v : 0;
        }

        // ------------------------------------------------------------------ --snapshot --demo

        public static void UseDemo()
        {
            demo = true;
            Loaded = true;
            var now = DateTime.Now;
            Limits = new List<Limit>
            {
                new Limit { Name = "Current session", Percent = 34, Resets = now.AddHours(2).AddMinutes(14) },
                new Limit { Name = "This week · all models", Percent = 58, Resets = now.Date.AddDays(3).AddHours(0.5) },
            };
            LimitsAt = now.AddMinutes(-6);
            LimitsFrom = "VS Code";
            Week = new Stats
            {
                Week = new long[] { 1200000, 2650000, 900000, 3100000, 2200000, 400000, 1850000 },
                Replies = new[] { 40, 96, 31, 120, 77, 12, 64 },
                Last5h = 1420000,
                Sources = new List<KeyValuePair<string, long>> { new KeyValuePair<string, long>("VS Code", 9800000), new KeyValuePair<string, long>("Desktop & CLI", 2300000) },
                Models = new List<KeyValuePair<string, long>> { new KeyValuePair<string, long>("Opus 5.5", 8400000), new KeyValuePair<string, long>("Sonnet 5.5", 3700000) },
            };
        }
    }
}
