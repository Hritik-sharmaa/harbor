using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Harbor
{
    /// <summary>
    /// Your next meetings, from the calendar's private iCal address (Google Calendar: Settings → your calendar →
    /// "Secret address in iCal format"; Outlook: Settings → Calendar → Shared calendars → Publish a calendar → ICS).
    /// No account or sign-in: Harbor downloads that one link every 15 minutes, keeps the next 7 days in memory and finds
    /// the Teams / Meet / Zoom link in each event. Five minutes before a meeting it shows a live activity with Join.
    /// </summary>
    static class CalendarFeed
    {
        public sealed class Event
        {
            public string Title, Location, Link, Uid;
            public DateTime Start, End;
            public bool AllDay;
        }

        public static List<Event> Events = new List<Event>();   // sorted, the next 7 days (and today's earlier ones)
        public static string Error;
        public static DateTime? Fetched;
        public static bool Busy;
        public static event Action Changed;

        static System.Windows.Forms.Timer every, remind;
        static readonly HashSet<string> reminded = new HashSet<string>();

        public static bool Configured { get { return demo || Look.Get("CalendarUrls").Trim() != ""; } }

        public static void Init()
        {
            if (every == null)
            {
                every = new System.Windows.Forms.Timer { Interval = 15 * 60 * 1000 };
                every.Tick += delegate { Refresh(); };
                every.Start();
            }
            Refresh();
        }

        static void Fire() { if (Changed != null) Changed(); }

        /// <summary>The next meeting that hasn't ended (all-day events skipped), or null.</summary>
        public static Event Next()
        {
            var now = DateTime.Now;
            foreach (var e in Events) if (!e.AllDay && e.End > now) return e;
            return null;
        }

        public static void Refresh()
        {
            if (demo || Busy) return;
            if (!Configured) { Events = new List<Event>(); Error = null; Fetched = null; Fire(); return; }
            Busy = true;
            var urls = Look.Get("CalendarUrls").Split(new[] { ' ', '\n', '\r', ',' }, StringSplitOptions.RemoveEmptyEntries);
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                var all = new List<Event>();
                string err = null;
                foreach (var raw in urls)
                {
                    string url = raw.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase) ? "https://" + raw.Substring(9) : raw;
                    if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) { err = "Calendar links must start with https:// or webcal://"; continue; }
                    try
                    {
                        int status;
                        string ics = Web.Get(url, out status);
                        if (ics == null || ics.IndexOf("BEGIN:VCALENDAR", StringComparison.Ordinal) < 0) { err = "Couldn't read a calendar (" + (status == 0 ? "no connection" : "error " + status) + ")"; continue; }
                        all.AddRange(Parse(ics, DateTime.Today.AddDays(-1), DateTime.Today.AddDays(8)));
                    }
                    catch (Exception ex) { App.Log(ex); err = "Couldn't read a calendar"; }
                }
                all.Sort((a, b) => a.Start.CompareTo(b.Start));
                App.Post(() =>
                {
                    Busy = false;
                    Error = err;
                    if (all.Count > 0 || err == null) { Events = all; Fetched = DateTime.Now; }
                    ArmReminder();
                    Fire();
                });
            });
        }

        // ------------------------------------------------------------------ "starts in 5 minutes"

        static void ArmReminder()
        {
            if (remind == null) { remind = new System.Windows.Forms.Timer(); remind.Tick += delegate { remind.Stop(); Remind(); }; }
            remind.Stop();
            var now = DateTime.Now;
            foreach (var e in Events)
            {
                if (e.AllDay || reminded.Contains(Key(e))) continue;
                var at = e.Start.AddMinutes(-5);
                if (e.Start <= now) continue;
                double ms = Math.Max(1000, (at - now).TotalMilliseconds);
                remind.Interval = (int)Math.Min(ms, 30 * 60 * 1000); // wake at most every 30 min, in case of sleep / clock changes
                remind.Start();
                return;
            }
        }

        static string Key(Event e) { return e.Uid + "|" + e.Start.Ticks; }

        static void Remind()
        {
            var now = DateTime.Now;
            foreach (var e in Events)
            {
                if (e.AllDay || reminded.Contains(Key(e)) || e.Start <= now.AddMinutes(-1) || e.Start > now.AddMinutes(5.5)) continue;
                reminded.Add(Key(e));
                var ev = e;
                int mins = (int)Math.Max(0, Math.Round((e.Start - now).TotalMinutes));
                LiveActivity.Show(new LiveActivity.Item
                {
                    Glyph = "", Title = e.Title, Detail = mins <= 0 ? "starting now" : "in " + mins + " min",
                    Tint = System.Drawing.Color.FromArgb(255, 255, 99, 99), Important = true, Ms = 12000,
                    Action = e.Link != null ? "Join" : null, Click = () => { if (ev.Link != null) Launcher.Run(ev.Link); else Island.OpenTab("calendar"); },
                });
            }
            ArmReminder();
        }

        public static void Join(Event e) { if (e != null && e.Link != null) Launcher.Run(e.Link); }

        // ------------------------------------------------------------------ iCalendar

        sealed class Prop { public string Name, Value; public Dictionary<string, string> Params = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); }

        static List<Prop> Lines(string ics)
        {
            var list = new List<Prop>();
            // unfold: a line starting with a space or tab continues the one before
            var text = ics.Replace("\r\n", "\n").Replace("\n ", "").Replace("\n\t", "");
            foreach (var l in text.Split('\n'))
            {
                if (l.Length == 0) continue;
                int colon = -1;
                bool quoted = false;
                for (int i = 0; i < l.Length; i++) { if (l[i] == '"') quoted = !quoted; else if (l[i] == ':' && !quoted) { colon = i; break; } }
                if (colon < 0) continue;
                var head = l.Substring(0, colon).Split(';');
                var p = new Prop { Name = head[0].ToUpperInvariant(), Value = l.Substring(colon + 1) };
                for (int i = 1; i < head.Length; i++) { int eq = head[i].IndexOf('='); if (eq > 0) p.Params[head[i].Substring(0, eq)] = head[i].Substring(eq + 1).Trim('"'); }
                list.Add(p);
            }
            return list;
        }

        sealed class Zone { public List<int[]> Rules = new List<int[]>(); } // { offsetMinutes, month, week (−1 last), weekday, hour, minute }

        sealed class Raw
        {
            public string Uid, Summary, Location, Description, Url, Rrule, Status;
            public Prop Start, End;
            public List<DateTime> ExDates = new List<DateTime>();
            public DateTime? RecurrenceId;
        }

        public static List<Event> Parse(string ics, DateTime from, DateTime to)
        {
            var props = Lines(ics);
            var zones = new Dictionary<string, Zone>(StringComparer.OrdinalIgnoreCase);
            var raws = new List<Raw>();
            Raw cur = null;
            Zone zone = null;
            string zoneId = null, sub = null;
            int[] rule = null;
            foreach (var p in props)
            {
                if (p.Name == "BEGIN")
                {
                    if (p.Value == "VEVENT") cur = new Raw();
                    else if (p.Value == "VTIMEZONE") { zone = new Zone(); zoneId = null; }
                    else if (zone != null && (p.Value == "STANDARD" || p.Value == "DAYLIGHT")) { sub = p.Value; rule = new int[6]; rule[2] = 0; }
                    continue;
                }
                if (p.Name == "END")
                {
                    if (p.Value == "VEVENT" && cur != null) { raws.Add(cur); cur = null; }
                    else if (p.Value == "VTIMEZONE" && zone != null) { if (zoneId != null) zones[zoneId] = zone; zone = null; }
                    else if ((p.Value == "STANDARD" || p.Value == "DAYLIGHT") && zone != null && rule != null) { zone.Rules.Add(rule); rule = null; sub = null; }
                    continue;
                }
                if (zone != null)
                {
                    if (p.Name == "TZID" && sub == null) zoneId = p.Value;
                    else if (rule != null && p.Name == "TZOFFSETTO") rule[0] = OffsetMinutes(p.Value);
                    else if (rule != null && p.Name == "DTSTART" && p.Value.Length >= 13)
                    {
                        rule[1] = int.Parse(p.Value.Substring(4, 2)); rule[4] = int.Parse(p.Value.Substring(9, 2)); rule[5] = int.Parse(p.Value.Substring(11, 2));
                        rule[2] = 99; rule[3] = int.Parse(p.Value.Substring(6, 2)); // fixed date unless an RRULE says otherwise
                    }
                    else if (rule != null && p.Name == "RRULE")
                    {
                        var r = RuleParts(p.Value);
                        string v;
                        if (r.TryGetValue("BYMONTH", out v)) rule[1] = int.Parse(v);
                        if (r.TryGetValue("BYDAY", out v))
                        {
                            var m = Regex.Match(v, @"^([+-]?\d)?(SU|MO|TU|WE|TH|FR|SA)");
                            if (m.Success) { rule[2] = m.Groups[1].Success ? int.Parse(m.Groups[1].Value) : 1; rule[3] = Array.IndexOf(days, m.Groups[2].Value); }
                        }
                    }
                    continue;
                }
                if (cur == null) continue;
                switch (p.Name)
                {
                    case "UID": cur.Uid = p.Value; break;
                    case "SUMMARY": cur.Summary = Unescape(p.Value); break;
                    case "LOCATION": cur.Location = Unescape(p.Value); break;
                    case "DESCRIPTION": cur.Description = Unescape(p.Value); break;
                    case "URL": cur.Url = p.Value; break;
                    case "STATUS": cur.Status = p.Value; break;
                    case "RRULE": cur.Rrule = p.Value; break;
                    case "DTSTART": cur.Start = p; break;
                    case "DTEND": cur.End = p; break;
                    case "EXDATE": foreach (var v in p.Value.Split(',')) { var d = Time(new Prop { Value = v, Params = p.Params }, zones); if (d != null) cur.ExDates.Add(d.Value); } break;
                    case "RECURRENCE-ID": cur.RecurrenceId = Time(p, zones); break;
                    case "X-GOOGLE-CONFERENCE": if (cur.Url == null) cur.Url = p.Value; break;
                }
            }

            // moved or edited single occurrences replace the original one in the series
            var moved = new HashSet<string>();
            foreach (var r in raws) if (r.RecurrenceId != null) moved.Add(r.Uid + "|" + r.RecurrenceId.Value.Ticks);

            var list = new List<Event>();
            foreach (var r in raws)
            {
                if (r.Start == null || string.Equals(r.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase)) continue;
                var wall = Wall(r.Start);
                if (wall == null) continue;
                var conv = ToLocal(r.Start, zones);
                DateTime? start = conv(wall.Value);
                bool allDay = r.Start.Value.Length == 8 || string.Equals(Param(r.Start, "VALUE"), "DATE", StringComparison.OrdinalIgnoreCase);
                var end = r.End != null ? Time(r.End, zones) : null;
                var len = end != null && end.Value > start.Value ? end.Value - start.Value : (allDay ? TimeSpan.FromDays(1) : TimeSpan.FromHours(1));
                string link = MeetingLink(r.Url, r.Location, r.Description);
                foreach (var w in Occurrences(wall.Value, r.Rrule, conv, from, to))
                {
                    var s = conv(w);
                    if (r.ExDates.Contains(s)) continue;
                    if (r.RecurrenceId == null && r.Rrule != null && moved.Contains(r.Uid + "|" + s.Ticks)) continue;
                    if (s + len < from || s > to) continue;
                    list.Add(new Event { Uid = r.Uid, Title = string.IsNullOrEmpty(r.Summary) ? "(No title)" : r.Summary, Location = r.Location, Link = link, Start = s, End = s + len, AllDay = allDay });
                }
            }
            return list;
        }

        static readonly string[] days = { "SU", "MO", "TU", "WE", "TH", "FR", "SA" };

        static string Param(Prop p, string k) { string v; return p.Params.TryGetValue(k, out v) ? v : null; }

        static Dictionary<string, string> RuleParts(string rule)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in rule.Split(';')) { int eq = part.IndexOf('='); if (eq > 0) d[part.Substring(0, eq)] = part.Substring(eq + 1); }
            return d;
        }

        static int OffsetMinutes(string v)
        {
            var m = Regex.Match(v ?? "", @"^([+-])(\d{2})(\d{2})");
            if (!m.Success) return 0;
            int mins = int.Parse(m.Groups[2].Value) * 60 + int.Parse(m.Groups[3].Value);
            return m.Groups[1].Value == "-" ? -mins : mins;
        }

        static string Unescape(string s) { return (s ?? "").Replace("\\n", "\n").Replace("\\N", "\n").Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\"); }

        static readonly Dictionary<string, string> iana = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Asia/Kolkata", "India Standard Time" }, { "Asia/Calcutta", "India Standard Time" }, { "Europe/London", "GMT Standard Time" },
            { "America/New_York", "Eastern Standard Time" }, { "America/Chicago", "Central Standard Time" }, { "America/Denver", "Mountain Standard Time" },
            { "America/Los_Angeles", "Pacific Standard Time" }, { "Europe/Paris", "Romance Standard Time" }, { "Europe/Berlin", "W. Europe Standard Time" },
            { "Europe/Amsterdam", "W. Europe Standard Time" }, { "Asia/Dubai", "Arabian Standard Time" }, { "Asia/Singapore", "Singapore Standard Time" },
            { "Asia/Tokyo", "Tokyo Standard Time" }, { "Australia/Sydney", "AUS Eastern Standard Time" }, { "UTC", "UTC" }, { "Etc/UTC", "UTC" },
        };

        /// <summary>A DTSTART / DTEND value as local time on this PC.</summary>
        /// <summary>A DTSTART / DTEND value as local time on this PC.</summary>
        static DateTime? Time(Prop p, Dictionary<string, Zone> zones)
        {
            var w = Wall(p);
            return w == null ? null : (DateTime?)ToLocal(p, zones)(w.Value);
        }

        /// <summary>The value as written: the wall-clock time in the event's own zone (repeats are counted in it).</summary>
        static DateTime? Wall(Prop p)
        {
            string v = (p.Value ?? "").Trim();
            DateTime t;
            if (v.Length == 8)
                return DateTime.TryParseExact(v, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out t) ? t : (DateTime?)null;
            return DateTime.TryParseExact(v.TrimEnd('Z'), "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out t) ? t : (DateTime?)null;
        }

        /// <summary>Turns that zone's wall-clock times into this PC's local time (each one with its own daylight saving).</summary>
        static Func<DateTime, DateTime> ToLocal(Prop p, Dictionary<string, Zone> zones)
        {
            string v = (p.Value ?? "").Trim();
            if (v.Length == 8) return t => t; // all-day: a date, not a moment
            if (v.EndsWith("Z")) return t => DateTime.SpecifyKind(t, DateTimeKind.Utc).ToLocalTime();
            string tzid = Param(p, "TZID");
            if (tzid == null) return t => t; // "floating": already local
            Zone z;
            if (zones.TryGetValue(tzid, out z) && z.Rules.Count > 0)
                return t => DateTime.SpecifyKind(t.AddMinutes(-ZoneOffset(z, t)), DateTimeKind.Utc).ToLocalTime();
            string win;
            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById(iana.TryGetValue(tzid, out win) ? win : tzid);
                return t => { try { return TimeZoneInfo.ConvertTime(t, tz, TimeZoneInfo.Local); } catch { return t; } };
            }
            catch { return t => t; }
        }

        /// <summary>The UTC offset (minutes) a VTIMEZONE gives at local time t: the latest transition this year before t.</summary>
        static int ZoneOffset(Zone z, DateTime t)
        {
            if (z.Rules.Count == 1) return z.Rules[0][0];
            int best = z.Rules[0][0];
            DateTime bestAt = DateTime.MinValue;
            foreach (var y in new[] { t.Year - 1, t.Year })
                foreach (var r in z.Rules)
                {
                    DateTime at;
                    if (r[1] < 1 || r[1] > 12) continue;
                    if (r[2] == 99) at = new DateTime(y, r[1], Math.Min(r[3], DateTime.DaysInMonth(y, r[1])), r[4], r[5], 0);
                    else at = NthWeekday(y, r[1], r[2], r[3]).AddHours(r[4]).AddMinutes(r[5]);
                    if (at <= t && at > bestAt) { bestAt = at; best = r[0]; }
                }
            return best;
        }

        static DateTime NthWeekday(int year, int month, int n, int weekday)
        {
            if (weekday < 0) weekday = 0;
            if (n < 0)
            {
                var d = new DateTime(year, month, DateTime.DaysInMonth(year, month));
                while ((int)d.DayOfWeek != weekday) d = d.AddDays(-1);
                return d.AddDays(7 * (n + 1));
            }
            var f = new DateTime(year, month, 1);
            while ((int)f.DayOfWeek != weekday) f = f.AddDays(1);
            var res = f.AddDays(7 * (Math.Max(1, n) - 1));
            return res.Month == month ? res : res.AddDays(-7);
        }

        /// <summary>The start times of a (possibly repeating) event that fall in the window.</summary>
        static IEnumerable<DateTime> Occurrences(DateTime start, string rrule, Func<DateTime, DateTime> local, DateTime from, DateTime to)
        {
            if (rrule == null) { yield return start; yield break; }
            var r = RuleParts(rrule);
            string freq, v;
            r.TryGetValue("FREQ", out freq);
            int interval = r.TryGetValue("INTERVAL", out v) ? Math.Max(1, int.Parse(v)) : 1;
            int count = r.TryGetValue("COUNT", out v) ? int.Parse(v) : int.MaxValue;
            DateTime until = DateTime.MaxValue;
            if (r.TryGetValue("UNTIL", out v)) { var u = Time(new Prop { Value = v }, new Dictionary<string, Zone>()); if (u != null) until = u.Value.Date.AddDays(1); }
            var byDay = new List<int>();
            if (r.TryGetValue("BYDAY", out v))
                foreach (var d in v.Split(',')) { var m = Regex.Match(d, @"(SU|MO|TU|WE|TH|FR|SA)$"); if (m.Success) byDay.Add(Array.IndexOf(days, m.Groups[1].Value)); }
            int n = 0;
            var tod = start.TimeOfDay;
            for (int step = 0; step < 4000; step++)
            {
                IEnumerable<DateTime> batch;
                if (freq == "DAILY") batch = new[] { start.AddDays(step * interval) };
                else if (freq == "WEEKLY")
                {
                    var weekStart = start.Date.AddDays(-(int)start.DayOfWeek).AddDays(7 * step * interval);
                    var l = new List<DateTime>();
                    if (byDay.Count == 0) l.Add(start.AddDays(7 * step * interval));
                    else { var sorted = new List<int>(byDay); sorted.Sort(); foreach (var wd in sorted) l.Add(weekStart.AddDays(wd) + tod); }
                    batch = l;
                }
                else if (freq == "MONTHLY") batch = new[] { start.AddMonths(step * interval) };
                else if (freq == "YEARLY") batch = new[] { start.AddYears(step * interval) };
                else { yield return start; yield break; }
                bool past = false;
                foreach (var s in batch)
                {
                    if (s < start) continue;
                    if (local(s) >= until || n >= count) { past = true; break; }
                    n++;
                    if (local(s) > to) { past = true; break; }
                    if (local(s) >= from.AddDays(-1)) yield return s;
                }
                if (past) yield break;
            }
        }

        static readonly Regex rxLink = new Regex(@"https://[^\s""<>]*(teams\.microsoft\.com|teams\.live\.com|meet\.google\.com|zoom\.us|webex\.com|whereby\.com|gotomeeting\.com|meet\.jit\.si|chime\.aws)[^\s""<>]*", RegexOptions.IgnoreCase);

        static string MeetingLink(params string[] fields)
        {
            foreach (var f in fields)
            {
                if (string.IsNullOrEmpty(f)) continue;
                var m = rxLink.Match(f);
                if (m.Success) return m.Value.TrimEnd('.', ',', ')', '>');
            }
            return null;
        }

        // ------------------------------------------------------------------ --snapshot --demo

        static bool demo;

        public static void UseDemo()
        {
            demo = true;
            var t = DateTime.Today;
            var now = DateTime.Now;
            Events = new List<Event>
            {
                new Event { Title = "Design review", Start = now.AddMinutes(12), End = now.AddMinutes(57), Link = "https://meet.google.com/abc-defg-hij" },
                new Event { Title = "1:1 with Priya", Start = now.AddHours(2), End = now.AddHours(2.5), Link = "https://teams.microsoft.com/l/meetup-join/x" },
                new Event { Title = "Gym", Start = t.AddHours(19), End = t.AddHours(20), Location = "Fitness First" },
                new Event { Title = "Sprint planning", Start = t.AddDays(1).AddHours(10), End = t.AddDays(1).AddHours(11), Link = "https://zoom.us/j/123" },
            };
            Fetched = now;
        }
    }
}
