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
                var t = new System.Windows.Forms.Timer { Interval = 30000 };
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

        static Result Lookup(string artist, string title, string album, TimeSpan duration)
        {
            if (string.IsNullOrWhiteSpace(title)) return new Result { NotFound = true };
            // 1) exact match on title + artist (+ album and length when known)
            var q = new StringBuilder("get?track_name=").Append(Uri.EscapeDataString(title))
                .Append("&artist_name=").Append(Uri.EscapeDataString(artist ?? ""));
            if (!string.IsNullOrWhiteSpace(album)) q.Append("&album_name=").Append(Uri.EscapeDataString(album));
            if (duration > TimeSpan.Zero) q.Append("&duration=").Append((int)Math.Round(duration.TotalSeconds));
            int status;
            string json = Web.Get(Api + q, out status);
            if (status == 200) { var r = FromJson(new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json)); if (r != null) return r; }
            else if (status != 404) return new Result { Failed = true };

            // 2) search with a cleaned title ("Song - Remastered 2011", "Song (feat. X)") and pick the closest length
            string clean = Regex.Replace(title, @"\s*[\(\[].*?(feat|ft\.|with|remaster|version|live|edit|mix).*?[\)\]]|\s+-\s+.*(remaster|version|live|edit|mix).*$", "", RegexOptions.IgnoreCase).Trim();
            string mainArtist = Regex.Split(artist ?? "", @"\s*(,|&|feat\.?|ft\.|x)\s+", RegexOptions.IgnoreCase)[0];
            json = Web.Get(Api + "search?track_name=" + Uri.EscapeDataString(clean) + "&artist_name=" + Uri.EscapeDataString(mainArtist), out status);
            if (status != 200 && status != 404) return new Result { Failed = true };
            var best = status == 200 ? Pick(json, duration, null) : null;

            // 3) free-text search ("title artist") catches songs filed under a different name. Looser, so a match
            //    must have the same length (within 4 s), or the same title when the app doesn't report a length.
            if (best == null)
            {
                json = Web.Get(Api + "search?q=" + Uri.EscapeDataString(clean + " " + mainArtist), out status);
                if (status != 200 && status != 404) return new Result { Failed = true };
                if (status == 200) best = Pick(json, duration, clean);
            }
            return (best != null ? FromJson(best) : null) ?? new Result { NotFound = true };
        }

        /// <summary>The closest-length result (synced lyrics preferred); null if none is the same recording.</summary>
        static Dictionary<string, object> Pick(string json, TimeSpan duration, string mustContainTitle)
        {
            var list = new JavaScriptSerializer().Deserialize<List<Dictionary<string, object>>>(json);
            Dictionary<string, object> best = null;
            double bestScore = double.MaxValue;
            foreach (var item in list)
            {
                double d = duration > TimeSpan.Zero ? Math.Abs(Num(item, "duration") - duration.TotalSeconds) : 0;
                if (duration > TimeSpan.Zero && d > 4) continue;              // a different recording
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
