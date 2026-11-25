using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Harbor
{
    /// <summary>harbord --selftest: runs the shared code for real on this machine (a scratch config folder, no D-Bus, no network).</summary>
    static class SelfTest
    {
        static int fails;

        static void Check(bool ok, string what)
        {
            Console.WriteLine((ok ? "  ok    " : "  FAIL  ") + what);
            if (!ok) fails++;
        }

        public static int Run(MainLoop loop, LinuxPlatform platform)
        {
            string dir = Path.Combine(Path.GetTempPath(), "harbord-selftest-" + Environment.ProcessId);
            platform.ConfigDir = dir;
            Console.WriteLine("harbord " + AppInfo.Version + " self-test (config in " + dir + ")");
            var done = new ManualResetEventSlim();
            var changes = new List<string>();
            var api = new Api(platform, s => changes.Add(s));

            loop.Enqueue(() =>
            {
                try
                {
                    // settings: a fresh install writes settings.ini with Linux pins (desktop IDs)
                    Settings.Load();
                    Check(File.Exists(Path.Combine(dir, "settings.ini")), "settings.ini written on first run");
                    Check(Settings.Pinned.TrueForAll(p => p.EndsWith(".desktop")), "default pins are desktop IDs (" + Settings.Pinned.Count + " found)");
                    Look.Set("DockRadius", 24);
                    Settings.IconSize = 52;
                    Settings.Save();
                    Settings.IconSize = 40;
                    Look.Reset("Dock");
                    Settings.Load();
                    Check(Settings.IconSize == 52 && Look.Int("DockRadius") == 24, "settings survive save and load");
                    api.Start();

                    // JSON API: every section serialises and parses back
                    var all = Json.Object(api.Get("all"));
                    Check(all != null && all.ContainsKey("pomodoro") && all.ContainsKey("dock"), "Get(\"all\") returns every section as JSON");

                    // reminders persist
                    Reminders.Add("Self-test reminder", DateTime.Now.AddHours(2));
                    Check(File.ReadAllText(Path.Combine(dir, "reminders.tsv")).Contains("Self-test reminder"), "reminders are saved");

                    // notes
                    Notes.Add("a note");
                    Check(Notes.All.Count == 1 && File.Exists(Path.Combine(dir, "notes.txt")), "notes are saved");

                    // timers: parsing, world clocks through Windows zone IDs (ICU on Linux), and a real 1 s timer
                    Check(Timers.Parse("1h 20m") == TimeSpan.FromMinutes(80) && Timers.Parse("1:30") == TimeSpan.FromSeconds(90), "timer lengths parse");
                    Check(Timers.AddClock("Tokyo") == null && Timers.Clocks[0].Tz != null, "world clock time zones resolve on Linux");
                    Check(Timers.AddAlarm("6:15 pm gym") == null && Timers.Alarms[0].Hour == 18, "alarms parse");
                    Timers.Start(TimeSpan.FromSeconds(1), "Self-test");
                    Check(Timers.Running.Count == 1, "a timer starts");

                    // Pomodoro
                    Pomodoro.Start();
                    Check(Pomodoro.Running && Pomodoro.Remaining > TimeSpan.FromMinutes(24), "Pomodoro starts");
                    Pomodoro.Pause();
                    Check(!Pomodoro.Running, "Pomodoro pauses");

                    // lyrics and calendar parsers
                    var lines = Lyrics.Parse("[00:12.50]first\n[01:02.00]second");
                    Check(lines.Count == 2 && lines[1].At == TimeSpan.FromSeconds(62), "LRC lyrics parse");
                    var ev = CalendarFeed.Parse("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:x\r\nSUMMARY:Standup\r\nDTSTART:" + DateTime.Today.AddDays(1).AddHours(9).ToString("yyyyMMdd'T'HHmmss") +
                        "\r\nDTEND:" + DateTime.Today.AddDays(1).AddHours(9.5).ToString("yyyyMMdd'T'HHmmss") + "\r\nDESCRIPTION:Join https://meet.google.com/abc-defg-hij\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n",
                        DateTime.Today, DateTime.Today.AddDays(3));
                    Check(ev.Count == 1 && ev[0].Title == "Standup" && ev[0].Link != null, "iCal events parse, with the Meet link");
                    Check(ClaudeUsage.EditorName("/home/me/.claude-vscode") == "VS Code", "Claude folder names map to editors");
                }
                catch (Exception ex) { Check(false, "unexpected error: " + ex); }
            });

            // the 1-second timer rings through the main loop's ticker
            var deadline = DateTime.Now.AddSeconds(4);
            var watch = new Timer(_ => loop.Enqueue(() =>
            {
                if (Timers.Running.Count == 0 || DateTime.Now > deadline)
                {
                    Check(Timers.Running.Count == 0, "the timer rang and finished on the main loop");
                    Check(changes.Contains("timers") && changes.Contains("reminders") && changes.Contains("pomodoro"), "Changed events reach the API");
                    loop.Stop();
                }
            }), null, 500, 250);

            loop.Run();
            watch.Dispose();
            try { Directory.Delete(dir, true); } catch { }
            Console.WriteLine(fails == 0 ? "All checks passed." : fails + " check(s) failed.");
            return fails;
        }
    }
}
