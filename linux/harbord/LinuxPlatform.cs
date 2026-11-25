using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Harbor
{
    /// <summary>Linux's side of IPlatform: XDG folders, freedesktop notifications, D-Bus signals for the extension.</summary>
    sealed class LinuxPlatform : IPlatform
    {
        readonly MainLoop loop;
        public Bus Bus;                      // null in --selftest
        public Song Playing = new Song();    // what the extension last told us is playing (MPRIS lives in the shell)

        public LinuxPlatform(MainLoop loop) { this.loop = loop; }

        public static string XdgConfigHome
        {
            get
            {
                string x = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
                return !string.IsNullOrEmpty(x) && Path.IsPathRooted(x) ? x
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            }
        }

        string configDir;
        public string ConfigDir
        {
            get { return configDir ?? (configDir = Path.Combine(XdgConfigHome, "harbor")); }
            set { configDir = value; }
        }

        public bool SnapshotMode { get { return false; } }

        public void Log(Exception ex)
        {
            if (ex == null) return;
            Console.Error.WriteLine("harbord: " + ex);
            try
            {
                Directory.CreateDirectory(ConfigDir);
                string f = Path.Combine(ConfigDir, "error.log");
                if (File.Exists(f) && new FileInfo(f).Length > 256 * 1024) File.Delete(f);
                File.AppendAllText(f, DateTime.Now.ToString("s") + "  " + ex + Environment.NewLine + Environment.NewLine);
            }
            catch { }
        }

        public void Post(Action a) { loop.Enqueue(a); }
        public ITicker NewTicker() { return new LoopTicker(loop); }

        public void Banner(string app, string title, string body)
        {
            if (Bus != null) Bus.Notify(string.IsNullOrEmpty(app) || app == "Harbor" ? "Harbor" : "Harbor · " + app, title, body);
            else Console.WriteLine("[banner] " + app + ": " + title + " - " + body);
        }

        public void ShowActivity(Activity a)
        {
            var d = new Dictionary<string, object>
            {
                { "glyph", a.Glyph }, { "icon", Icons.ForGlyph(a.Glyph) }, { "title", a.Title }, { "detail", a.Detail },
                { "action", a.Action }, { "tint", Look.Hex(a.Tint) }, { "important", a.Important }, { "ms", a.Ms },
            };
            // a click comes back as Call("activity.click", {"id": ...}): keep the callback a little while
            if (a.Click != null) d["id"] = Api.RememberClick(a.Click);
            if (Bus != null) Bus.EmitActivity(Json.Write(d));
            else Console.WriteLine("[activity] " + Json.Write(d));
        }

        /// <summary>The extension draws the activity; with nothing listening it's simply dropped.</summary>
        public bool CanShowActivity { get { return Bus == null || Bus.ExtensionListening; } }

        public void PlaySound(Sound s)
        {
            // libcanberra's player is on most GNOME and KDE installs; without it, stay quiet
            Run("canberra-gtk-play", "-i", s == Sound.Exclamation ? "dialog-warning" : "message-new-instant");
        }

        public void Open(string target) { if (!string.IsNullOrEmpty(target)) Run("xdg-open", target); }

        public void OpenTab(string tab) { if (Bus != null) Bus.EmitOpenTab(tab); }

        public double IdleMinutes() { return Bus != null ? Bus.IdleMs() / 60000.0 : 0; }

        public bool SystemPrefersDark() { return Bus == null || Bus.PrefersDark(); }

        public void ThemeApplied(bool dark) { }

        public void FocusChanged() { if (Bus != null) Bus.EmitChanged("focus"); }

        public void TrimMemory() { GC.Collect(2, GCCollectionMode.Optimized, false); }

        public Song NowPlaying() { return Playing; }

        public List<string> DefaultPins() { return Desktop.DefaultPins(); }

        /// <summary>Starts a program without waiting for it; a missing one is not an error.</summary>
        public static void Run(string file, params string[] args)
        {
            try
            {
                var psi = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var a in args) psi.ArgumentList.Add(a);
                var p = Process.Start(psi);
                if (p != null) { p.EnableRaisingEvents = true; p.Exited += (s, e) => p.Dispose(); }
            }
            catch (System.ComponentModel.Win32Exception) { } // not installed
            catch (Exception ex) { Core.Log(ex); }
        }

        /// <summary>A timer whose ticks run on the main loop, like a WinForms timer's run on the UI thread.</summary>
        sealed class LoopTicker : ITicker
        {
            readonly MainLoop loop;
            readonly Timer timer;
            int interval = 100;
            int generation; // a tick already queued when Stop() was called is dropped
            bool enabled;
            public event Action Tick;

            public LoopTicker(MainLoop loop)
            {
                this.loop = loop;
                timer = new Timer(_ =>
                {
                    int gen = Volatile.Read(ref generation);
                    loop.Enqueue(() => { if (enabled && gen == generation && Tick != null) Tick(); });
                }, null, Timeout.Infinite, Timeout.Infinite);
            }

            public int Interval
            {
                get { return interval; }
                set { interval = Math.Max(1, value); if (enabled) Restart(); }
            }

            public bool Enabled { get { return enabled; } }
            public void Start() { enabled = true; Restart(); }
            public void Stop() { enabled = false; Interlocked.Increment(ref generation); timer.Change(Timeout.Infinite, Timeout.Infinite); }
            void Restart() { Interlocked.Increment(ref generation); timer.Change(interval, interval); }
            public void Dispose() { Stop(); timer.Dispose(); }
        }
    }

    /// <summary>Installed apps, the freedesktop way: .desktop files in the XDG data folders.</summary>
    static class Desktop
    {
        /// <summary>Every folder that can hold .desktop files, most specific first.</summary>
        public static List<string> AppDirs()
        {
            var dirs = new List<string>();
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (string.IsNullOrEmpty(dataHome)) dataHome = Path.Combine(home, ".local", "share");
            dirs.Add(Path.Combine(dataHome, "applications"));
            string dataDirs = Environment.GetEnvironmentVariable("XDG_DATA_DIRS");
            if (string.IsNullOrEmpty(dataDirs)) dataDirs = "/usr/local/share:/usr/share";
            foreach (var d in dataDirs.Split(':')) if (d != "") dirs.Add(Path.Combine(d, "applications"));
            // Flatpak and Snap apps (normally already in XDG_DATA_DIRS, but not when started outside a session)
            dirs.Add(Path.Combine(home, ".local/share/flatpak/exports/share/applications"));
            dirs.Add("/var/lib/flatpak/exports/share/applications");
            dirs.Add("/var/lib/snapd/desktop/applications");
            var unique = new List<string>();
            foreach (var d in dirs) if (!unique.Contains(d)) unique.Add(d);
            return unique;
        }

        /// <summary>True when an app with this desktop ID ("firefox.desktop") is installed.</summary>
        public static bool Exists(string id)
        {
            foreach (var d in AppDirs()) if (File.Exists(Path.Combine(d, id))) return true;
            return false;
        }

        /// <summary>What a fresh install pins: the first installed app of each kind, on Fedora, Ubuntu and friends.</summary>
        public static List<string> DefaultPins()
        {
            var pins = new List<string>();
            string[][] groups =
            {
                new[] { "org.gnome.Nautilus.desktop", "org.kde.dolphin.desktop", "nemo.desktop", "thunar.desktop" },
                new[] { "firefox.desktop", "org.mozilla.firefox.desktop", "firefox_firefox.desktop", "google-chrome.desktop", "com.google.Chrome.desktop", "chromium.desktop", "chromium_chromium.desktop" },
                new[] { "code.desktop", "com.visualstudio.code.desktop", "code_code.desktop" },
                new[] { "org.gnome.Ptyxis.desktop", "org.gnome.Terminal.desktop", "org.gnome.Console.desktop", "org.kde.konsole.desktop" },
                new[] { "spotify.desktop", "com.spotify.Client.desktop", "spotify_spotify.desktop" },
                new[] { "org.gnome.TextEditor.desktop", "org.gnome.gedit.desktop", "org.kde.kate.desktop" },
                new[] { "org.gnome.Calculator.desktop", "org.kde.kcalc.desktop" },
                new[] { "org.gnome.Settings.desktop", "gnome-control-center.desktop", "systemsettings.desktop" },
            };
            foreach (var g in groups)
                foreach (var id in g)
                    if (Exists(id)) { pins.Add(id); break; }
            return pins;
        }
    }

    /// <summary>Harbor's glyphs are Segoe Fluent Icons code points (Windows). The extension gets a freedesktop icon name too.</summary>
    static class Icons
    {
        static readonly Dictionary<string, string> map = new Dictionary<string, string>
        {
            { "\uE916", "alarm-symbolic" },                 // timer / alarm
            { "\uE787", "x-office-calendar-symbolic" },     // meeting
            { "\uE7B3", "view-reveal-symbolic" },           // rest your eyes
            { "\uE805", "emoji-people-symbolic" },          // stand up
            { "\uEB42", "weather-showers-symbolic" },       // drink water
            { "\uE896", "folder-download-symbolic" },       // download finished
            { "\uE945", "battery-full-charging-symbolic" }, // charging
            { "\uE702", "bluetooth-active-symbolic" },      // headphones
        };

        public static string ForGlyph(string glyph)
        {
            string name;
            return glyph != null && map.TryGetValue(glyph, out name) ? name : "dialog-information-symbolic";
        }
    }
}
