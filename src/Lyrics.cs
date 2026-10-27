using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Harbor
{
    /// <summary>
    /// Song lyrics from LRCLIB (lrclib.net: free, open, no account or key). This is Harbor's only network request:
    /// it sends the title, artist, album and length of the song you're playing, only while the media panel is open,
    /// and only when "Lyrics" is on. Results are kept in memory for the last 30 songs.
    /// </summary>
    static class Lyrics
    {
        public sealed class Line { public TimeSpan At; public string Text; }

        public sealed class Result
        {
            public List<Line> Synced;   // time-stamped lines, or null
            public string Plain;        // plain text when there are no timings, or null
            public bool Instrumental;
            public bool NotFound, Failed;
        }

        public enum State { Off, Loading, Ready }

        static readonly Dictionary<string, Result> cache = new Dictionary<string, Result>();
        static readonly List<string> order = new List<string>();
        static string loadingKey;

        public static event Action Changed;

        static string Key(string artist, string title) { return ((artist ?? "") + "\u0001" + (title ?? "")).ToLowerInvariant(); }

        /// <summary>Lyrics for the current song: Ready with a result, Loading (a request is on its way), or Off.</summary>
        public static State For(out Result result)
        {
            result = null;
            if (!Look.Bool("Lyrics") || !Media.Has) return State.Off;
            if (Media.Demo) { result = Demo(); return State.Ready; }
            string key = Key(Media.Artist, Media.Title);
            if (cache.TryGetValue(key, out result)) return State.Ready;
            var saved = FromDisk(key);
            if (saved != null) { Store(key, saved); result = saved; return State.Ready; } // played before: no request
            if (loadingKey != key) Fetch(key, Media.Artist, Media.Title, Media.Album, Media.Duration);
            return State.Loading;
        }

        static void Fetch(string key, string artist, string title, string album, TimeSpan duration)
        {
            loadingKey = key;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                Result r;
                try { r = Lookup(artist, title, album, duration); }
                catch (Exception ex) { App.Log(ex); r = new Result { Failed = true }; }
                ToDisk(key, r);
                App.Post(() =>
                {
                    if (loadingKey == key) loadingKey = null;
                    Store(key, r);
                    if (Changed != null) Changed();
                });
            });
        }

        static void Store(string key, Result r)
        {
            if (r.Failed)
            {
                // keep the failure only briefly so reopening the panel later tries again
                cache[key] = r;
                var t = new System.Windows.Forms.Timer { Interval = 15000 };
                t.Tick += delegate { t.Stop(); t.Dispose(); Result cur; if (cache.TryGetValue(key, out cur) && cur == r) cache.Remove(key); };
                t.Start();
                return;
            }
            cache[key] = r;
            order.Remove(key);
            order.Add(key);
            while (order.Count > 30) { cache.Remove(order[0]); order.RemoveAt(0); }
        }

        /// <summary>Developer check: Harbor.exe --lyrics-test &lt;out file&gt; &lt;artist&gt; &lt;title&gt; [seconds] writes what the lookup found.</summary>
        public static void Test(string outFile, string artist, string title, int seconds)
        {
            var sb = new StringBuilder();
            try
            {
                var r = Lookup(artist, title, null, TimeSpan.FromSeconds(seconds));
                sb.AppendLine(r.Failed ? "FAILED (network)" : r.NotFound ? "NOT FOUND" : r.Instrumental ? "INSTRUMENTAL"
                    : r.Synced != null ? "SYNCED " + r.Synced.Count + " lines, first at " + r.Synced[0].At + ", last at " + r.Synced[r.Synced.Count - 1].At
                    : "PLAIN " + r.Plain.Split('\n').Length + " lines");
            }
            catch (Exception ex) { sb.AppendLine("ERROR " + ex); }
            File.WriteAllText(outFile, sb.ToString());
        }

        /// <summary>--snapshot --demo: placeholder lines (not a real song's lyrics), no network.</summary>
        static Result Demo()
        {
            var text = new[] { "City lights are humming low", "Every window keeps a glow", "We drive until the morning finds us",
                               "Radio on, the world behind us", "Hold the moment, let it stay", "Midnight carries us away", "", "Streets that shimmer in the rain" };
            var lines = new List<Line>();
            for (int i = 0; i < text.Length; i++) lines.Add(new Line { At = TimeSpan.FromSeconds(56 + i * 5), Text = text[i] });
            return new Result { Synced = lines };
        }

        // ------------------------------------------------------------------ LRCLIB

        const string Api = "https://lrclib.net/api/";

        /// <summary>
        /// LRCLIB, asked in order of precision: the exact song (title + artist + length; no album, which often differs
        /// between Spotify and LRCLIB), the same with just the main artist and a cleaned title, a search, and a free-text
        /// search. A busy server (503, timeouts) is retried; only if it stays unreachable is the result "Failed".
        /// </summary>
        static Result Lookup(string artist, string title, string album, TimeSpan duration)
        {
            if (string.IsNullOrWhiteSpace(title)) return new Result { NotFound = true };
            // "Song (feat. X)", "Song - Remastered 2011", "Song (From "Movie")", "Song - From "Movie"" → "Song"
            string clean = Regex.Replace(title, @"\s*[\(\[][^\)\]]*\b(feat|ft\.|with|remaster|version|live|edit|mix|from)\b[^\)\]]*[\)\]]|\s+-\s+.*\b(remaster|version|live|edit|mix|from)\b.*$", "", RegexOptions.IgnoreCase).Trim();
            if (clean == "") clean = title;
            string mainArtist = Regex.Split(artist ?? "", @"\s*(?:,|&|\bfeat\.?|\bft\.|\bx\b|\band\b)\s*", RegexOptions.IgnoreCase)[0].Trim();
            string len = duration > TimeSpan.Zero ? "&duration=" + (int)Math.Round(duration.TotalSeconds) : "";
            bool busy = false;

            // 1) exact matches
            var tries = new List<string[]> { new[] { title, artist ?? "" } };
            if (mainArtist != (artist ?? "")) tries.Add(new[] { title, mainArtist });
            if (clean != title) tries.Add(new[] { clean, mainArtist });
            foreach (var t in tries)
            {
                int status;
                string json = Ask(Api + "get?track_name=" + Uri.EscapeDataString(t[0]) + "&artist_name=" + Uri.EscapeDataString(t[1]) + len, out status);
                if (status == 200) { var r = FromJson(new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json)); if (r != null) return r; }
                else if (status != 404) busy = true;
            }

            // 2) search by title + main artist, closest length wins
            int s2;
            string j2 = Ask(Api + "search?track_name=" + Uri.EscapeDataString(clean) + "&artist_name=" + Uri.EscapeDataString(mainArtist), out s2);
            var best = s2 == 200 ? Pick(j2, duration, null, 10) : null; // same title and artist: a few seconds apart is the same song
            if (s2 != 200 && s2 != 404) busy = true;

            // 3) free-text search ("title artist") catches songs filed under a different name. Looser, so a match
            //    must have the same length (within 4 s), or the same title when the app doesn't report a length.
            if (best == null)
            {
                int s3;
                string j3 = Ask(Api + "search?q=" + Uri.EscapeDataString(clean + " " + mainArtist), out s3);
                if (s3 == 200) best = Pick(j3, duration, clean, 4);
                else if (s3 != 404) busy = true;
            }
            var found = best != null ? FromJson(best) : null;
            if (found != null) return found;
            // the server was busy on some step: don't conclude it has no lyrics, try again later
            return busy ? new Result { Failed = true } : new Result { NotFound = true };
        }

        /// <summary>A GET that waits out a busy server: retries 503 / 429 / timeouts twice, with a short pause.</summary>
        static string Ask(string url, out int status)
        {
            string json = null;
            status = 0;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (attempt > 0) System.Threading.Thread.Sleep(attempt == 1 ? 700 : 1600);
                json = Web.Get(url, out status);
                if (status == 200 || status == 404 || (status >= 400 && status < 500 && status != 429)) break;
            }
            return json;
        }

        // ------------------------------------------------------------------ remembered on disk

        // Lyrics found once are kept in %LOCALAPPDATA%\Harbor\lyrics: a song you've played before shows its lyrics
        // instantly with no request. "Not on LRCLIB" is remembered for 3 days, then looked up again.
        static string CacheDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Harbor", "lyrics"); } }

        static string CacheFile(string key)
        {
            using (var sha = System.Security.Cryptography.SHA1.Create())
                return Path.Combine(CacheDir, BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(key))).Replace("-", "").Substring(0, 24) + ".txt");
        }

        static Result FromDisk(string key)
        {
            try
            {
                string f = CacheFile(key);
                if (!File.Exists(f)) return null;
                string text = File.ReadAllText(f, Encoding.UTF8);
                if (text.Length < 1) return null;
                string body = text.Length > 2 ? text.Substring(2) : "";
                switch (text[0])
                {
                    case 'S': var lines = Parse(body); return lines != null && lines.Count > 0 ? new Result { Synced = lines } : null;
                    case 'P': return new Result { Plain = body };
                    case 'I': return new Result { Instrumental = true };
                    case 'N': return (DateTime.Now - File.GetLastWriteTime(f)).TotalDays < 3 ? new Result { NotFound = true } : null;
                }
            }
            catch { }
            return null;
        }

        static void ToDisk(string key, Result r)
        {
            if (r.Failed || Media.Demo) return;
            try
            {
                Directory.CreateDirectory(CacheDir);
                var sb = new StringBuilder();
                if (r.Synced != null)
                {
                    sb.Append("S\n");
                    foreach (var l in r.Synced)
                        sb.Append('[').Append(((int)l.At.TotalMinutes).ToString("00")).Append(':').Append(l.At.Seconds.ToString("00")).Append('.')
                          .Append((l.At.Milliseconds / 10).ToString("00")).Append(']').Append(l.Text).Append('\n');
                }
                else if (r.Plain != null) sb.Append("P\n").Append(r.Plain);
                else if (r.Instrumental) sb.Append("I\n");
                else sb.Append("N\n");
                File.WriteAllText(CacheFile(key), sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex) { App.Log(ex); }
        }

        /// <summary>"Try again" after a failed lookup.</summary>
        public static void Retry()
        {
            if (!Media.Has) return;
            string key = Key(Media.Artist, Media.Title);
            Result r;
            if (cache.TryGetValue(key, out r) && (r.Failed || r.NotFound)) cache.Remove(key);
            if (r != null && r.NotFound) try { File.Delete(CacheFile(key)); } catch { }
            if (Changed != null) Changed();
        }

        /// <summary>The closest-length result (synced lyrics preferred); null if none is the same recording.</summary>
        static Dictionary<string, object> Pick(string json, TimeSpan duration, string mustContainTitle, double tolerance)
        {
            var list = new JavaScriptSerializer().Deserialize<List<Dictionary<string, object>>>(json);
            Dictionary<string, object> best = null;
            double bestScore = double.MaxValue;
            foreach (var item in list)
            {
                double d = duration > TimeSpan.Zero ? Math.Abs(Num(item, "duration") - duration.TotalSeconds) : 0;
                if (duration > TimeSpan.Zero && d > tolerance) continue;      // a different recording
                if (duration <= TimeSpan.Zero && mustContainTitle != null)
                {
                    object tn;
                    string name = item.TryGetValue("trackName", out tn) ? tn as string : null;
                    if (name == null || name.IndexOf(mustContainTitle, StringComparison.OrdinalIgnoreCase) < 0) continue;
                }
                double score = d + (item.ContainsKey("syncedLyrics") && item["syncedLyrics"] != null ? 0 : 100); // prefer synced
                if (score < bestScore) { bestScore = score; best = item; }
            }
            return best;
        }

        static double Num(Dictionary<string, object> d, string k)
        {
            object v;
            if (!d.TryGetValue(k, out v) || v == null) return double.MaxValue;
            try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); } catch { return double.MaxValue; }
        }

        static Result FromJson(Dictionary<string, object> d)
        {
            if (d == null) return null;
            object v;
            if (d.TryGetValue("instrumental", out v) && v is bool && (bool)v) return new Result { Instrumental = true };
            string synced = d.TryGetValue("syncedLyrics", out v) ? v as string : null;
            string plain = d.TryGetValue("plainLyrics", out v) ? v as string : null;
            var lines = Parse(synced);
            if (lines != null && lines.Count > 0) return new Result { Synced = lines };
            if (!string.IsNullOrWhiteSpace(plain)) return new Result { Plain = plain.Trim() };
            return null;
        }

        static readonly Regex stamp = new Regex(@"\[(\d+):(\d+(?:[.:]\d+)?)\]");

        /// <summary>LRC: "[mm:ss.xx] text" (several stamps may share one line); metadata tags like [ar:] are skipped.</summary>
        public static List<Line> Parse(string lrc)
        {
            if (string.IsNullOrWhiteSpace(lrc)) return null;
            var lines = new List<Line>();
            foreach (var raw in lrc.Split('\n'))
            {
                var ms = stamp.Matches(raw);
                if (ms.Count == 0) continue;
                string text = stamp.Replace(raw, "").Trim();
                foreach (Match m in ms)
                {
                    double sec;
                    if (!double.TryParse(m.Groups[2].Value.Replace(':', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out sec)) continue;
                    lines.Add(new Line { At = TimeSpan.FromSeconds(int.Parse(m.Groups[1].Value) * 60 + sec), Text = text });
                }
            }
            lines.Sort((a, b) => a.At.CompareTo(b.At));
            return lines;
        }

    }

    /// <summary>Plain HTTPS GET for Harbor's few web lookups (lyrics, weather). It blocks, so call it off the UI thread.</summary>
    static class Web
    {
        public static string Get(string url, out int status)
        {
            // .NET Framework apps built without a target-framework attribute default to old TLS versions
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | (SecurityProtocolType)12288; // TLS 1.3
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "Harbor/" + SettingsWindow.Version + " (" + SettingsWindow.RepoUrl + ")";
            req.Timeout = 6000;
            req.ReadWriteTimeout = 6000;
            // gzip only: .NET Framework misreads servers' "deflate" (zlib-wrapped) replies - Open-Meteo sends those
            req.AutomaticDecompression = DecompressionMethods.GZip;
            try
            {
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var rd = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    status = (int)resp.StatusCode;
                    return rd.ReadToEnd();
                }
            }
            catch (WebException ex)
            {
                var resp = ex.Response as HttpWebResponse;
                status = resp != null ? (int)resp.StatusCode : 0;
                if (resp != null) resp.Close();
                return null;
            }
        }
    }
}
