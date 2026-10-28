using System;
using System.Windows.Forms;

namespace Harbor
{
    /// <summary>
    /// Pomodoro timer: Work 25 / Short break 5 / Long break 15 (every 4th break is long). Runs on its own whether or not
    /// the island is open, ticks only while running, says so with a banner when a phase ends, and counts today's
    /// finished work sessions.
    /// </summary>
    static class Pomodoro
    {
        public enum Phase { Work, ShortBreak, LongBreak }

        public static Phase Current = Phase.Work;
        public static bool Running;
        static TimeSpan remaining = Length(Phase.Work);
        static DateTime endsAt;
        static Timer tick;
        public static event Action Changed;

        public static TimeSpan Length(Phase p)
        {
            int m = p == Phase.Work ? Look.Int("PomoWork") : p == Phase.ShortBreak ? Look.Int("PomoShort") : Look.Int("PomoLong");
            return TimeSpan.FromMinutes(Math.Max(1, Math.Min(180, m)));
        }

        public static string Name(Phase p) { return p == Phase.Work ? "Work" : p == Phase.ShortBreak ? "Short Break" : "Long Break"; }

        public static TimeSpan Remaining
        {
            get
            {
                if (!Running) return remaining;
                var r = endsAt - DateTime.Now;
                return r < TimeSpan.Zero ? TimeSpan.Zero : r;
            }
        }

        /// <summary>0 at the start of the phase, 1 when it's done.</summary>
        public static float Progress
        {
            get
            {
                double total = Length(Current).TotalSeconds;
                return total <= 0 ? 0 : (float)Math.Max(0, Math.Min(1, 1 - Remaining.TotalSeconds / total));
            }
        }

        /// <summary>Work sessions finished today.</summary>
        public static int SessionsToday
        {
            get
            {
                string v = Look.Get("PomoToday");
                int bar = v.IndexOf('|'), n;
                return bar > 0 && v.Substring(0, bar) == DateTime.Today.ToString("yyyy-MM-dd") && int.TryParse(v.Substring(bar + 1), out n) ? n : 0;
            }
        }

        /// <summary>--snapshot --demo: a work session paused partway, two done today (not saved).</summary>
        public static void UseDemo()
        {
            Current = Phase.Work;
            remaining = TimeSpan.FromMinutes(14.5);
            Look.Set("PomoToday", DateTime.Today.ToString("yyyy-MM-dd") + "|2");
        }

        /// <summary>The reminder this focus session is for (Pomodoro's "focus target"), or null.</summary>
        public static Reminders.Item Focus
        {
            get
            {
                var i = Reminders.Find(Look.Get("PomoFocus"));
                return i != null && !i.Done ? i : null;
            }
        }

        /// <summary>Steps through your open reminders as the focus target, then back to none.</summary>
        public static void NextFocus()
        {
            var open = Reminders.Open();
            var cur = Focus;
            int at = cur == null ? -1 : open.FindIndex(i => i.Id == cur.Id);
            Look.Set("PomoFocus", at + 1 < open.Count ? open[at + 1].Id : "");
            Settings.Save();
            Fire();
        }

        public static void Toggle() { if (Running) Pause(); else Start(); }

        public static void Start()
        {
            if (Running) return;
            if (remaining <= TimeSpan.Zero) remaining = Length(Current);
            endsAt = DateTime.Now + remaining;
            Running = true;
            if (tick == null) { tick = new Timer { Interval = 1000 }; tick.Tick += delegate { Tick(); }; }
            tick.Start();
            Fire();
        }

        public static void Pause()
        {
            if (!Running) return;
            remaining = Remaining;
            Running = false;
            if (tick != null) tick.Stop();
            Fire();
        }

        /// <summary>Back to the start of the current phase.</summary>
        public static void Reset()
        {
            Running = false;
            if (tick != null) tick.Stop();
            remaining = Length(Current);
            Fire();
        }

        /// <summary>Choose a phase (stops the timer).</summary>
        public static void Select(Phase p)
        {
            Current = p;
            Reset();
        }

        /// <summary>End this phase now and move to the next (skipping doesn't count as a finished session).</summary>
        public static void Skip() { Advance(false); }

        static void Tick()
        {
            if (Remaining <= TimeSpan.Zero) { Advance(true); return; }
            Fire();
        }

        static void Advance(bool finished)
        {
            bool wasRunning = Running;
            var done = Current;
            if (done == Phase.Work)
            {
                int n = SessionsToday;
                if (finished) { n++; Look.Set("PomoToday", DateTime.Today.ToString("yyyy-MM-dd") + "|" + n); Settings.Save(); }
                if (finished) FocusMode.WorkSessionDone(); // a Pomodoro-linked Focus ends with the session
                Current = n > 0 && n % 4 == 0 && finished ? Phase.LongBreak : Phase.ShortBreak;
            }
            else Current = Phase.Work;
            Running = false;
            if (tick != null) tick.Stop();
            remaining = Length(Current);
            if (finished)
            {
                var focus = Focus;
                Banners.ShowLocal("Pomodoro", done == Phase.Work ? "Time for a break" : "Break's over",
                    done == Phase.Work ? Name(Current) + " · " + (int)Length(Current).TotalMinutes + " min. Nice work" + (focus != null ? " on \"" + focus.Text + "\"" : "") + "!"
                                       : "Ready for another " + (int)Length(Phase.Work).TotalMinutes + " minutes" + (focus != null ? " on \"" + focus.Text + "\"" : " of focus") + "?");
                try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
                // breaks start on their own, like most Pomodoro apps; the next work session waits for you
                if (Current != Phase.Work && wasRunning) Start();
            }
            Fire();
        }

        static void Fire() { if (Changed != null) Changed(); }
    }
}
