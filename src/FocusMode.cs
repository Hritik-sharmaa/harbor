using System;

namespace Harbor
{
    /// <summary>
    /// Focus: one switch for heads-down time. Banners are held back (notifications still collect in the Notification
    /// Center), a Pomodoro work session starts, and the dock tucks away until you push the mouse to the screen edge.
    /// With Pomodoro it ends by itself when the work session does; otherwise when you switch it off.
    /// Nothing runs while it's off.
    /// </summary>
    static class FocusMode
    {
        public static bool On;
        static bool withPomodoro;
        static int held;                    // banners kept quiet while focusing
        public static event Action Changed;

        /// <summary>While focusing, the dock behaves as if "Automatically hide the dock" were on.</summary>
        public static bool HidesDock { get { return On && Look.Bool("FocusHideDock"); } }

        public static void Toggle() { if (On) Stop(); else Start(); }

        public static void Start()
        {
            if (On) return;
            On = true;
            held = 0;
            withPomodoro = Look.Bool("FocusPomodoro");
            if (withPomodoro)
            {
                if (Pomodoro.Current != Pomodoro.Phase.Work) Pomodoro.Select(Pomodoro.Phase.Work);
                Pomodoro.Start();
            }
            Fire();
        }

        public static void Stop() { End(false); }

        /// <summary>Pomodoro finished a work session: a Pomodoro-linked focus is over (Pomodoro's own banner says so).</summary>
        public static void WorkSessionDone()
        {
            if (On && withPomodoro) End(true);
        }

        static void End(bool finished)
        {
            if (!On) return;
            On = false;
            Fire();
            if (held > 0)
                Banners.ShowLocal("Focus", finished ? "Focus session done" : "Focus ended",
                    held + (held == 1 ? " notification" : " notifications") + " came in while you focused. They're in the Notification Center.");
            held = 0;
        }

        /// <summary>A banner that wasn't shown because of Focus.</summary>
        public static void Held() { held++; }

        /// <summary>Time left when it ends by itself (a Pomodoro work session), else null.</summary>
        public static TimeSpan? Left
        {
            get { return On && withPomodoro && Pomodoro.Current == Pomodoro.Phase.Work ? Pomodoro.Remaining : (TimeSpan?)null; }
        }

        /// <summary>"18m" (or "" when it runs until switched off) for the menu bar.</summary>
        public static string LeftText()
        {
            var l = Left;
            if (l == null) return "";
            return l.Value.TotalMinutes >= 1 ? (int)Math.Ceiling(l.Value.TotalMinutes) + "m" : (int)Math.Max(0, l.Value.TotalSeconds) + "s";
        }

        static void Fire()
        {
            App.OnFocusChanged();
            if (Changed != null) Changed();
        }
    }
}
