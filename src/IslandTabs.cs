using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace Harbor
{
    /// <summary>The island's Calendar, Timers, Notes and Downloads tabs, which tabs show, and using the island from the keyboard.</summary>
    sealed partial class Island
    {
        // ------------------------------------------------------------------ which tabs show, in what order

        /// <summary>Every tab with whether it shows, in your order. Saved as "media,-ai,..." ("-" = hidden); tabs added in later versions show up at the end.</summary>
        public static List<KeyValuePair<string, bool>> TabConfig()
        {
            var list = new List<KeyValuePair<string, bool>>();
            string saved = Look.Get("IslandTabs");
            if (saved.Trim() != "")
                foreach (var part in saved.Split(','))
                {
                    string id = part.Trim().TrimStart('-');
                    if (Array.IndexOf(allTabs, id) >= 0 && !list.Exists(kv => kv.Key == id)) list.Add(new KeyValuePair<string, bool>(id, !part.Trim().StartsWith("-")));
                }
            bool claude = ClaudeHistory.Available || App.SnapshotMode;
            foreach (var id in allTabs)
                if (!list.Exists(kv => kv.Key == id))
                    list.Add(new KeyValuePair<string, bool>(id, claude || (id != "ai" && id != "usage"))); // the Claude tabs only where Claude Code is used
            return list;
        }

        public static void SaveTabConfig(List<KeyValuePair<string, bool>> list)
        {
            var parts = new List<string>();
            foreach (var kv in list) parts.Add((kv.Value ? "" : "-") + kv.Key);
            Look.Set("IslandTabs", string.Join(",", parts.ToArray()));
            Settings.Save();
            if (inst != null && inst.Visible) inst.Refresh2();
        }

        static List<string> ShownTabs()
        {
            var l = new List<string>();
            foreach (var kv in TabConfig()) if (kv.Value) l.Add(kv.Key);
            if (l.Count == 0) l.Add("dashboard");
            return l;
        }

        public static string NameOf(string id) { int i = Array.IndexOf(allTabs, id); return i >= 0 ? allNames[i] : id; }
        public static string GlyphOf(string id) { int i = Array.IndexOf(allTabs, id); return i >= 0 ? allGlyphs[i] : null; }

        /// <summary>Opens the island on a tab, under the menu bar of the screen you're working on.</summary>
        public static void OpenTab(string t)
        {
            IntPtr mon = N.MonitorFromWindow(N.GetForegroundWindow(), 2);
            var d = App.Displays.Find(x => x.HMon == mon && x.Bar != null && x.Bar.IsHandleCreated) ?? App.PrimaryDisplay;
            if (d == null || d.Bar == null || !d.Bar.IsHandleCreated) return;
            var sb = d.Bar.ScreenBounds;
            Open(d.Bar, new RectangleF(sb.Width / 2f - 120 * d.Scale, 0, 240 * d.Scale, sb.Height), t);
        }

        // ------------------------------------------------------------------ the keyboard

        bool byKeyboard;   // opened with the shortcut: stays until Esc or a click elsewhere, and takes keys
        IntPtr pasteTo;    // the window you were in, for "paste" from the clipboard tab
        int clipSel;

        /// <summary>Alt+Win+Space (or Alt+Win+V for the clipboard): open the island and let the keyboard drive it; again to close.</summary>
        public static void ToggleFromKeyboard(string t)
        {
            if (inst != null && inst.Visible && inst.byKeyboard && (t == null || inst.tab == t)) { inst.CloseFlyout(); return; }
            IntPtr was = N.GetForegroundWindow();
            OpenTab(t);
            if (inst == null || !inst.Visible) return;
            inst.byKeyboard = true;
            inst.pasteTo = was;
            inst.clipSel = 0;
            inst.Activate();
            N.SetForegroundWindow(inst.Handle);
            inst.Refresh2();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (FocusField == null && HandleKey(keyData)) return true;
            return base.ProcessCmdKey(ref msg, keyData);
        }

        bool HandleKey(Keys k)
        {
            var t = tabs;
            int i = Array.IndexOf(t, tab);
            Keys key = k & Keys.KeyCode;
            bool shift = (k & Keys.Shift) != 0;
            if ((k & (Keys.Control | Keys.Alt)) != 0) return false;
            if (tab == "clipboard" && (key == Keys.Down || key == Keys.Up || key == Keys.Enter))
            {
                var list = ClipboardHistory.Items.FindAll(c => clipFilter == 0 || (clipFilter == 1 ? c.Kind == "text" : c.Kind == "image"));
                if (list.Count == 0) return false;
                if (key == Keys.Enter) { PasteClip(list[Math.Max(0, Math.Min(clipSel, list.Count - 1))]); return true; }
                clipSel = Math.Max(0, Math.Min(list.Count - 1, clipSel + (key == Keys.Down ? 1 : -1)));
                if (clipSel < clipFirst) clipFirst = clipSel;
                if (clipSel >= clipFirst + 4) clipFirst = clipSel - 3;
                Refresh2();
                return true;
            }
            int to = -1;
            if (key == Keys.Right || (key == Keys.Tab && !shift)) to = (i + 1) % t.Length;
            else if (key == Keys.Left || (key == Keys.Tab && shift)) to = (i - 1 + t.Length) % t.Length;
            else if (key >= Keys.D1 && key <= Keys.D9 && key - Keys.D1 < t.Length) to = key - Keys.D1;
            else if (key >= Keys.NumPad1 && key <= Keys.NumPad9 && key - Keys.NumPad1 < t.Length) to = key - Keys.NumPad1;
            if (to < 0) return false;
            SetTab(t[to], true);
            Refresh2();
            return true;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

        /// <summary>Copies it, goes back to the window you were in and pastes it there.</summary>
        void PasteClip(ClipboardHistory.Item it)
        {
            ClipboardHistory.CopyBack(it);
            var target = pasteTo;
            CloseFlyout();
            if (target == IntPtr.Zero) return;
            N.SetForegroundWindow(target);
            var t = new Timer { Interval = 120 };
            t.Tick += delegate
            {
                t.Dispose();
                keybd_event(0x11, 0, 0, UIntPtr.Zero);       // Ctrl
                keybd_event(0x56, 0, 0, UIntPtr.Zero);       // V
                keybd_event(0x56, 0, 2, UIntPtr.Zero);
                keybd_event(0x11, 0, 2, UIntPtr.Zero);
            };
            t.Start();
        }

        // ------------------------------------------------------------------ dashboard: the next meeting

        void UpNextMeeting(UI ui, RectangleF r, CalendarFeed.Event e, Fonts2 f)
        {
            using (var gf = new Font(Fonts.Icons, 11f * S, GraphicsUnit.Pixel))
            using (var red = new SolidBrush(Color.FromArgb(255, 255, 110, 110)))
                ui.Glyph("", new RectangleF(r.X - 1 * S, r.Y + 1 * S, 17 * S, 19 * S), gf, red);
            float jw = e.Link != null ? PillWidth(ui, "Join", f.Small) : 0;
            string when = MeetingWhen(e);
            float ww = ui.G.MeasureString(when, f.Small).Width + 4 * S;
            ui.Text(e.Title, new RectangleF(r.X + 24 * S, r.Y, r.Width - 24 * S - ww - jw - 6 * S, r.Height), f.Body, f.White);
            ui.Text(when, new RectangleF(r.X, r.Y, r.Width - jw - 4 * S, r.Height), f.Small, f.Faint, 2);
            if (e.Link != null)
            {
                var jr = new RectangleF(r.Right - jw, r.Y + 1 * S, jw, 19 * S);
                using (var dark = new SolidBrush(Color.FromArgb(235, 18, 18, 22)))
                    Pill(ui, jr, "Join", Color.FromArgb(Hover == "djoin" ? 255 : 230, 255, 255, 255), f.Small, dark);
                ui.AddHit("djoin", jr, () => CalendarFeed.Join(e));
            }
        }

        static string MeetingWhen(CalendarFeed.Event e)
        {
            var now = DateTime.Now;
            if (e.Start <= now) return "now";
            if ((e.Start - now).TotalMinutes < 60) return "in " + (int)Math.Ceiling((e.Start - now).TotalMinutes) + "m";
            return e.Start.ToString("t", CultureInfo.CurrentCulture);
        }

        // ------------------------------------------------------------------ media: the last song, after its app closed

        void LastPlayedCard(UI ui, RectangleF c, Fonts2 f)
        {
            float lw = 300 * S;
            var art = new RectangleF(c.X, c.Y + 8 * S, 120 * S, 120 * S);
            if (LastPlayed.Art != null)
            {
                var st = ui.G.Save();
                using (var clip = Gfx.RoundRect(art, 14 * S)) ui.G.SetClip(clip);
                ui.G.InterpolationMode = InterpolationMode.HighQualityBicubic;
                ui.G.DrawImage(LastPlayed.Art, art);
                using (var dim = new SolidBrush(Color.FromArgb(70, 0, 0, 0))) ui.G.FillRectangle(dim, art);
                ui.G.Restore(st);
            }
            else
            {
                ui.Fill(art, Color.FromArgb(30, 255, 255, 255), 14 * S);
                using (var gf = new Font(Fonts.Icons, 34f * S, GraphicsUnit.Pixel)) ui.Glyph("", art, gf, f.Faint);
            }
            float tx = art.Right + 18 * S, tw = c.Right - tx;
            using (var cf = new Font(Fonts.UiBold, 10.5f * S, GraphicsUnit.Pixel))
                ui.Text("LAST PLAYED", new RectangleF(tx, art.Y + 4 * S, tw, 16 * S), cf, f.Faint);
            ui.Text(LastPlayed.Title, new RectangleF(tx, art.Y + 24 * S, tw, 26 * S), f.Title, f.White);
            ui.Text(LastPlayed.Artist, new RectangleF(tx, art.Y + 50 * S, tw, 20 * S), f.Body, f.Dim);
            ui.Text((string.IsNullOrEmpty(LastPlayed.AppName) ? "" : LastPlayed.AppName + " · ") + Ago(LastPlayed.At), new RectangleF(tx, art.Y + 72 * S, tw, 18 * S), f.Small, f.Faint);
            string label = LastPlayed.Resuming ? "Opening " + (LastPlayed.AppName ?? "the app") + "…" : LastPlayed.PlayLabel;
            var pr = new RectangleF(tx, art.Bottom - 30 * S, PillWidth(ui, label, f.Bold) + 30 * S, 30 * S);
            using (var dark = new SolidBrush(Color.FromArgb(235, 18, 18, 22)))
            {
                ui.Fill(pr, Color.FromArgb(LastPlayed.Resuming ? 140 : (Hover == "lpplay" ? 255 : 235), 255, 255, 255), 15 * S);
                using (var gf = new Font(Fonts.Icons, 11f * S, GraphicsUnit.Pixel)) ui.Glyph(LastPlayed.FromBrowser ? "" : "", new RectangleF(pr.X + 8 * S, pr.Y, 20 * S, pr.Height), gf, dark);
                ui.Text(label, new RectangleF(pr.X + 28 * S, pr.Y, pr.Width - 34 * S, pr.Height), f.Bold, dark, 1);
            }
            if (!LastPlayed.Resuming) ui.AddHit("lpplay", pr, LastPlayed.Resume, true);
        }

        // ------------------------------------------------------------------ calendar

        void CalendarTab(UI ui, RectangleF c, Fonts2 f)
        {
            ui.Text("Calendar", new RectangleF(c.X, c.Y - 2 * S, 200 * S, 22 * S), f.Title, f.White);
            if (!CalendarFeed.Configured)
            {
                using (var gf = new Font(Fonts.Icons, 26f * S, GraphicsUnit.Pixel)) ui.Glyph("", new RectangleF(c.X, c.Y + 34 * S, c.Width, 36 * S), gf, f.Faint);
                ui.Text("See your next meetings here, with a Join button", new RectangleF(c.X, c.Y + 76 * S, c.Width, 22 * S), f.Bold, f.White, 1);
                ui.Text("Paste your calendar's private iCal link in Harbor → General → Calendar (Google or Outlook, no sign-in)", new RectangleF(c.X, c.Y + 100 * S, c.Width, 20 * S), f.Small, f.Dim, 1);
                var go = new RectangleF(c.X + c.Width / 2 - 80 * S, c.Y + 132 * S, 160 * S, 28 * S);
                using (var dark = new SolidBrush(Color.FromArgb(235, 18, 18, 22)))
                    Pill(ui, go, "Set up calendar", Color.FromArgb(Hover == "calset" ? 255 : 230, 255, 255, 255), f.Bold, dark);
                ui.AddHit("calset", go, () => { CloseFlyout(); SettingsWindow.ShowWindow(); });
                return;
            }
            var now = DateTime.Now;
            float lw = 250 * S;
            // left: the next meeting, big
            var card = new RectangleF(c.X, c.Y + 28 * S, lw, c.Bottom - c.Y - 28 * S);
            Card(ui, card);
            var next = CalendarFeed.Next();
            float ix = card.X + 16 * S, iw = card.Width - 32 * S;
            using (var cf = new Font(Fonts.UiBold, 10.5f * S, GraphicsUnit.Pixel))
                ui.Text(next != null && next.Start <= now ? "NOW" : "NEXT", new RectangleF(ix, card.Y + 12 * S, iw, 16 * S), cf, f.Faint);
            if (next == null)
                ui.Text(CalendarFeed.Busy ? "Loading…" : "Nothing coming up", new RectangleF(ix, card.Y + 34 * S, iw, 24 * S), f.Title, f.White);
            else
            {
                using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisWord })
                    ui.G.DrawString(next.Title, f.Title, f.White, new RectangleF(ix, card.Y + 32 * S, iw, 44 * S), sf);
                string when = next.Start.Date == now.Date ? "" : next.Start.ToString("ddd ", CultureInfo.CurrentCulture);
                when += next.Start.ToString("t", CultureInfo.CurrentCulture) + " – " + next.End.ToString("t", CultureInfo.CurrentCulture);
                ui.Text(when + "  ·  " + MeetingWhen(next), new RectangleF(ix, card.Y + 78 * S, iw, 18 * S), f.Small, f.Dim);
                if (next.Link != null)
                {
                    var jr = new RectangleF(ix, card.Bottom - 44 * S, iw, 30 * S);
                    using (var dark = new SolidBrush(Color.FromArgb(235, 18, 18, 22)))
                        Pill(ui, jr, "Join meeting", Color.FromArgb(Hover == "calnext" ? 255 : 235, 255, 255, 255), f.Bold, dark);
                    var ev = next;
                    ui.AddHit("calnext", jr, () => CalendarFeed.Join(ev));
                }
                else if (!string.IsNullOrEmpty(next.Location))
                    ui.Text(next.Location, new RectangleF(ix, card.Bottom - 40 * S, iw, 20 * S), f.Small, f.Faint);
            }

            // right: today and tomorrow
            float rx = c.X + lw + 22 * S, rw = c.Right - rx, y = c.Y - 2 * S;
            string err = CalendarFeed.Error;
            if (err != null) ui.Text(err, new RectangleF(rx, y, rw, 18 * S), f.Small, f.Faint, 2);
            foreach (var day in new[] { now.Date, now.Date.AddDays(1) })
            {
                if (y + 40 * S > c.Bottom) break;
                var evs = CalendarFeed.Events.FindAll(e => e.Start.Date == day || (e.AllDay && e.Start <= day && e.End > day));
                ui.Text(day == now.Date ? "Today" : "Tomorrow", new RectangleF(rx, y, rw, 20 * S), f.Bold, f.White);
                y += 24 * S;
                if (evs.Count == 0) { ui.Text("No events", new RectangleF(rx, y, rw, 20 * S), f.Small, f.Faint); y += 24 * S; continue; }
                foreach (var e in evs)
                {
                    if (y + 24 * S > c.Bottom) break;
                    bool past = !e.AllDay && e.End <= now;
                    string time = e.AllDay ? "All day" : e.Start.ToString("t", CultureInfo.CurrentCulture);
                    ui.Text(time, new RectangleF(rx, y, 64 * S, 22 * S), f.Small, past ? f.Faint : f.Dim);
                    using (var bar = new SolidBrush(past ? Color.FromArgb(60, 255, 110, 110) : Color.FromArgb(255, 255, 110, 110))) ui.G.FillRectangle(bar, rx + 66 * S, y + 4 * S, 3 * S, 14 * S);
                    float jw = e.Link != null && !past ? PillWidth(ui, "Join", f.Small) : 0;
                    ui.Text(e.Title, new RectangleF(rx + 76 * S, y, rw - 80 * S - jw, 22 * S), f.Body, past ? f.Faint : f.White);
                    if (jw > 0)
                    {
                        string id = "calj" + e.Uid + e.Start.Ticks;
                        var jr = new RectangleF(rx + rw - jw, y + 1 * S, jw, 20 * S);
                        Pill(ui, jr, "Join", Color.FromArgb(Hover == id ? 70 : 36, 255, 255, 255), f.Small, f.White);
                        var ev = e;
                        ui.AddHit(id, jr, () => CalendarFeed.Join(ev));
                    }
                    y += 24 * S;
                }
                y += 6 * S;
            }
        }

        // ------------------------------------------------------------------ timers, alarms, world clocks

        string timersNote;

        void Field(UI ui, string id, string placeholder, float x, float y, float w, Action submit)
        {
            ui.Pad = x - 8 * S;
            ui.Right = x + w + 8 * S;
            ui.Y = y;
            ui.TextField(id, placeholder, false, submit);
        }

        void TimersTab(UI ui, RectangleF c, Fonts2 f)
        {
            float gap = 22 * S, w1 = 236 * S, w2 = 176 * S, w3 = c.Width - w1 - w2 - 2 * gap;
            float x1 = c.X, x2 = x1 + w1 + gap, x3 = x2 + w2 + gap;

            // ---- timers
            ui.Text("Timers", new RectangleF(x1, c.Y - 2 * S, w1, 22 * S), f.Title, f.White);
            float cx = x1, cy = c.Y + 26 * S;
            foreach (var mins in new[] { 1, 5, 10, 15, 25 })
            {
                string label = mins + "m", id = "tq" + mins;
                float pw = PillWidth(ui, label, f.Small) + 2 * S;
                var pr = new RectangleF(cx, cy, pw, 22 * S);
                Pill(ui, pr, label, Color.FromArgb(Hover == id ? 60 : 28, 255, 255, 255), f.Small, f.White);
                int m = mins;
                ui.AddHit(id, pr, () => Timers.Start(TimeSpan.FromMinutes(m), null), true);
                cx += pw + 6 * S;
            }
            Field(ui, "tmr", "Custom: 12, 1:30, 45s…", x1, cy + 24 * S, w1, () =>
            {
                var len = Timers.Parse(Fields.ContainsKey("tmr") ? Fields["tmr"] : "");
                if (len <= TimeSpan.Zero) { timersNote = "Try 12, 1:30 or 45s"; return; }
                timersNote = null;
                Timers.Start(len, null);
                Fields["tmr"] = "";
            });
            float y = cy + 66 * S;
            if (Timers.Running.Count == 0) ui.Text(timersNote ?? "Pick a time, or type one and press Enter", new RectangleF(x1, y + 4 * S, w1, 18 * S), f.Small, f.Faint);
            foreach (var t in Timers.Running)
            {
                if (y + 34 * S > c.Bottom) break;
                var ring = new RectangleF(x1 + 2 * S, y + 6 * S, 22 * S, 22 * S);
                using (var track = new Pen(Color.FromArgb(40, 255, 255, 255), 3 * S)) ui.G.DrawEllipse(track, ring);
                using (var arc = new Pen(Color.FromArgb(255, 255, 179, 64), 3 * S)) ui.G.DrawArc(arc, ring, -90, 360 * (1 - t.Progress));
                ui.Text(Timers.Text(t.Left), new RectangleF(x1 + 34 * S, y + 1 * S, 90 * S, 20 * S), f.Bold, t.Paused ? f.Dim : f.White);
                ui.Text(t.Label + (t.Paused ? " · paused" : ""), new RectangleF(x1 + 34 * S, y + 18 * S, w1 - 100 * S, 16 * S), f.Small, f.Faint);
                var cd = t;
                HeaderButton(ui, "tp" + t.Id, new RectangleF(x1 + w1 - 56 * S, y + 6 * S, 24 * S, 24 * S), t.Paused ? "" : "", true, () => Timers.TogglePause(cd), f);
                HeaderButton(ui, "tx" + t.Id, new RectangleF(x1 + w1 - 28 * S, y + 6 * S, 24 * S, 24 * S), "", true, () => Timers.Remove(cd), f);
                y += 36 * S;
            }

            // ---- alarms
            ui.Text("Alarms", new RectangleF(x2, c.Y - 2 * S, w2, 22 * S), f.Title, f.White);
            Field(ui, "alm", "7:30 Wake up", x2, c.Y + 20 * S, w2, () =>
            {
                timersNote = Timers.AddAlarm(Fields.ContainsKey("alm") ? Fields["alm"] : "");
                if (timersNote == null) Fields["alm"] = "";
            });
            y = c.Y + 64 * S;
            if (Timers.Alarms.Count == 0) ui.Text("Type a time and press Enter", new RectangleF(x2, y + 4 * S, w2, 18 * S), f.Small, f.Faint);
            foreach (var a in Timers.Alarms)
            {
                if (y + 34 * S > c.Bottom) break;
                string id = "al" + a.Id;
                bool hot = Hover == id || Hover == "alx" + a.Id || Hover == "als" + a.Id;
                ui.AddHit(id, new RectangleF(x2, y, w2, 34 * S), null);
                ui.Text(Timers.AlarmText(a), new RectangleF(x2, y + 1 * S, w2 - 50 * S, 20 * S), f.Bold, a.On ? f.White : f.Faint);
                ui.Text(a.Label, new RectangleF(x2, y + 18 * S, w2 - 50 * S, 16 * S), f.Small, f.Faint);
                var al = a;
                var sw = new RectangleF(x2 + w2 - 38 * S, y + 8 * S, 34 * S, 20 * S);
                ui.Switch(sw, a.On);
                ui.AddHit("als" + a.Id, sw, () => Timers.ToggleAlarm(al), true);
                if (hot) HeaderButton(ui, "alx" + a.Id, new RectangleF(sw.X - 28 * S, y + 6 * S, 24 * S, 24 * S), "", true, () => Timers.RemoveAlarm(al), f);
                y += 36 * S;
            }

            // ---- world clocks
            ui.Text("World clocks", new RectangleF(x3, c.Y - 2 * S, w3, 22 * S), f.Title, f.White);
            Field(ui, "clk", "Add a city", x3, c.Y + 20 * S, w3, () =>
            {
                timersNote = Timers.AddClock(Fields.ContainsKey("clk") ? Fields["clk"] : "");
                if (timersNote == null) Fields["clk"] = "";
            });
            y = c.Y + 64 * S;
            if (Timers.Clocks.Count == 0) ui.Text(timersNote ?? "e.g. London, Tokyo, New York", new RectangleF(x3, y + 4 * S, w3, 18 * S), f.Small, f.Faint);
            var now = DateTime.Now;
            foreach (var k in Timers.Clocks)
            {
                if (y + 34 * S > c.Bottom) break;
                var tz = k.Tz;
                if (tz == null) continue;
                var there = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.Local, tz);
                double diff = (tz.GetUtcOffset(now) - TimeZoneInfo.Local.GetUtcOffset(now)).TotalHours;
                string id = "ck" + k.Label;
                bool hot = Hover == id || Hover == "ckx" + k.Label;
                ui.AddHit(id, new RectangleF(x3, y, w3, 34 * S), null);
                ui.Text(k.Label, new RectangleF(x3, y + 1 * S, w3 - 70 * S, 20 * S), f.Body, f.White);
                string rel = (there.Date > now.Date ? "Tomorrow, " : there.Date < now.Date ? "Yesterday, " : "Today, ") + (diff >= 0 ? "+" : "−") + FormatHours(Math.Abs(diff));
                ui.Text(rel, new RectangleF(x3, y + 18 * S, w3 - 70 * S, 16 * S), f.Small, f.Faint);
                ui.Text(there.ToString("t", CultureInfo.CurrentCulture), new RectangleF(x3, y + 4 * S, w3 - (hot ? 30 : 0) * S, 22 * S), f.Bold, f.White, 2);
                var ck = k;
                if (hot) HeaderButton(ui, "ckx" + k.Label, new RectangleF(x3 + w3 - 24 * S, y + 6 * S, 24 * S, 24 * S), "", true, () => Timers.RemoveClock(ck), f);
                y += 36 * S;
            }
            if (timersNote != null && Timers.Running.Count > 0)
                ui.Text(timersNote, new RectangleF(x2, c.Bottom - 16 * S, c.Right - x2, 16 * S), f.Small, f.Faint);
        }

        static string FormatHours(double h)
        {
            int mins = (int)Math.Round(h * 60);
            return mins % 60 == 0 ? (mins / 60) + "h" : (mins / 60) + ":" + (mins % 60).ToString("00") + "h";
        }

        // ------------------------------------------------------------------ notes

        int notesFirst;
        Notes.Note copiedNote;

        void NotesTab(UI ui, RectangleF c, Fonts2 f)
        {
            float lw = 236 * S;
            ui.Text("Notes", new RectangleF(c.X, c.Y - 2 * S, lw, 22 * S), f.Title, f.White);
            Field(ui, "note", "Jot something down…", c.X, c.Y + 20 * S, lw, () =>
            {
                Notes.Add(Fields.ContainsKey("note") ? Fields["note"] : "");
                Fields["note"] = "";
                notesFirst = 0;
            });
            ui.G.DrawString("Enter to save. Click a note to copy it; pin the ones you keep using.", f.Small, f.Faint, new RectangleF(c.X, c.Y + 66 * S, lw, 40 * S));

            float rx = c.X + lw + 24 * S, rw = c.Right - rx, y = c.Y - 2 * S;
            var all = Notes.All;
            if (all.Count == 0) { ui.Text("No notes yet", new RectangleF(rx, c.Y + 60 * S, rw, 22 * S), f.Body, f.Faint, 1); return; }
            notesFirst = Math.Max(0, Math.Min(notesFirst, all.Count - 1));
            for (int i = notesFirst; i < all.Count && y + 44 * S <= c.Bottom + 4 * S; i++)
            {
                var n = all[i];
                string id = "nt" + n.Id;
                bool hot = Hover == id || Hover == "ntp" + n.Id || Hover == "ntx" + n.Id;
                var row = new RectangleF(rx - 8 * S, y, rw + 8 * S, 44 * S);
                GlassShape(ui, row, 12 * S, hot ? 22 : 10, hot ? 40 : 18);
                if (n.Pinned)
                    using (var gf = new Font(Fonts.Icons, 9f * S, GraphicsUnit.Pixel)) ui.Glyph("", new RectangleF(row.X + 6 * S, row.Y + 4 * S, 14 * S, 14 * S), gf, f.Dim);
                using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisWord })
                    ui.G.DrawString(n.Text, f.Body, f.White, new RectangleF(rx + (n.Pinned ? 10 * S : 0), y + 4 * S, rw - (hot ? 64 : 4) * S - (n.Pinned ? 10 * S : 0), 38 * S), sf);
                var note = n;
                ui.AddHit(id, row, () => { try { Clipboard.SetText(note.Text); } catch { } copiedNote = note; }, true);
                if (copiedNote == n)
                    using (var dark = new SolidBrush(Color.FromArgb(240, 18, 18, 22)))
                        Pill(ui, new RectangleF(row.Right - 78 * S, row.Y + 11 * S, 70 * S, 22 * S), "Copied", Color.FromArgb(235, 255, 255, 255), f.Small, dark);
                else if (hot)
                {
                    HeaderButton(ui, "ntp" + n.Id, new RectangleF(row.Right - 58 * S, row.Y + 10 * S, 24 * S, 24 * S), n.Pinned ? "" : "", true, () => Notes.TogglePin(note), f);
                    HeaderButton(ui, "ntx" + n.Id, new RectangleF(row.Right - 30 * S, row.Y + 10 * S, 24 * S, 24 * S), "", true, () => Notes.Remove(note), f);
                }
                y += 50 * S;
            }
            if (Hover == null) copiedNote = null;
        }

        // ------------------------------------------------------------------ downloads

        int dlChecked;
        bool dlMoving;

        /// <summary>Something is downloading (checked at most once a second): the tab redraws to show it grow.</summary>
        bool DownloadsMoving(int now)
        {
            if (unchecked(now - dlChecked) < 1000) return false;
            dlChecked = now;
            bool moving = Downloads.Active().Count > 0;
            bool redraw = moving || dlMoving; // one more frame after it finishes
            dlMoving = moving;
            return redraw;
        }

        void DownloadsTab(UI ui, RectangleF c, Fonts2 f)
        {
            float lw = 236 * S;
            ui.Text("Downloads", new RectangleF(c.X, c.Y - 2 * S, lw, 22 * S), f.Title, f.White);
            var active = Downloads.Active();
            float y = c.Y + 28 * S;
            if (active.Count == 0) ui.Text("Nothing downloading right now", new RectangleF(c.X, y, lw, 20 * S), f.Small, f.Faint);
            foreach (var a in active)
            {
                if (y + 40 * S > c.Bottom - 34 * S) break;
                ui.Text(a.Name, new RectangleF(c.X, y, lw, 20 * S), f.Body, f.White);
                ui.Text(Downloads.Size(a.Size) + " so far", new RectangleF(c.X, y, lw, 20 * S), f.Small, f.Faint, 2);
                // no total is known while the browser writes: a moving band says "in progress"
                var bar = new RectangleF(c.X, y + 24 * S, lw, 4 * S);
                ui.Fill(bar, Color.FromArgb(34, 255, 255, 255), 2 * S);
                float band = lw * 0.3f, pos = (Environment.TickCount % 1600) / 1600f * (lw + band) - band;
                var seg = RectangleF.Intersect(bar, new RectangleF(bar.X + pos, bar.Y, band, bar.Height));
                if (seg.Width > 0) ui.Fill(seg, Color.FromArgb(255, 100, 180, 255), 2 * S);
                y += 40 * S;
            }
            var open = new RectangleF(c.X, c.Bottom - 30 * S, 170 * S, 28 * S);
            Pill(ui, open, "Open Downloads folder", Color.FromArgb(Hover == "dlopen" ? 60 : 30, 255, 255, 255), f.Small, f.White);
            ui.AddHit("dlopen", open, () => Launcher.Open(Downloads.Folder));

            // the latest files: open, drag out, right-click to show in Explorer
            float rx = c.X + lw + 24 * S, rw = c.Right - rx;
            ui.Text("Recent", new RectangleF(rx, c.Y - 2 * S, rw, 22 * S), f.Bold, f.White);
            const int cols = 4;
            float gap = 6 * S, tw = (rw - (cols - 1) * gap) / cols, th = (c.Bottom - c.Y - 26 * S - gap) / 2, isz = 36 * S;
            var recent = Downloads.Recent(8);
            if (recent.Count == 0) ui.Text("Your Downloads folder is empty", new RectangleF(rx, c.Y + 60 * S, rw, 20 * S), f.Small, f.Faint, 1);
            for (int i = 0; i < recent.Count; i++)
            {
                var e = recent[i];
                var r = new RectangleF(rx + (i % cols) * (tw + gap), c.Y + 26 * S + (i / cols) * (th + gap), tw, th);
                string id = "dl" + i, p = e.FullName;
                if (Hover == id) GlassShape(ui, r, 12 * S, 26, 50);
                var ic = FileIcon(p, isz);
                if (ic != null) ui.G.DrawImage(ic, new RectangleF(r.X + (tw - isz) / 2, r.Y + 6 * S, isz, isz));
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter })
                    ui.G.DrawString(e.Name, f.Small, f.White, new RectangleF(r.X + 3 * S, r.Y + isz + 9 * S, tw - 6 * S, th - isz - 10 * S), sf);
                ui.AddHit(id, r, () => { if (LastButton == MouseButtons.Right) Launcher.Reveal(p); else Launcher.Open(p); }, true);
                drags[id] = () => ShellDrag.Drag(Handle, p);
            }
        }
    }
}
