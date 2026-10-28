using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace Harbor
{
    /// <summary>
    /// In-app updates from the project's GitHub releases. Once a day (if "UpdateCheck" is on) Harbor asks GitHub for the
    /// list of releases; nothing about you or your PC is sent. When a newer one exists it says so once, and installs it
    /// only when you click: the zip is downloaded over HTTPS, checked against GitHub's SHA-256, unpacked to %TEMP% and
    /// installed with its own "--install", which restarts Harbor.
    /// </summary>
    static class Updates
    {
        public sealed class Release
        {
            public string Version, Notes, ZipUrl, Sha256, Page;
            public long Size;
        }

        public static Release Available;   // newer than this build, or null
        public static string Status;       // shown under "Check for Updates"
        public static bool Busy;           // checking, downloading or installing
        public static int Percent = -1;    // download progress
        public static event Action Changed;

        const string Api = "https://api.github.com/repos/Hritik-sharmaa/harbor/releases?per_page=10";
        static Timer daily;

        static string WorkDir { get { return Path.Combine(Path.GetTempPath(), "Harbor-update"); } }

        public static void Init()
        {
            try { if (Directory.Exists(WorkDir)) Directory.Delete(WorkDir, true); } catch { } // what's left of the last update
            // first look a couple of minutes after start (not during login), then every few hours if a day has passed
            var first = new Timer { Interval = 2 * 60 * 1000 };
            first.Tick += delegate { first.Dispose(); Auto(); };
            first.Start();
            daily = new Timer { Interval = 4 * 3600 * 1000 };
            daily.Tick += delegate { Auto(); };
            daily.Start();
        }

        static void Auto()
        {
            if (!Look.Bool("UpdateCheck") || Busy) return;
            DateTime last;
            if (DateTime.TryParse(Look.Get("UpdateChecked"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out last) && (DateTime.Now - last).TotalHours < 23) return;
            Check(false);
        }

        static void Fire() { if (Changed != null) Changed(); }

        public static Version Parse(string v)
        {
            Version r;
            v = (v ?? "").TrimStart('v', 'V');
            int dash = v.IndexOf('-');
            if (dash > 0) v = v.Substring(0, dash);
            return System.Version.TryParse(v.IndexOf('.') < 0 ? v + ".0" : v, out r) ? r : null;
        }

        /// <summary>Asks GitHub for the newest release. manual: say "up to date" too.</summary>
        public static void Check(bool manual)
        {
            if (Busy) return;
            Busy = true;
            Status = "Checking…";
            Fire();
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                Release best = null;
                string err = null;
                try
                {
                    int status;
                    string json = Web.Get(Api, out status);
                    if (json == null) err = status == 403 ? "GitHub is busy, try again later" : "Couldn't reach GitHub";
                    else best = Newest(json, SettingsWindow.Version);
                }
                catch (Exception ex) { App.Log(ex); err = "Couldn't read the release list"; }
                App.Post(() =>
                {
                    Busy = false;
                    if (err != null) { Status = err; Fire(); return; }
                    Look.Set("UpdateChecked", DateTime.Now.ToString("o", CultureInfo.InvariantCulture));
                    Settings.Save();
                    Available = best;
                    Status = best != null ? null : "Harbor is up to date · checked " + DateTime.Now.ToString("t", CultureInfo.CurrentCulture);
                    if (best != null && Look.Get("UpdateNotified") != best.Version)
                    {
                        Look.Set("UpdateNotified", best.Version);
                        Settings.Save();
                        if (!manual) Banners.ShowLocal("Harbor", "Harbor " + best.Version + " is available", "Install it from the Harbor menu (top left) or Harbor → About.");
                    }
                    Fire();
                });
            });
        }

        static Release Newest(string json, string current)
        {
            var cur = Parse(current);
            var list = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(json) as object[];
            Release best = null;
            Version bestV = cur;
            if (list == null) return null;
            foreach (var o in list)
            {
                var r = o as Dictionary<string, object>;
                if (r == null || true.Equals(Get(r, "draft"))) continue;
                var v = Parse(Get(r, "tag_name") as string);
                if (v == null || bestV == null || v <= bestV) continue;
                var assets = Get(r, "assets") as object[];
                if (assets == null) continue;
                foreach (var ao in assets)
                {
                    var a = ao as Dictionary<string, object>;
                    string name = a == null ? null : Get(a, "name") as string;
                    if (name == null || !name.StartsWith("Harbor", StringComparison.OrdinalIgnoreCase) || !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
                    string digest = Get(a, "digest") as string;
                    bestV = v;
                    best = new Release
                    {
                        Version = v.ToString(),
                        ZipUrl = Get(a, "browser_download_url") as string,
                        Size = Convert.ToInt64(Get(a, "size") ?? 0L, CultureInfo.InvariantCulture),
                        Sha256 = digest != null && digest.StartsWith("sha256:") ? digest.Substring(7).ToLowerInvariant() : null,
                        Page = Get(r, "html_url") as string,
                        Notes = NotesFor(Get(r, "body") as string, v.ToString()),
                    };
                    break;
                }
            }
            return best != null && best.ZipUrl != null && best.ZipUrl.StartsWith("https://github.com/") ? best : null;
        }

        static object Get(Dictionary<string, object> d, string k) { object v; return d.TryGetValue(k, out v) ? v : null; }

        /// <summary>The release body is the whole changelog: keep just this version's section, as plain text.</summary>
        static string NotesFor(string body, string version)
        {
            if (string.IsNullOrEmpty(body)) return null;
            body = body.Replace("\r\n", "\n");
            int at = body.IndexOf("## " + version, StringComparison.Ordinal);
            if (at < 0) return null;
            int start = body.IndexOf('\n', at);
            int end = body.IndexOf("\n## ", start < 0 ? at : start, StringComparison.Ordinal);
            string s = start < 0 ? "" : (end < 0 ? body.Substring(start) : body.Substring(start, end - start));
            var lines = new List<string>();
            foreach (var raw in s.Split('\n'))
            {
                string l = raw.Trim();
                if (l == "") continue;
                l = l.Replace("**", "").Replace("`", "");
                if (l.StartsWith("- ")) l = "• " + l.Substring(2);
                lines.Add(l);
            }
            return string.Join("\n", lines.ToArray());
        }

        // ------------------------------------------------------------------ installing

        /// <summary>Downloads, verifies and installs the available release (Harbor restarts).</summary>
        public static void Install()
        {
            var rel = Available;
            if (rel == null || Busy) return;
            Busy = true;
            Percent = 0;
            Status = "Downloading Harbor " + rel.Version + "…";
            Fire();
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                string exe;
                string err = Prepare(rel, out exe);
                App.Post(() =>
                {
                    Percent = -1;
                    if (err != null) { Busy = false; Status = err; Fire(); return; }
                    Status = "Installing… Harbor will restart";
                    Fire();
                    try { Process.Start(exe, "--install"); } // closes this copy, copies itself into place and starts
                    catch (Exception ex) { App.Log(ex); Busy = false; Status = "Couldn't start the installer"; Fire(); }
                });
            });
        }

        /// <summary>Downloads, checks and unpacks a release into %TEMP%; returns an error, or null and the new Harbor.exe.</summary>
        static string Prepare(Release rel, out string exe)
        {
            exe = null;
            try
            {
                string dir = Path.Combine(WorkDir, rel.Version), zip = Path.Combine(dir, "Harbor.zip"), files = Path.Combine(dir, "files");
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Directory.CreateDirectory(dir);
                string err = Download(rel, zip) ?? Verify(rel, zip);
                if (err != null) return err;
                System.IO.Compression.ZipFile.ExtractToDirectory(zip, files);
                exe = Path.Combine(files, "Harbor.exe");
                if (!File.Exists(exe)) { exe = null; return "The download didn't contain Harbor.exe"; }
                return null;
            }
            catch (Exception ex) { App.Log(ex); return "The update couldn't be installed: " + ex.Message; }
        }

        /// <summary>Developer check: Harbor.exe --update-test &lt;out file&gt; &lt;pretend version&gt; finds, downloads, checks and unpacks (never installs).</summary>
        public static void Test(string outFile, string pretend)
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                int status;
                string json = Web.Get(Api, out status);
                var rel = json == null ? null : Newest(json, pretend);
                if (rel == null) sb.AppendLine("NO NEWER RELEASE (status " + status + ")");
                else
                {
                    sb.AppendLine("FOUND " + rel.Version + " " + rel.ZipUrl + " size " + rel.Size + " sha256 " + (rel.Sha256 ?? "(none)"));
                    sb.AppendLine("NOTES:");
                    sb.AppendLine(rel.Notes);
                    string exe, err = Prepare(rel, out exe);
                    sb.AppendLine(err != null ? "ERROR " + err : "READY " + exe + " (" + new FileInfo(exe).Length + " bytes)");
                }
            }
            catch (Exception ex) { sb.AppendLine("ERROR " + ex); }
            File.WriteAllText(outFile, sb.ToString());
        }

        static string Download(Release rel, string path)
        {
            // GitHub's download host has several addresses and some networks can't reach one of them. Windows waits
            // ~21 s on such an address before .NET tries the next, so give each attempt longer than that, and retry once.
            for (int attempt = 0; ; attempt++)
            {
                try { return DownloadOnce(rel, path); }
                catch (WebException ex)
                {
                    if (attempt >= 1 || ex.Status != WebExceptionStatus.Timeout && ex.Status != WebExceptionStatus.ConnectFailure) throw;
                }
            }
        }

        static string DownloadOnce(Release rel, string path)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | (SecurityProtocolType)12288;
            var req = (HttpWebRequest)WebRequest.Create(rel.ZipUrl);
            req.UserAgent = "Harbor/" + SettingsWindow.Version + " (" + SettingsWindow.RepoUrl + ")";
            req.Timeout = 60000;
            req.ReadWriteTimeout = 30000;
            using (var resp = (HttpWebResponse)req.GetResponse())
            {
                // GitHub hands the file out from its own download host
                string host = resp.ResponseUri.Host;
                if (!host.EndsWith("github.com", StringComparison.OrdinalIgnoreCase) && !host.EndsWith("githubusercontent.com", StringComparison.OrdinalIgnoreCase))
                    return "The download came from an unexpected place (" + host + ")";
                long total = resp.ContentLength > 0 ? resp.ContentLength : rel.Size, got = 0;
                int lastPct = -1;
                using (var s = resp.GetResponseStream())
                using (var f = File.Create(path))
                {
                    var buf = new byte[81920];
                    int n;
                    while ((n = s.Read(buf, 0, buf.Length)) > 0)
                    {
                        f.Write(buf, 0, n);
                        got += n;
                        if (got > 64L * 1024 * 1024) return "The download is much bigger than expected";
                        int pct = total > 0 ? (int)(got * 100 / total) : 0;
                        if (pct != lastPct && pct % 5 == 0) { lastPct = pct; App.Post(() => { Percent = pct; Fire(); }); }
                    }
                }
                if (rel.Size > 0 && got != rel.Size) return "The download was incomplete";
            }
            return null;
        }

        static string Verify(Release rel, string path)
        {
            if (rel.Sha256 == null) return null; // releases made before GitHub published digests
            using (var sha = SHA256.Create())
            using (var f = File.OpenRead(path))
            {
                string hex = BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "").ToLowerInvariant();
                return hex == rel.Sha256 ? null : "The download didn't match GitHub's checksum, so it wasn't installed";
            }
        }
    }
}
