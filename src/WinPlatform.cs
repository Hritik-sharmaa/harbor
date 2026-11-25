using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Harbor
{
    /// <summary>Windows' side of IPlatform: what the shared code in src/Core asks of the OS, done the Win32 way.</summary>
    sealed class WinPlatform : IPlatform
    {
        public string ConfigDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Harbor"); }
        }

        public bool SnapshotMode { get { return App.SnapshotMode; } }
        public void Log(Exception ex) { App.Log(ex); }
        public void Post(Action a) { App.Post(a); }
        public ITicker NewTicker() { return new FormsTicker(); }
        public void Banner(string app, string title, string body) { Banners.ShowLocal(app, title, body); }

        public void ShowActivity(Activity a)
        {
            LiveActivity.Show(new LiveActivity.Item
            {
                Glyph = a.Glyph, Title = a.Title, Detail = a.Detail, Action = a.Action,
                Tint = a.Tint, Click = a.Click, Important = a.Important, Ms = a.Ms,
            });
        }

        public bool CanShowActivity { get { return LiveActivity.Target() != null; } }

        public void PlaySound(Sound s)
        {
            if (s == Sound.Exclamation) System.Media.SystemSounds.Exclamation.Play();
            else System.Media.SystemSounds.Asterisk.Play();
        }

        public void Open(string target) { Launcher.Run(target); }
        public void OpenTab(string tab) { Island.OpenTab(tab); }

        [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint cbSize, dwTime; }
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO li);

        public double IdleMinutes()
        {
            var li = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO)) };
            if (!GetLastInputInfo(ref li)) return 0;
            return unchecked((uint)Environment.TickCount - li.dwTime) / 60000.0;
        }

        public bool SystemPrefersDark()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
            {
                object v = k == null ? null : k.GetValue("SystemUsesLightTheme");
                return v is int ? (int)v == 0 : true;
            }
        }

        public void ThemeApplied(bool dark) { N.SetMenuTheme(dark); }
        public void FocusChanged() { App.OnFocusChanged(); }
        public void TrimMemory() { App.ScheduleTrim(); }

        public Song NowPlaying()
        {
            return new Song { Has = Media.Has, Demo = Media.Demo, Artist = Media.Artist, Title = Media.Title, Album = Media.Album, Duration = Media.Duration };
        }

        public List<string> DefaultPins()
        {
            var pins = new List<string>();
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string winApps = Path.Combine(local, @"Microsoft\WindowsApps");

            pins.Add(Path.Combine(win, "explorer.exe"));
            AddFirst(pins, Path.Combine(pf86, @"Microsoft\Edge\Application\msedge.exe"),
                           Path.Combine(pf, @"Microsoft\Edge\Application\msedge.exe"));
            AddFirst(pins, Path.Combine(pf, @"Google\Chrome\Application\chrome.exe"),
                           Path.Combine(pf86, @"Google\Chrome\Application\chrome.exe"),
                           Path.Combine(local, @"Google\Chrome\Application\chrome.exe"));
            AddFirst(pins, Path.Combine(pf, @"Mozilla Firefox\firefox.exe"));
            AddFirst(pins, Path.Combine(local, @"Programs\Microsoft VS Code\Code.exe"),
                           Path.Combine(pf, @"Microsoft VS Code\Code.exe"));
            if (File.Exists(Path.Combine(winApps, "wt.exe")))
                pins.Add(@"shell:AppsFolder\Microsoft.WindowsTerminal_8wekyb3d8bbwe!App");
            AddFirst(pins, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Spotify\Spotify.exe"));
            if (Directory.Exists(Path.Combine(winApps, "Microsoft.WindowsNotepad_8wekyb3d8bbwe")))
                pins.Add(@"shell:AppsFolder\Microsoft.WindowsNotepad_8wekyb3d8bbwe!App");
            else
                pins.Add(Path.Combine(win, "notepad.exe"));
            pins.Add(@"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App");
            pins.Add(@"shell:AppsFolder\windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel");

            // drop AppsFolder entries that don't exist on this PC
            pins.RemoveAll(p => p.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) && !ShellIcons.Exists(p));
            return pins;
        }

        static void AddFirst(List<string> pins, params string[] candidates)
        {
            foreach (var c in candidates) if (File.Exists(c)) { pins.Add(c); return; }
        }

        /// <summary>A WinForms timer: ticks on the UI thread, like everything else in Harbor.exe.</summary>
        sealed class FormsTicker : ITicker
        {
            readonly System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
            public event Action Tick;
            public FormsTicker() { t.Tick += delegate { if (Tick != null) Tick(); }; }
            public int Interval { get { return t.Interval; } set { t.Interval = Math.Max(1, value); } }
            public bool Enabled { get { return t.Enabled; } }
            public void Start() { t.Start(); }
            public void Stop() { t.Stop(); }
            public void Dispose() { t.Dispose(); }
        }
    }
}
