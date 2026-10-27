using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Harbor
{
    /// <summary>
    /// Screen Time: how long each app was in front today, grouped into categories. Counted locally from the
    /// foreground window every 15 s; time away from the PC (no input for 2 minutes, unless media is playing),
    /// locked or asleep isn't counted. Kept in %APPDATA%\Harbor\screentime\yyyy-MM-dd.tsv and never sent anywhere.
    /// </summary>
    static class ScreenTime
    {
        public sealed class AppUse { public string Key, Name, Icon; public double Seconds; }

        static readonly Dictionary<string, AppUse> apps = new Dictionary<string, AppUse>();
        static DateTime day = DateTime.Today, lastCount = DateTime.Now, lastSave = DateTime.Now;
        static string curKey;
        static DateTime curSince = DateTime.Now;
        public static TimeSpan Longest;      // longest stretch in one app today
        public static int Switches;          // app switches today
        static System.Windows.Forms.Timer tick;
        static bool dirty;

        static string Dir { get { return Path.Combine(Settings.Dir, "screentime"); } }
        static string FileFor(DateTime d) { return Path.Combine(Dir, d.ToString("yyyy-MM-dd") + ".tsv"); }

        public static void Init()
        {
            if (tick != null || !Look.Bool("ScreenTime")) return;
            Load();
            Tracker.ForegroundChanged += OnSwitch;
            tick = new System.Windows.Forms.Timer { Interval = 15000 };
            tick.Tick += delegate { Count(); };
            tick.Start();
            OnSwitch();
        }

        public static void Shutdown() { if (tick == null) return; Count(); Save(); }

        static bool demo;

        /// <summary>--snapshot --demo: sample usage, nothing recorded or saved.</summary>
        public static void UseDemo()
        {
            demo = true;
            apps.Clear();
            var sample = new[] { "Visual Studio Code|9000", "Google Chrome|5200", "Spotify|3100", "WhatsApp|1900", "Notion|1500", "File Explorer|700" };
            foreach (var s in sample)
            {
                var p = s.Split('|');
                apps["demo:" + p[0]] = new AppUse { Key = "demo:" + p[0], Name = p[0], Seconds = int.Parse(p[1]) };
            }
            curKey = "demo:Visual Studio Code";
            curSince = DateTime.Now.AddMinutes(-23);
        }

        public static List<AppUse> Today()
        {
            Count();
            var l = new List<AppUse>(apps.Values);
            l.Sort((a, b) => b.Seconds.CompareTo(a.Seconds));
            return l;
        }

        public static double TotalSeconds { get { double t = 0; foreach (var a in apps.Values) t += a.Seconds; return t; } }

        /// <summary>The app in front right now and how long it's been there.</summary>
        public static AppUse Current(out TimeSpan since)
        {
            since = DateTime.Now - curSince;
            AppUse a;
            return curKey != null && apps.TryGetValue(curKey, out a) ? a : null;
        }

        static void OnSwitch()
        {
            Count();
            var w = App.ActiveApp();
            string key = w == null ? null : w.Key;
            if (key == curKey) return;
            var stretch = DateTime.Now - curSince;
            if (curKey != null && stretch > Longest) Longest = stretch;
            curKey = key;
            curSince = DateTime.Now;
            if (key == null) return;
            Switches++;
            if (!apps.ContainsKey(key))
                apps[key] = new AppUse { Key = key, Name = App.ActiveAppName() ?? "App", Icon = w.WebAppIcon ?? (w.Aumid != null ? @"shell:AppsFolder\" + w.Aumid : w.Exe) };
        }

        /// <summary>Adds the time since the last count to the app in front (if you were actually there).</summary>
        static void Count()
        {
            if (demo) return;
            var now = DateTime.Now;
            if (now.Date != day) { Save(); apps.Clear(); day = now.Date; Longest = TimeSpan.Zero; Switches = 0; curSince = now; }
            double secs = Math.Min(30, (now - lastCount).TotalSeconds); // asleep or suspended: the gap isn't screen time
            lastCount = now;
            AppUse a;
            if (secs <= 0 || curKey == null || !apps.TryGetValue(curKey, out a)) return;
            if (IdleSeconds() > 120 && !Media.Playing) return;
            a.Seconds += secs;
            dirty = true;
            if ((now - lastSave).TotalSeconds > 60) Save();
        }

        [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint cbSize, dwTime; }
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO i);

        static double IdleSeconds()
        {
            var i = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO)) };
            return GetLastInputInfo(ref i) ? unchecked((uint)Environment.TickCount - i.dwTime) / 1000.0 : 0;
        }

        static void Save()
        {
            lastSave = DateTime.Now;
            if (!dirty) return;
            try
            {
                Directory.CreateDirectory(Dir);
                var sb = new StringBuilder();
                foreach (var a in apps.Values)
                    sb.Append(((int)a.Seconds).ToString()).Append('\t').Append(a.Key).Append('\t').Append(a.Name).Append('\t').Append(a.Icon ?? "").Append('\n');
                File.WriteAllText(FileFor(day), sb.ToString(), Encoding.UTF8);
                dirty = false;
            }
            catch (Exception ex) { App.Log(ex); }
        }

        static void Load()
        {
            try
            {
                string f = FileFor(day);
                if (!File.Exists(f)) return;
                foreach (var line in File.ReadAllLines(f, Encoding.UTF8))
                {
                    var p = line.Split('\t');
                    int s;
                    if (p.Length < 3 || !int.TryParse(p[0], out s)) continue;
                    apps[p[1]] = new AppUse { Seconds = s, Key = p[1], Name = p[2], Icon = p.Length > 3 && p[3] != "" ? p[3] : null };
                }
            }
            catch (Exception ex) { App.Log(ex); }
        }

        // ------------------------------------------------------------------ categories

        public static readonly string[] Categories = { "Development", "Browsing", "Communication", "Entertainment", "Productivity", "Utilities", "Other" };

        public static readonly System.Drawing.Color[] CategoryColors =
        {
            System.Drawing.Color.FromArgb(255, 255, 159, 10),   // development: orange
            System.Drawing.Color.FromArgb(255, 100, 180, 255),  // browsing: blue
            System.Drawing.Color.FromArgb(255, 48, 209, 88),    // communication: green
            System.Drawing.Color.FromArgb(255, 255, 69, 120),   // entertainment: pink
            System.Drawing.Color.FromArgb(255, 120, 110, 255),  // productivity: indigo
            System.Drawing.Color.FromArgb(255, 152, 152, 160),  // utilities: grey
            System.Drawing.Color.FromArgb(255, 110, 110, 118),  // other
        };

        static readonly string[][] words =
        {
            new[] { "code", "devenv", "idea", "pycharm", "webstorm", "rider", "clion", "goland", "android studio", "antigravity", "cursor", "windsurf", "zed", "sublime", "notepad++", "terminal", "windowsterminal", "powershell", "pwsh", "cmd", "wsl", "git", "docker", "postman", "github", "claude", "codex", "dbeaver", "datagrip", "unity", "unreal" },
            new[] { "chrome", "msedge", "edge", "firefox", "brave", "opera", "vivaldi", "arc", "dia", "browser", "zen", "comet" },
            new[] { "whatsapp", "telegram", "discord", "slack", "teams", "zoom", "skype", "signal", "outlook", "mail", "messenger", "thunderbird" },
            new[] { "spotify", "netflix", "vlc", "steam", "epic", "youtube", "prime", "disney", "hotstar", "music", "media player", "movies", "xbox", "game", "wallpaper", "twitch", "mpv", "plex" },
            new[] { "winword", "word", "excel", "powerpnt", "powerpoint", "onenote", "notion", "obsidian", "acrobat", "pdf", "figma", "photoshop", "illustrator", "canva", "todo", "calendar", "evernote", "chatgpt", "perplexity" },
            new[] { "explorer", "file explorer", "settings", "systemsettings", "taskmgr", "task manager", "control", "calculator", "snipping", "screen sketch", "harbor", "raycast", "everything", "7-zip", "winrar", "regedit" },
        };

        public static int CategoryOf(AppUse a)
        {
            string exe = "";
            try { exe = Path.GetFileNameWithoutExtension(a.Key ?? ""); } catch { }
            string s = " " + System.Text.RegularExpressions.Regex.Replace(((a.Name ?? "") + " " + exe).ToLowerInvariant(), "[^a-z0-9+]+", " ") + " ";
            // whole words only: "dia" mustn't match "Media Player", nor "arc" match "Search"
            for (int c = 0; c < words.Length; c++)
                foreach (var w in words[c]) if (s.Contains(" " + w + " ")) return c;
            return words.Length; // other
        }
    }
}
