using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Media;
using System.Text;

namespace Harbor
{
    /// <summary>
    /// Timers, alarms and world clocks for the island. Kept in %APPDATA%\Harbor\timers.tsv. A one-second tick runs only
    /// while a timer is counting down (for the menu bar and the island); alarms are checked once a minute, and only
    /// while one is switched on.
    /// </summary>
    static class Timers
    {
        public sealed class Countdown
        {
            public string Id = Guid.NewGuid().ToString("N").Substring(0, 8), Label;
            public TimeSpan Length;
            public DateTime Ends;
            public TimeSpan PausedLeft;
            public bool Paused;
            public TimeSpan Left { get { var l = Paused ? PausedLeft : Ends - DateTime.Now; return l < TimeSpan.Zero ? TimeSpan.Zero : l; } }
            public float Progress { get { return Length.TotalSeconds <= 0 ? 1 : (float)Math.Min(1, 1 - Left.TotalSeconds / Length.TotalSeconds); } }
        }

        public sealed class Alarm
        {
            public string Id = Guid.NewGuid().ToString("N").Substring(0, 8), Label;
            public int Hour, Minute;
            public bool On = true;
            public DateTime LastRang;
        }

        public sealed class Clock
        {
            public string Label, Zone;
            public TimeZoneInfo Tz { get { try { return TimeZoneInfo.FindSystemTimeZoneById(Zone); } catch { return null; } } }
        }

        public static readonly List<Countdown> Running = new List<Countdown>();
        public static readonly List<Alarm> Alarms = new List<Alarm>();
        public static readonly List<Clock> Clocks = new List<Clock>();
        public static event Action Changed;

        static System.Windows.Forms.Timer tick, minute;
        static bool loaded;

        static string FilePath { get { return Path.Combine(Settings.Dir, "timers.tsv"); } }

        public static void Init()
        {
            Load();
            Arm();
        }

        static void Fire() { if (Changed != null) Changed(); }

        // ------------------------------------------------------------------ timers

        public static void Start(TimeSpan length, string label)
        {
            if (length <= TimeSpan.Zero) return;
            Running.Add(new Countdown { Length = length, Ends = DateTime.Now + length, Label = string.IsNullOrEmpty(label) ? Describe(length) + " timer" : label });
            Save();
            Arm();
            Fire();
        }

        public static void TogglePause(Countdown c)
        {
            if (c.Paused) { c.Ends = DateTime.Now + c.PausedLeft; c.Paused = false; }
            else { c.PausedLeft = c.Left; c.Paused = true; }
            Save();
            Arm();
            Fire();
        }

        public static void Remove(Countdown c)
        {
            Running.Remove(c);
            Save();
            Arm();
            Fire();
        }

        /// <summary>The running timer that ends first (for the menu bar), or null.</summary>
        public static Countdown Soonest()
        {
            Countdown best = null;
            foreach (var c in Running) if (!c.Paused && (best == null || c.Ends < best.Ends)) best = c;
            return best;
        }

        /// <summary>"12", "1:30", "90s", "1h 20m", "25 min" → a length, or zero if it doesn't read as one.</summary>
        public static TimeSpan Parse(string s)
        {
            s = (s ?? "").Trim().ToLowerInvariant();
            if (s == "") return TimeSpan.Zero;
            var m = System.Text.RegularExpressions.Regex.Match(s, @"^(\d{1,2}):(\d{2})(?::(\d{2}))?$");
            if (m.Success)
                return m.Groups[3].Success ? new TimeSpan(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value))
                                           : new TimeSpan(0, int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));
            var total = TimeSpan.Zero;
            bool any = false;
            foreach (System.Text.RegularExpressions.Match p in System.Text.RegularExpressions.Regex.Matches(s, @"(\d+(?:\.\d+)?)\s*(h|hr|hrs|hour|hours|m|min|mins|minute|minutes|s|sec|secs|second|seconds)?"))
            {
                if (p.Value.Trim() == "") continue;
                double v;
                if (!double.TryParse(p.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) continue;
                string u = p.Groups[2].Value;
                total += u.StartsWith("h") ? TimeSpan.FromHours(v) : u.StartsWith("s") ? TimeSpan.FromSeconds(v) : TimeSpan.FromMinutes(v);
                any = true;
            }
            return any && total <= TimeSpan.FromHours(24) ? total : TimeSpan.Zero;
        }

