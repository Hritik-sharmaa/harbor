using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Harbor
{
    /// <summary>
    /// Harbor's own reminders: a text, an optional due time, done or not. Kept in %APPDATA%\Harbor\reminders.tsv.
    /// A due reminder shows a banner (and a sound); one timer is armed for the next one, nothing polls.
    /// </summary>
    static class Reminders
    {
        public sealed class Item
        {
            public string Id = Guid.NewGuid().ToString("N").Substring(0, 10), Text;
            public DateTime? Due;
            public bool Done, Notified;
            public DateTime Created = DateTime.Now, Finished;
        }

        static List<Item> items;
        static System.Windows.Forms.Timer alarm;
        public static event Action Changed;

        static string FilePath { get { return Path.Combine(Settings.Dir, "reminders.tsv"); } }

        public static List<Item> All { get { if (items == null) Load(); return items; } }

        /// <summary>Not done yet: due ones first (soonest first), then the rest newest first.</summary>
        public static List<Item> Open()
        {
            var l = All.FindAll(i => !i.Done);
            l.Sort((a, b) =>
            {
                if (a.Due.HasValue != b.Due.HasValue) return a.Due.HasValue ? -1 : 1;
                if (a.Due.HasValue) return a.Due.Value.CompareTo(b.Due.Value);
                return b.Created.CompareTo(a.Created);
            });
            return l;
        }

        public static int DoneToday { get { return All.FindAll(i => i.Done && i.Finished.Date == DateTime.Today).Count; } }

        public static Item Find(string id) { return All.Find(i => i.Id == id); }

        public static void Add(string text, DateTime? due)
        {
            text = (text ?? "").Trim();
            if (text == "") return;
            All.Add(new Item { Text = text, Due = due });
            Save();
        }

        public static void Toggle(Item i)
        {
            i.Done = !i.Done;
            i.Finished = i.Done ? DateTime.Now : DateTime.MinValue;
            Save();
        }

        public static void Remove(Item i) { All.Remove(i); Save(); }

        /// <summary>"in 25m", "today 18:00", "tomorrow 09:00", "overdue"...</summary>
        public static string DueText(Item i)
        {
            if (!i.Due.HasValue) return null;
            var d = i.Due.Value;
            var now = DateTime.Now;
            string time = d.ToString(CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains("H") ? "HH:mm" : "h:mm tt", CultureInfo.CurrentCulture);
            if (d < now) return "Overdue · " + (d.Date == now.Date ? time : d.ToString("d MMM", CultureInfo.CurrentCulture));
            if ((d - now).TotalMinutes < 60) return "in " + Math.Max(1, (int)Math.Ceiling((d - now).TotalMinutes)) + "m";
            if (d.Date == now.Date) return "Today " + time;
            if (d.Date == now.Date.AddDays(1)) return "Tomorrow " + time;
            return d.ToString("ddd d MMM", CultureInfo.CurrentCulture) + " " + time;
        }

        public static bool Overdue(Item i) { return !i.Done && i.Due.HasValue && i.Due.Value < DateTime.Now; }

        // ------------------------------------------------------------------ due alarms

        public static void Init()
        {
            if (items == null) Load();
            Arm();
        }

        /// <summary>One timer, set for the next due reminder (at most an hour ahead, then it re-arms).</summary>
        static void Arm()
        {
            if (alarm == null) { alarm = new System.Windows.Forms.Timer(); alarm.Tick += delegate { alarm.Stop(); Fire(); Arm(); }; }
            alarm.Stop();
            DateTime? next = null;
            foreach (var i in All) if (!i.Done && !i.Notified && i.Due.HasValue && (next == null || i.Due.Value < next.Value)) next = i.Due;
            if (next == null) return;
            double ms = Math.Max(500, Math.Min(3600000, (next.Value - DateTime.Now).TotalMilliseconds));
            alarm.Interval = (int)ms;
            alarm.Start();
        }

        static void Fire()
        {
            bool any = false;
            foreach (var i in All)
                if (!i.Done && !i.Notified && i.Due.HasValue && i.Due.Value <= DateTime.Now.AddSeconds(1))
                {
                    i.Notified = true;
                    any = true;
                    Banners.ShowLocal("Reminder", i.Text, DueText(i));
                }
            if (any) { try { System.Media.SystemSounds.Asterisk.Play(); } catch { } Save(); }
        }

        // ------------------------------------------------------------------ storage

        static void Load()
        {
            items = new List<Item>();
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (var line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    var p = line.Split('\t');
                    if (p.Length < 6) continue;
                    DateTime due, created, finished;
                    var it = new Item { Id = p[0], Done = p[1] == "1", Notified = p[2] == "1", Text = p[5].Replace("\\t", "\t").Replace("\\n", "\n") };
                    if (DateTime.TryParse(p[3], CultureInfo.InvariantCulture, DateTimeStyles.None, out due)) it.Due = due;
                    if (DateTime.TryParse(p[4], CultureInfo.InvariantCulture, DateTimeStyles.None, out created)) it.Created = created;
                    if (p.Length > 6 && DateTime.TryParse(p[6], CultureInfo.InvariantCulture, DateTimeStyles.None, out finished)) it.Finished = finished;
                    items.Add(it);
                }
            }
            catch (Exception ex) { App.Log(ex); }
        }

        static void Save()
        {
            // finished reminders are kept for a week, then cleaned up
            items.RemoveAll(i => i.Done && i.Finished != DateTime.MinValue && (DateTime.Now - i.Finished).TotalDays > 7);
            try
            {
                var sb = new StringBuilder();
                foreach (var i in items)
                    sb.Append(i.Id).Append('\t').Append(i.Done ? "1" : "0").Append('\t').Append(i.Notified ? "1" : "0").Append('\t')
                      .Append(i.Due.HasValue ? i.Due.Value.ToString("s", CultureInfo.InvariantCulture) : "").Append('\t')
                      .Append(i.Created.ToString("s", CultureInfo.InvariantCulture)).Append('\t')
                      .Append(i.Text.Replace("\t", "\\t").Replace("\n", "\\n")).Append('\t')
                      .Append(i.Finished == DateTime.MinValue ? "" : i.Finished.ToString("s", CultureInfo.InvariantCulture)).Append('\n');
                Directory.CreateDirectory(Settings.Dir);
                File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex) { App.Log(ex); }
            Arm();
            if (Changed != null) Changed();
        }

        /// <summary>--snapshot --demo: sample reminders (not saved).</summary>
        public static void UseDemo()
        {
            var t = DateTime.Today;
            items = new List<Item>
            {
                new Item { Text = "Review the pull request for checkout", Due = DateTime.Now.AddMinutes(25) },
                new Item { Text = "Call the dentist", Due = t.AddHours(17.5) < DateTime.Now ? t.AddDays(1).AddHours(10) : t.AddHours(17.5) },
                new Item { Text = "Buy coffee beans" },
                new Item { Text = "Send weekly update", Done = true, Finished = DateTime.Now.AddHours(-2) },
            };
        }
    }
}
