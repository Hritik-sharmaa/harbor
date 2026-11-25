using System;
using System.Collections.Generic;
using System.Drawing;

namespace Harbor
{
    // ---------------------------------------------------------------------------------------------------------------
    // src/Core is Harbor's shared code: reminders, timers, Pomodoro, Focus, weather, lyrics, calendar, Claude usage,
    // settings. It runs on Windows (Harbor.exe, compiled by build.cmd with the C# 5 compiler that ships with Windows)
    // and on Linux (harbord, compiled by the .NET SDK). Rules for files in this folder:
    //   - C# 5 only (no string interpolation, no ?., no expression-bodied members), so build.cmd keeps working.
    //   - No WinForms, no GDI+ (Bitmap/Graphics), no P/Invoke, no registry, no WinRT. System.Drawing.Color is fine:
    //     it is a plain struct on every platform.
    //   - Anything the OS has to do goes through IPlatform (via the Core facade below).
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>What the shared code needs from the operating system it runs on. Windows: WinPlatform. Linux: harbord.</summary>
    interface IPlatform
    {
        /// <summary>Where Harbor keeps its settings and data: %APPDATA%\Harbor, or ~/.config/harbor.</summary>
        string ConfigDir { get; }
        /// <summary>--snapshot: rendering README screenshots, nothing real is read.</summary>
        bool SnapshotMode { get; }
        void Log(Exception ex);
        /// <summary>Runs 'a' on the UI (main) thread, soon. Background work hands its results back this way.</summary>
        void Post(Action a);
        /// <summary>A timer that ticks on the UI thread (stopped when created).</summary>
        ITicker NewTicker();
        /// <summary>A notification banner from Harbor itself ("Reminder", "Timer", "Pomodoro"...).</summary>
        void Banner(string app, string title, string body);
        /// <summary>A live activity: the small pill that drops out of the island.</summary>
        void ShowActivity(Activity a);
        /// <summary>False while nothing can show a live activity right now (fullscreen, no island): callers try later.</summary>
        bool CanShowActivity { get; }
        void PlaySound(Sound s);
        /// <summary>Opens a URL, file or folder with its default app.</summary>
        void Open(string target);
        /// <summary>Opens the island on one of its tabs ("calendar", "weather"...).</summary>
        void OpenTab(string tab);
        /// <summary>Minutes since the last keyboard or mouse input.</summary>
        double IdleMinutes();
        /// <summary>The system's own light / dark choice (for Theme "auto").</summary>
        bool SystemPrefersDark();
        /// <summary>The theme was just worked out: native menus and the like can follow it.</summary>
        void ThemeApplied(bool dark);
        /// <summary>Focus turned on or off: the dock and bar re-apply their hiding rules.</summary>
        void FocusChanged();
        /// <summary>A good moment to give memory back (after a big read).</summary>
        void TrimMemory();
        /// <summary>What a fresh install pins to the dock.</summary>
        List<string> DefaultPins();
        /// <summary>The song playing right now (Has is false when nothing is).</summary>
        Song NowPlaying();
    }

    /// <summary>What's playing, as far as the shared code needs it (lyrics lookups).</summary>
    sealed class Song
    {
        public bool Has, Demo;
        public string Artist, Title, Album;
        public TimeSpan Duration;
    }

    interface ITicker : IDisposable
    {
        int Interval { get; set; }
        bool Enabled { get; }
        event Action Tick;
        void Start();
        void Stop();
    }

    enum Sound { Asterisk, Exclamation }

    /// <summary>A live activity, as the shared code describes it. Each platform draws it its own way.</summary>
    sealed class Activity
    {
        public string Glyph, Title, Detail, Action;   // Glyph: a Segoe Fluent Icons code point; Action: a label like "Join"
        public Color Tint = Color.White;
        public Action Click;
        public bool Important;
        public int Ms = 4000;
    }

    /// <summary>The shared code's way to reach the OS. Set Core.Platform first thing in Main.</summary>
    static class Core
    {
        public static IPlatform Platform;

        public static bool SnapshotMode { get { return Platform != null && Platform.SnapshotMode; } }
        public static void Log(Exception ex) { if (Platform != null) Platform.Log(ex); }
        public static void Post(Action a) { Platform.Post(a); }
        public static ITicker NewTicker(int interval) { var t = Platform.NewTicker(); t.Interval = interval; return t; }
        public static ITicker NewTicker() { return Platform.NewTicker(); }
        public static void Banner(string app, string title, string body) { Platform.Banner(app, title, body); }
        public static void Show(Activity a) { Platform.ShowActivity(a); }
        public static void Play(Sound s) { try { Platform.PlaySound(s); } catch { } }
        public static void Open(string target) { Platform.Open(target); }
        public static void OpenTab(string tab) { Platform.OpenTab(tab); }
    }

    static class AppInfo
    {
        public const string Version = "0.4.2";
        public const string RepoUrl = "https://github.com/hritik-sharmaa/harbor";
    }
}