        public static string Describe(TimeSpan t)
        {
            if (t.TotalHours >= 1) return (int)t.TotalHours + " h" + (t.Minutes > 0 ? " " + t.Minutes + " min" : "");
            if (t.TotalMinutes >= 1) return (int)t.TotalMinutes + " min" + (t.Seconds > 0 ? " " + t.Seconds + " s" : "");
            return (int)t.TotalSeconds + " s";
        }

        /// <summary>"4:59" / "1:04:59".</summary>
        public static string Text(TimeSpan t)
        {
            int secs = (int)Math.Ceiling(t.TotalSeconds);
            return secs >= 3600 ? (secs / 3600) + ":" + (secs / 60 % 60).ToString("00") + ":" + (secs % 60).ToString("00") : (secs / 60) + ":" + (secs % 60).ToString("00");
        }

        // ------------------------------------------------------------------ alarms

        /// <summary>"7:30", "07:30 Wake up", "6:15pm gym" → adds an alarm; returns an error, or null.</summary>
        public static string AddAlarm(string text)
        {
            var m = System.Text.RegularExpressions.Regex.Match((text ?? "").Trim(), @"^(\d{1,2})(?:[:.](\d{2}))?\s*(am|pm|a\.m\.|p\.m\.)?\s*(.*)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!m.Success) return "Type a time, e.g. 7:30 or 6:15 pm";
            int h = int.Parse(m.Groups[1].Value), min = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
            string ap = m.Groups[3].Value.ToLowerInvariant();
            if (ap.StartsWith("p") && h < 12) h += 12;
            if (ap.StartsWith("a") && h == 12) h = 0;
            if (h > 23 || min > 59) return "That isn't a time of day";
            Alarms.Add(new Alarm { Hour = h, Minute = min, Label = m.Groups[4].Value.Trim() == "" ? "Alarm" : m.Groups[4].Value.Trim() });
            Alarms.Sort((a, b) => (a.Hour * 60 + a.Minute).CompareTo(b.Hour * 60 + b.Minute));
            Save();
            Arm();
            Fire();
            return null;
        }

        public static void ToggleAlarm(Alarm a) { a.On = !a.On; Save(); Arm(); Fire(); }
        public static void RemoveAlarm(Alarm a) { Alarms.Remove(a); Save(); Arm(); Fire(); }

        public static string AlarmText(Alarm a) { return new DateTime(2000, 1, 1, a.Hour, a.Minute, 0).ToString("t", CultureInfo.CurrentCulture); }

        // ------------------------------------------------------------------ world clocks

        static readonly Dictionary<string, string> cities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "London", "GMT Standard Time" }, { "Dublin", "GMT Standard Time" }, { "Lisbon", "GMT Standard Time" },
            { "Paris", "Romance Standard Time" }, { "Madrid", "Romance Standard Time" }, { "Brussels", "Romance Standard Time" },
            { "Berlin", "W. Europe Standard Time" }, { "Amsterdam", "W. Europe Standard Time" }, { "Rome", "W. Europe Standard Time" }, { "Zurich", "W. Europe Standard Time" }, { "Stockholm", "W. Europe Standard Time" },
            { "Athens", "GTB Standard Time" }, { "Istanbul", "Turkey Standard Time" }, { "Cairo", "Egypt Standard Time" }, { "Moscow", "Russian Standard Time" },
            { "Dubai", "Arabian Standard Time" }, { "Abu Dhabi", "Arabian Standard Time" }, { "Riyadh", "Arab Standard Time" }, { "Karachi", "Pakistan Standard Time" },
            { "Mumbai", "India Standard Time" }, { "Delhi", "India Standard Time" }, { "New Delhi", "India Standard Time" }, { "Bangalore", "India Standard Time" }, { "Bengaluru", "India Standard Time" },
            { "Kolkata", "India Standard Time" }, { "Chennai", "India Standard Time" }, { "Hyderabad", "India Standard Time" }, { "Pune", "India Standard Time" },
            { "Kathmandu", "Nepal Standard Time" }, { "Dhaka", "Bangladesh Standard Time" }, { "Bangkok", "SE Asia Standard Time" }, { "Jakarta", "SE Asia Standard Time" },
            { "Singapore", "Singapore Standard Time" }, { "Kuala Lumpur", "Singapore Standard Time" }, { "Hong Kong", "China Standard Time" }, { "Shanghai", "China Standard Time" },
            { "Beijing", "China Standard Time" }, { "Taipei", "Taipei Standard Time" }, { "Seoul", "Korea Standard Time" }, { "Tokyo", "Tokyo Standard Time" },
            { "Sydney", "AUS Eastern Standard Time" }, { "Melbourne", "AUS Eastern Standard Time" }, { "Brisbane", "E. Australia Standard Time" }, { "Perth", "W. Australia Standard Time" },
            { "Auckland", "New Zealand Standard Time" }, { "Honolulu", "Hawaiian Standard Time" }, { "Anchorage", "Alaskan Standard Time" },
            { "Los Angeles", "Pacific Standard Time" }, { "San Francisco", "Pacific Standard Time" }, { "Seattle", "Pacific Standard Time" }, { "Vancouver", "Pacific Standard Time" },
            { "Denver", "Mountain Standard Time" }, { "Phoenix", "US Mountain Standard Time" }, { "Chicago", "Central Standard Time" }, { "Dallas", "Central Standard Time" }, { "Austin", "Central Standard Time" },
            { "Mexico City", "Central Standard Time (Mexico)" }, { "New York", "Eastern Standard Time" }, { "Toronto", "Eastern Standard Time" }, { "Boston", "Eastern Standard Time" },
            { "Washington", "Eastern Standard Time" }, { "Miami", "Eastern Standard Time" }, { "São Paulo", "E. South America Standard Time" }, { "Sao Paulo", "E. South America Standard Time" },
            { "Buenos Aires", "Argentina Standard Time" }, { "Johannesburg", "South Africa Standard Time" }, { "Lagos", "W. Central Africa Standard Time" }, { "Nairobi", "E. Africa Standard Time" },
            { "UTC", "UTC" }, { "GMT", "GMT Standard Time" },
        };

        /// <summary>Adds a city (or a time zone name); returns an error, or null.</summary>
        public static string AddClock(string text)
        {
            text = (text ?? "").Trim();
            if (text == "") return null;
            string zone;
            if (!cities.TryGetValue(text, out zone))
                foreach (var tz in TimeZoneInfo.GetSystemTimeZones())
                    if (tz.DisplayName.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 || tz.Id.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0) { zone = tz.Id; break; }
            if (zone == null) return "Couldn't find \"" + text + "\". Try a bigger city nearby.";
            string label = char.ToUpper(text[0]) + text.Substring(1);
            if (Clocks.Exists(c => string.Equals(c.Label, label, StringComparison.OrdinalIgnoreCase))) return null;
            Clocks.Add(new Clock { Label = label, Zone = zone });
            Save();
            Fire();
            return null;
        }

        public static void RemoveClock(Clock c) { Clocks.Remove(c); Save(); Fire(); }

        // ------------------------------------------------------------------ ticking and ringing

        static void Arm()
        {
            bool counting = Running.Exists(c => !c.Paused);
            if (counting && tick == null) { tick = new System.Windows.Forms.Timer { Interval = 1000 }; tick.Tick += delegate { Tick(); }; }
            if (tick != null) { if (counting) tick.Start(); else tick.Stop(); }
            bool alarms = Alarms.Exists(a => a.On);
            if (alarms && minute == null) { minute = new System.Windows.Forms.Timer { Interval = 15000 }; minute.Tick += delegate { CheckAlarms(); }; }
            if (minute != null) { if (alarms) minute.Start(); else minute.Stop(); }
        }

        static void Tick()
        {
            foreach (var c in Running.ToArray())
                if (!c.Paused && c.Left <= TimeSpan.Zero)
                {
                    Running.Remove(c);
                    Save();
                    Ring(c.Label, "Time's up", () => Start(TimeSpan.FromMinutes(1), c.Label), "+1 min");
                }
            Arm();
            Fire();
        }

        static void CheckAlarms()
        {
            var now = DateTime.Now;
            foreach (var a in Alarms)
                if (a.On && a.Hour == now.Hour && a.Minute == now.Minute && (now - a.LastRang).TotalMinutes > 2)
                {
                    a.LastRang = now;
                    var snooze = a;
                    Ring(a.Label, AlarmText(a), () => Start(TimeSpan.FromMinutes(9), snooze.Label + " (snoozed)"), "Snooze");
                }
        }

        static void Ring(string title, string detail, Action again, string againLabel)
        {
            try { SystemSounds.Exclamation.Play(); } catch { }
            LiveActivity.Show(new LiveActivity.Item { Glyph = "", Title = title, Detail = detail, Tint = Color.FromArgb(255, 255, 179, 64), Important = true, Ms = 10000, Action = againLabel, Click = again });
            Banners.ShowLocal("Timer", title, detail);
        }

        // ------------------------------------------------------------------ saved

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (var line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    var p = line.Split('\t');
                    try
                    {
                        if (p[0] == "timer" && p.Length >= 6)
                        {
                            var c = new Countdown { Label = p[1], Length = TimeSpan.FromSeconds(double.Parse(p[2], CultureInfo.InvariantCulture)), Paused = p[3] == "1" };
                            c.Ends = DateTime.Parse(p[4], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                            c.PausedLeft = TimeSpan.FromSeconds(double.Parse(p[5], CultureInfo.InvariantCulture));
                            if (c.Paused || c.Ends > DateTime.Now) Running.Add(c);
                        }
                        else if (p[0] == "alarm" && p.Length >= 5) Alarms.Add(new Alarm { Label = p[1], Hour = int.Parse(p[2]), Minute = int.Parse(p[3]), On = p[4] == "1" });
                        else if (p[0] == "clock" && p.Length >= 3) Clocks.Add(new Clock { Label = p[1], Zone = p[2] });
                    }
                    catch { }
                }
            }
            catch (Exception ex) { App.Log(ex); }
        }

        static void Save()
        {
            if (demo) return;
            try
            {
                var sb = new StringBuilder();
                Func<string, string> clean = s => (s ?? "").Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');
                foreach (var c in Running)
                    sb.Append("timer\t").Append(clean(c.Label)).Append('\t').Append(c.Length.TotalSeconds.ToString(CultureInfo.InvariantCulture)).Append('\t')
                      .Append(c.Paused ? "1" : "0").Append('\t').Append(c.Ends.ToString("o", CultureInfo.InvariantCulture)).Append('\t')
                      .Append(c.PausedLeft.TotalSeconds.ToString(CultureInfo.InvariantCulture)).Append('\n');
                foreach (var a in Alarms) sb.Append("alarm\t").Append(clean(a.Label)).Append('\t').Append(a.Hour).Append('\t').Append(a.Minute).Append('\t').Append(a.On ? "1" : "0").Append('\n');
                foreach (var c in Clocks) sb.Append("clock\t").Append(clean(c.Label)).Append('\t').Append(c.Zone).Append('\n');
                Directory.CreateDirectory(Settings.Dir);
                File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex) { App.Log(ex); }
        }

        static bool demo;

        public static void UseDemo()
        {
            demo = loaded = true;
            Running.Add(new Countdown { Label = "Pasta", Length = TimeSpan.FromMinutes(10), Paused = true, PausedLeft = new TimeSpan(0, 6, 42) });
            Alarms.Add(new Alarm { Label = "Wake up", Hour = 7, Minute = 0 });
            Alarms.Add(new Alarm { Label = "Gym", Hour = 18, Minute = 30, On = false });
            Clocks.Add(new Clock { Label = "London", Zone = "GMT Standard Time" });
            Clocks.Add(new Clock { Label = "New York", Zone = "Eastern Standard Time" });
            Clocks.Add(new Clock { Label = "Tokyo", Zone = "Tokyo Standard Time" });
        }
    }
}
