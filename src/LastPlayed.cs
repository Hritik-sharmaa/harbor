using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;

namespace Harbor
{
    /// <summary>
    /// The last song you played, kept after its app closes, so the media views can show "Last played" instead of
    /// "Nothing playing", and Play can bring it back: Spotify and other music apps reopen on the song they were on,
    /// so Harbor opens the app and presses play once it's ready. Browser media can't be reopened that way (Windows
    /// never says which page it was), so it offers a YouTube search instead. Saved only when the song changes.
    /// </summary>
    static class LastPlayed
    {
        public static string Title, Artist, App, AppName;
        public static Bitmap Art;
        public static DateTime At;
        public static bool Resuming;
        public static event Action Changed;

        static string key, savedKey;
        static System.Windows.Forms.Timer saveSoon, wait;
        static int waitStart;
        static bool keySent;

        static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Harbor"); } }
        static string InfoFile { get { return Path.Combine(Dir, "lastplayed.txt"); } }
        static string ArtFile { get { return Path.Combine(Dir, "lastplayed.png"); } }

        public static bool Has { get { return !string.IsNullOrEmpty(Title); } }

        static readonly string[] browsers = { "msedge", "chrome", "firefox", "brave", "opera", "vivaldi", "arc", "dia", "browser" };

        public static bool FromBrowser
        {
            get
            {
                string a = (App ?? "").ToLowerInvariant();
                foreach (var b in browsers) if (a.Contains(b)) return true;
                return false;
            }
        }

        public static string PlayLabel { get { return FromBrowser ? "Search on YouTube" : "Play"; } }

        public static void Init()
        {
            Load();
            Media.Changed += OnMedia;
        }

        static void OnMedia()
        {
            if (Resuming && Media.Has) Resumed();
            if (!Media.Has || Media.Demo) return;
            string k = Media.Title + "|" + Media.Artist + "|" + Media.SourceApp;
            if (k == key && (Art != null || Media.Art == null)) return;
            key = k;
            Title = Media.Title;
            Artist = Media.Artist;
            App = Media.SourceApp;
            AppName = Media.AppName;
            At = DateTime.Now;
            if (Media.Art != null) { var old = Art; Art = new Bitmap(Media.Art); if (old != null) old.Dispose(); }
            // the cover often arrives a moment after the title: save once things settle
            if (saveSoon == null) { saveSoon = new System.Windows.Forms.Timer { Interval = 3000 }; saveSoon.Tick += delegate { saveSoon.Stop(); Save(); }; }
            saveSoon.Stop();
            saveSoon.Start();
        }

        // ------------------------------------------------------------------ playing it again

        public static void Resume()
        {
            if (!Has) return;
            if (FromBrowser)
            {
                Launcher.Run("https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(Title + " " + Artist));
                return;
            }
            Resuming = true;
            keySent = false;
            waitStart = Environment.TickCount;
            try
            {
                if ((App ?? "").IndexOf("spotify", StringComparison.OrdinalIgnoreCase) >= 0) Launcher.Run("spotify:");
                else Launcher.Run("explorer.exe", @"shell:AppsFolder\" + App);
            }
            catch (Exception ex) { App2Log(ex); Resuming = false; return; }
            if (wait == null) { wait = new System.Windows.Forms.Timer { Interval = 500 }; wait.Tick += delegate { Waiting(); }; }
            wait.Start();
            Fire();
        }

        static void App2Log(Exception ex) { Harbor.App.Log(ex); }

        static void Waiting()
        {
            int ms = unchecked(Environment.TickCount - waitStart);
            if (Media.Has) { Resumed(); return; }
            // the app is open but hasn't told Windows about its player yet: a media key wakes it (nothing else is playing)
            if (!keySent && ms > 7000) { keySent = true; N.SendKey(0xB3); }
            if (ms > 20000) { wait.Stop(); Resuming = false; Fire(); }
        }

        static void Resumed()
        {
            if (wait != null) wait.Stop();
            Resuming = false;
            if (!Media.Playing && !keySent) Media.PlayPause();
            Fire();
        }

        static void Fire() { if (Changed != null) Changed(); }

        // ------------------------------------------------------------------ saved

        static void Save()
        {
            if (key == savedKey && File.Exists(ArtFile) == (Art != null)) return;
            savedKey = key;
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllLines(InfoFile, new[] { Title ?? "", Artist ?? "", App ?? "", AppName ?? "", At.ToString("o", CultureInfo.InvariantCulture) }, Encoding.UTF8);
                if (Art != null)
                    using (var small = Gfx.Scale(Art, 200)) small.Save(ArtFile, ImageFormat.Png);
                else if (File.Exists(ArtFile)) File.Delete(ArtFile);
            }
            catch (Exception ex) { Harbor.App.Log(ex); }
        }

        static void Load()
        {
            try
            {
                if (!File.Exists(InfoFile)) return;
                var l = File.ReadAllLines(InfoFile, Encoding.UTF8);
                if (l.Length < 5) return;
                Title = l[0]; Artist = l[1]; App = l[2]; AppName = l[3];
                DateTime.TryParse(l[4], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out At);
                key = savedKey = Title + "|" + Artist + "|" + App;
                if (File.Exists(ArtFile)) using (var fs = new FileStream(ArtFile, FileMode.Open, FileAccess.Read)) using (var b = Image.FromStream(fs)) Art = new Bitmap(b);
            }
            catch (Exception ex) { Harbor.App.Log(ex); }
        }

        public static void UseDemo()
        {
            Title = "Midnight City"; Artist = "M83"; App = "Spotify.exe"; AppName = "Spotify"; At = DateTime.Now.AddHours(-3);
        }
    }
}
