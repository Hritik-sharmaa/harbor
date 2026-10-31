using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace Harbor
{
    /// <summary>
    /// The island's Dashboard: one big card that follows your day (a meeting about to start, a running timer, the song
    /// playing, or a greeting with today's date), a weather card painted like the sky outside, what's up next, and a
    /// strip with your apps and quick toggles that light up in their own colour.
    /// </summary>
    sealed partial class Island
    {
        void DashboardTab(UI ui, RectangleF c, Fonts2 f)
        {
            float gap = 12 * S, heroW = 268 * S, stripH = 54 * S;
            var hero = new RectangleF(c.X, c.Y, heroW, c.Height);
            float rx = hero.Right + gap, rw = c.Right - rx, topH = c.Height - stripH - gap, half = (rw - gap) / 2;
            var weather = new RectangleF(rx, c.Y, half, topH);
            var next = new RectangleF(rx + half + gap, c.Y, half, topH);
            var strip = new RectangleF(rx, c.Bottom - stripH, rw, stripH);

            HeroCard(ui, hero, f);
            WeatherCard(ui, weather, f);
            NextCard(ui, next, f);
            Strip(ui, strip, f);
        }

        // ------------------------------------------------------------------ painting

        /// <summary>A rounded card filled with a diagonal gradient, a soft sheen on top and a light rim.</summary>
        void Paint(UI ui, RectangleF r, Color a, Color b, float radius)
        {
            var g = ui.G;
            using (var p = Gfx.RoundRect(r, radius))
            {
                using (var br = new LinearGradientBrush(RectangleF.Inflate(r, 2, 2), a, b, 55f)) g.FillPath(br, p);
                var st = g.Save();
                g.SetClip(p);
                using (var sheen = new LinearGradientBrush(new RectangleF(r.X, r.Y - 2, r.Width, r.Height * 0.55f + 4), Color.FromArgb(38, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                    g.FillRectangle(sheen, r.X, r.Y, r.Width, r.Height * 0.55f);
                g.Restore(st);
                using (var pen = new Pen(Color.FromArgb(46, 255, 255, 255), 1f * S)) g.DrawPath(pen, p);
            }
        }

        static Color Mix(Color a, Color b, float t)
        {
            return Color.FromArgb((int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        void Cap(UI ui, string text, RectangleF r, Brush b)
        {
            using (var cf = new Font(Fonts.UiBold, 10.5f * S, GraphicsUnit.Pixel)) ui.Text(text, r, cf, b);
        }

        // ------------------------------------------------------------------ the big card

        void HeroCard(UI ui, RectangleF r, Fonts2 f)
        {
            var now = DateTime.Now;
            var meet = CalendarFeed.Next();
            bool meetSoon = meet != null && meet.Start - now <= TimeSpan.FromMinutes(30);
            var timer = Timers.Soonest();
            if (meetSoon) MeetingHero(ui, r, meet, f);
            else if (Pomodoro.Running) PomodoroHero(ui, r, f);
            else if (timer != null) TimerHero(ui, r, timer, f);
            else if (Media.Has && Media.Playing) MediaHero(ui, r, f);
            else GreetingHero(ui, r, f);
        }

        /// <summary>Morning blue, afternoon amber, evening violet, night navy: today's date on the colour of the hour.</summary>
        void GreetingHero(UI ui, RectangleF r, Fonts2 f)
        {
            var now = DateTime.Now;
            int h = now.Hour;
            Color a, b;
            string hello;
            if (h >= 5 && h < 12) { a = Color.FromArgb(255, 72, 146, 255); b = Color.FromArgb(255, 96, 88, 230); hello = "Good morning"; }
            else if (h >= 12 && h < 17) { a = Color.FromArgb(255, 255, 152, 62); b = Color.FromArgb(255, 236, 78, 112); hello = "Good afternoon"; }
            else if (h >= 17 && h < 21) { a = Color.FromArgb(255, 150, 86, 236); b = Color.FromArgb(255, 236, 88, 128); hello = "Good evening"; }
            else { a = Color.FromArgb(255, 44, 58, 128); b = Color.FromArgb(255, 22, 24, 60); hello = "Good night"; }
            Paint(ui, r, a, b, 22 * S);
            float ip = 18 * S, x = r.X + ip, w = r.Width - 2 * ip;
            using (var soft = new SolidBrush(Color.FromArgb(215, 255, 255, 255)))
            {
                ui.Text(hello, new RectangleF(x, r.Y + 16 * S, w, 20 * S), f.Bold, soft);
                using (var huge = new Font(Fonts.UiBold, 44f * S, GraphicsUnit.Pixel))
                    ui.Text(now.ToString("dddd", CultureInfo.CurrentCulture), new RectangleF(x - 2 * S, r.Y + 36 * S, w + 4 * S, 54 * S), huge, f.White);
                ui.Text(now.ToString("d MMMM", CultureInfo.CurrentCulture), new RectangleF(x, r.Y + 90 * S, w, 24 * S), f.Title, soft);

                // what today holds, in one line, over a thin day-progress bar
                var bits = new List<string>();
                int rem = Reminders.Open().Count;
                if (rem > 0) bits.Add(rem + (rem == 1 ? " reminder" : " reminders"));
                var meet = CalendarFeed.Next();
                if (meet != null && meet.Start.Date == now.Date) bits.Add("next meeting " + meet.Start.ToString("t", CultureInfo.CurrentCulture));
                int start = Math.Max(0, Math.Min(23, Look.Int("DayStart"))), end = Math.Max(start + 1, Math.Min(24, Look.Int("DayEnd")));
                float frac = (float)Math.Max(0, Math.Min(1, (now - now.Date.AddHours(start)).TotalHours / (end - start)));
                var left = now.Date.AddHours(end) - now;
                if (frac > 0 && frac < 1) bits.Add((int)left.TotalHours + "h " + left.Minutes + "m left today");
                else if (frac >= 1) bits.Add("your day's done");
                if (bits.Count == 0) bits.Add("Nothing planned. Enjoy it.");
                using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisWord })
                    ui.G.DrawString(string.Join("  ·  ", bits.ToArray()), f.Small, soft, new RectangleF(x, r.Bottom - 52 * S, w, 32 * S), sf);
                var bar = new RectangleF(x, r.Bottom - 18 * S, w, 4 * S);
                ui.Fill(bar, Color.FromArgb(60, 255, 255, 255), 2 * S);
                if (frac > 0) ui.Fill(new RectangleF(bar.X, bar.Y, Math.Max(4 * S, bar.Width * Math.Min(1, frac)), bar.Height), Color.FromArgb(235, 255, 255, 255), 2 * S);
            }
        }

        void MeetingHero(UI ui, RectangleF r, CalendarFeed.Event e, Fonts2 f)
        {
            Paint(ui, r, Color.FromArgb(255, 255, 96, 96), Color.FromArgb(255, 176, 40, 120), 22 * S);
            float ip = 18 * S, x = r.X + ip, w = r.Width - 2 * ip;
            var now = DateTime.Now;
            using (var soft = new SolidBrush(Color.FromArgb(220, 255, 255, 255)))
            {
                Cap(ui, e.Start <= now ? "HAPPENING NOW" : "IN " + Math.Max(1, (int)Math.Ceiling((e.Start - now).TotalMinutes)) + " MIN", new RectangleF(x, r.Y + 16 * S, w, 16 * S), soft);
                using (var tf = new Font(Fonts.UiBold, 22f * S, GraphicsUnit.Pixel))
                using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisWord })
                    ui.G.DrawString(e.Title, tf, f.White, new RectangleF(x, r.Y + 36 * S, w, 60 * S), sf);
                ui.Text(e.Start.ToString("t", CultureInfo.CurrentCulture) + " – " + e.End.ToString("t", CultureInfo.CurrentCulture), new RectangleF(x, r.Y + 100 * S, w, 20 * S), f.Body, soft);
            }
            if (e.Link != null)
            {
                var jr = new RectangleF(x, r.Bottom - 50 * S, w, 34 * S);
                using (var red = new SolidBrush(Color.FromArgb(255, 200, 40, 80)))
                    Pill(ui, jr, "Join meeting", Color.FromArgb(Hover == "hjoin" ? 255 : 240, 255, 255, 255), f.Bold, red);
                ui.AddHit("hjoin", jr, () => CalendarFeed.Join(e));
            }
        }

        void PomodoroHero(UI ui, RectangleF r, Fonts2 f)
        {
            bool work = Pomodoro.Current == Pomodoro.Phase.Work;
            if (work) Paint(ui, r, Color.FromArgb(255, 255, 140, 66), Color.FromArgb(255, 196, 54, 54), 22 * S);
            else Paint(ui, r, Color.FromArgb(255, 52, 199, 140), Color.FromArgb(255, 20, 120, 120), 22 * S);
            RingHero(ui, r, f, work ? (FocusMode.On ? "FOCUS" : "WORK SESSION") : Pomodoro.Name(Pomodoro.Current).ToUpperInvariant(),
                Pomodoro.Remaining, Pomodoro.Progress, Pomodoro.Focus != null ? Pomodoro.Focus.Text : (work ? "Stay with it" : "Step away for a bit"), Pomodoro.Toggle, true);
        }

        void TimerHero(UI ui, RectangleF r, Timers.Countdown t, Fonts2 f)
        {
            Paint(ui, r, Color.FromArgb(255, 255, 184, 64), Color.FromArgb(255, 230, 110, 40), 22 * S);
            var cd = t;
            RingHero(ui, r, f, "TIMER", t.Left, t.Progress, t.Label, () => Timers.TogglePause(cd), !t.Paused);
        }

        /// <summary>A big countdown ring with the time in the middle, a caption, a line of text and a play/pause button.</summary>
        void RingHero(UI ui, RectangleF r, Fonts2 f, string caption, TimeSpan left, float progress, string line, Action toggle, bool running)
        {
            float ip = 18 * S, x = r.X + ip, w = r.Width - 2 * ip;
            using (var soft = new SolidBrush(Color.FromArgb(220, 255, 255, 255)))
            {
                Cap(ui, caption, new RectangleF(x, r.Y + 16 * S, w, 16 * S), soft);
                float d = 104 * S;
                var ring = new RectangleF(x, r.Y + 42 * S, d, d);
                using (var track = new Pen(Color.FromArgb(60, 255, 255, 255), 8 * S)) ui.G.DrawEllipse(track, ring);
                using (var arc = new Pen(Color.White, 8 * S) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    if (progress * 360 >= 1) ui.G.DrawArc(arc, ring, -90, 360 * Math.Min(1, progress));
                ui.Text(Timers.Text(left), ring, f.Big, f.White, 1);
                var btn = new RectangleF(ring.Right + 22 * S, ring.Y + d / 2 - 24 * S, 48 * S, 48 * S);
                using (var bb = new SolidBrush(Color.FromArgb(Hover == "hring" ? 255 : 235, 255, 255, 255))) ui.G.FillEllipse(bb, btn);
                using (var gf = new Font(Fonts.Icons, 16f * S, GraphicsUnit.Pixel))
                using (var dark = new SolidBrush(Color.FromArgb(230, 30, 30, 36)))
                    ui.Glyph(running ? "" : "", btn, gf, dark);
                ui.AddHit("hring", btn, toggle, true);
                ui.Text(line, new RectangleF(x, r.Bottom - 40 * S, w, 22 * S), f.Body, soft);
            }
        }

        void MediaHero(UI ui, RectangleF r, Fonts2 f)
        {
            var g = ui.G;
            using (var p = Gfx.RoundRect(r, 22 * S))
            {
                var st = g.Save();
                g.SetClip(p);
                view.Backdrop(g, r, 255); // the colours of the album art
                using (var shade = new LinearGradientBrush(RectangleF.Inflate(r, 0, 2), Color.FromArgb(30, 0, 0, 0), Color.FromArgb(150, 0, 0, 0), 90f)) g.FillRectangle(shade, r);
                g.Restore(st);
                using (var pen = new Pen(Color.FromArgb(46, 255, 255, 255), 1f * S)) g.DrawPath(pen, p);
            }
            float ip = 18 * S, x = r.X + ip, w = r.Width - 2 * ip;
            using (var soft = new SolidBrush(Color.FromArgb(220, 255, 255, 255)))
            {
                Cap(ui, "NOW PLAYING · " + Media.AppName.ToUpperInvariant(), new RectangleF(x, r.Y + 16 * S, w, 16 * S), soft);
                var art = new RectangleF(x, r.Y + 40 * S, 78 * S, 78 * S);
                view.Cover(ui, art, 12 * S);
                float tx = art.Right + 12 * S, tw = r.Right - ip - tx;
                using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisWord })
                    g.DrawString(Media.Title, f.Title, f.White, new RectangleF(tx, art.Y + 6 * S, tw, 44 * S), sf);
                ui.Text(Media.Artist, new RectangleF(tx, art.Y + 50 * S, tw, 20 * S), f.Body, soft);
            }
            view.Controls(ui, this, r.X + r.Width / 2, r.Bottom - 38 * S, 0.8f);
        }

        // ------------------------------------------------------------------ weather, painted like the sky

        void WeatherCard(UI ui, RectangleF r, Fonts2 f)
        {
            if (Weather.HasCity) Weather.Ensure(); // fetches (at most every 20 minutes) once a city is set
            var w = Weather.Now;
            float ip = 14 * S, x = r.X + ip, cw = r.Width - 2 * ip;
            if (w == null)
            {
                Paint(ui, r, Color.FromArgb(255, 60, 80, 120), Color.FromArgb(255, 30, 36, 60), 20 * S);
                using (var soft = new SolidBrush(Color.FromArgb(210, 255, 255, 255)))
                {
                    if (Weather.HasCity && !Weather.Failed) { Cap(ui, Weather.Place.Split(',')[0].ToUpperInvariant(), new RectangleF(x, r.Y + 14 * S, cw, 16 * S), soft); ui.Text("Loading…", new RectangleF(x, r.Y + 40 * S, cw, 20 * S), f.Body, soft); return; }
                    Cap(ui, "WEATHER", new RectangleF(x, r.Y + 14 * S, cw, 16 * S), soft);
                    using (var sf = new StringFormat())
                        ui.G.DrawString(Weather.Loading ? "Loading…" : "Set your city in Harbor → General → Island", f.Small, soft, new RectangleF(x, r.Y + 40 * S, cw, 60 * S), sf);
                }
                var set = new RectangleF(r.X, r.Y, r.Width, r.Height);
                if (!Weather.Loading) ui.AddHit("wset", set, () => { CloseFlyout(); SettingsWindow.ShowWindow(); });
                return;
            }
            Color a, b;
            Sky(w.Code, w.Day, out a, out b);
            Paint(ui, r, a, b, 20 * S);
            var gst = ui.G.Save();
            if (w.Code >= 51 && w.Code <= 67 || w.Code >= 80 && w.Code <= 82 || w.Code >= 95) Rain(ui, r);
            ui.G.Restore(gst);
            using (var soft = new SolidBrush(Color.FromArgb(215, 255, 255, 255)))
            {
                Cap(ui, Weather.Place.Split(',')[0].ToUpperInvariant(), new RectangleF(x, r.Y + 14 * S, cw, 16 * S), soft);
                using (var big = new Font(Fonts.UiBold, 34f * S, GraphicsUnit.Pixel))
                    ui.Text(Math.Round(w.Temp) + "°", new RectangleF(x - 1 * S, r.Y + 32 * S, cw, 42 * S), big, f.White);
                WeatherIcons.Draw(ui.G, new RectangleF(r.Right - ip - 40 * S, r.Y + 34 * S, 40 * S, 40 * S), w.Code, w.Day);
                ui.Text(Weather.Describe(w.Code), new RectangleF(x, r.Y + 78 * S, cw, 18 * S), f.Body, f.White);
                ui.Text("H " + Math.Round(w.High) + "°  ·  L " + Math.Round(w.Low) + "°", new RectangleF(x, r.Bottom - 26 * S, cw, 16 * S), f.Small, soft);
            }
            ui.AddHit("wgo", r, () => SetTab("weather", true), true);
        }

        /// <summary>Two colours for the sky: clear blue by day, navy at night, grey clouds, blue-grey rain, pale snow, purple storms.</summary>
        static void Sky(int code, bool day, out Color a, out Color b)
        {
            if (code >= 95) { a = Color.FromArgb(255, 88, 70, 140); b = Color.FromArgb(255, 36, 28, 70); return; }
            if (code >= 71 && code <= 77 || code == 85 || code == 86) { a = Color.FromArgb(255, 150, 178, 220); b = Color.FromArgb(255, 88, 112, 160); return; }
            if (code >= 51 && code <= 67 || code >= 80 && code <= 82) { a = day ? Color.FromArgb(255, 84, 112, 156) : Color.FromArgb(255, 44, 56, 90); b = day ? Color.FromArgb(255, 44, 62, 98) : Color.FromArgb(255, 20, 26, 48); return; }
            if (code >= 2) { a = day ? Color.FromArgb(255, 118, 136, 166) : Color.FromArgb(255, 50, 60, 88); b = day ? Color.FromArgb(255, 70, 84, 112) : Color.FromArgb(255, 24, 28, 46); return; }
            if (!day) { a = Color.FromArgb(255, 36, 52, 120); b = Color.FromArgb(255, 12, 16, 48); return; }
            a = Color.FromArgb(255, 70, 160, 255); b = Color.FromArgb(255, 26, 96, 214);
        }

        /// <summary>Faint diagonal rain streaks over the weather card.</summary>
        void Rain(UI ui, RectangleF r)
        {
            using (var p = Gfx.RoundRect(r, 20 * S)) ui.G.SetClip(p);
            using (var pen = new Pen(Color.FromArgb(40, 255, 255, 255), 1.2f * S))
                for (int i = 0; i < 16; i++)
                {
                    float x = r.X + (i * 37 % 100) / 100f * r.Width, y = r.Y + (i * 53 % 100) / 100f * r.Height;
                    ui.G.DrawLine(pen, x, y, x - 4 * S, y + 10 * S);
                }
        }

        // ------------------------------------------------------------------ up next: meeting, timers, reminders

        void NextCard(UI ui, RectangleF r, Fonts2 f)
        {
            Paint(ui, r, Color.FromArgb(255, 42, 42, 52), Color.FromArgb(255, 22, 22, 28), 20 * S);
            float ip = 14 * S, x = r.X + ip, cw = r.Width - 2 * ip;
            var now = DateTime.Now;
            var rows = new List<KeyValuePair<Color, string[]>>(); // dot colour, { text, right }
            var meet = CalendarFeed.Next();
            if (meet != null && meet.Start.Date == now.Date) rows.Add(new KeyValuePair<Color, string[]>(Color.FromArgb(255, 255, 99, 99), new[] { meet.Title, MeetingWhen(meet) }));
            foreach (var t in Timers.Running) rows.Add(new KeyValuePair<Color, string[]>(Color.FromArgb(255, 255, 179, 64), new[] { t.Label, Timers.Text(t.Left) }));
            foreach (var it in Reminders.Open()) rows.Add(new KeyValuePair<Color, string[]>(Reminders.Overdue(it) ? Color.FromArgb(255, 255, 105, 97) : Color.FromArgb(255, 100, 180, 255), new[] { it.Text, Reminders.DueText(it) ?? "" }));
            Cap(ui, "UP NEXT", new RectangleF(x, r.Y + 14 * S, cw, 16 * S), f.Faint);
            if (rows.Count > 0)
            {
                string n = rows.Count.ToString(CultureInfo.InvariantCulture);
                float bw = Math.Max(18 * S, ui.G.MeasureString(n, f.Small).Width + 8 * S);
                using (var badge = new SolidBrush(Color.FromArgb(255, 10, 132, 255))) using (var bp = Gfx.RoundRect(new RectangleF(r.Right - ip - bw, r.Y + 12 * S, bw, 18 * S), 9 * S)) ui.G.FillPath(badge, bp);
                ui.Text(n, new RectangleF(r.Right - ip - bw, r.Y + 12 * S, bw, 18 * S), f.Small, f.White, 1);
            }
            if (rows.Count == 0)
            {
                using (var gf = new Font(Fonts.Icons, 20f * S, GraphicsUnit.Pixel))
                using (var green = new SolidBrush(Color.FromArgb(255, 52, 209, 110)))
                    ui.Glyph("", new RectangleF(x, r.Y + 38 * S, 26 * S, 28 * S), gf, green);
                ui.Text("All clear", new RectangleF(x + 30 * S, r.Y + 38 * S, cw - 30 * S, 28 * S), f.Title, f.White);
                ui.Text("Nothing waiting on you", new RectangleF(x, r.Y + 70 * S, cw, 18 * S), f.Small, f.Dim);
            }
            float y = r.Y + 38 * S;
            for (int i = 0; i < rows.Count && i < 3; i++)
            {
                using (var dot = new SolidBrush(rows[i].Key)) ui.G.FillEllipse(dot, x, y + 7 * S, 7 * S, 7 * S);
                string right = rows[i].Value[1];
                float rwid = right != "" ? ui.G.MeasureString(right, f.Small).Width + 2 * S : 0;
                ui.Text(rows[i].Value[0], new RectangleF(x + 14 * S, y, cw - 14 * S - rwid, 21 * S), f.Body, f.White);
                if (right != "") ui.Text(right, new RectangleF(x, y, cw, 21 * S), f.Small, f.Faint, 2);
                y += 25 * S;
            }
            var add = new RectangleF(x - 6 * S, r.Bottom - 30 * S, 110 * S, 22 * S);
            if (Hover == "dadd") ui.Fill(add, Color.FromArgb(22, 255, 255, 255), 11 * S);
            ui.Text(rows.Count > 3 ? "+" + (rows.Count - 3) + " more" : "+  Add reminder", new RectangleF(add.X + 6 * S, add.Y, add.Width, add.Height), f.Small, Hover == "dadd" ? f.White : f.Faint);
            ui.AddHit("dadd", add, () => { SetTab("reminders", false); FocusField = "rem"; Activate(); N.SetForegroundWindow(Handle); }, true);
        }

        // ------------------------------------------------------------------ apps and quick toggles

        void Strip(UI ui, RectangleF r, Fonts2 f)
        {
            Paint(ui, r, Color.FromArgb(255, 36, 36, 44), Color.FromArgb(255, 22, 22, 28), r.Height / 2);
            float ip = 14 * S;
            var apps = new List<string>();
            foreach (var a in ScreenTime.Today()) if (a.Icon != null && apps.Count < 4) apps.Add(a.Icon);
            foreach (var p in Settings.Pinned) if (apps.Count < 4 && !apps.Contains(p)) apps.Add(p);
            float isz = 24 * S, ay = r.Y + (r.Height - isz) / 2;
            for (int i = 0; i < apps.Count; i++)
            {
                var ar = new RectangleF(r.X + ip + i * (isz + 8 * S), ay, isz, isz);
                string id = "la" + i, path = apps[i];
                if (Hover == id) ui.Fill(RectangleF.Inflate(ar, 4 * S, 4 * S), Color.FromArgb(34, 255, 255, 255), 9 * S);
                var ic = IconFor(path, 26);
                if (ic != null) ui.G.DrawImage(ic, ar);
                ui.AddHit(id, RectangleF.Inflate(ar, 4 * S, 4 * S), () => Launcher.Open(path));
            }

            // each toggle has its own colour when it's on
            var ids = new List<string>(); var glyphs = new List<string>(); var ons = new List<bool>(); var acts = new List<Action>(); var tints = new List<Color>();
            ids.Add("qfocus"); glyphs.Add(""); ons.Add(FocusMode.On); acts.Add(FocusMode.Toggle); tints.Add(Color.FromArgb(255, 94, 92, 230));
            ids.Add("qdark"); glyphs.Add(""); ons.Add(Theme.UserDark); acts.Add(ControlCenter.ToggleDarkMode); tints.Add(Color.FromArgb(255, 88, 86, 214));
            if (Radios.HasWifi) { ids.Add("qwifi"); glyphs.Add(""); ons.Add(Radios.WifiOn); acts.Add(() => Radios.SetWifi(!Radios.WifiOn)); tints.Add(Color.FromArgb(255, 10, 132, 255)); }
            if (Radios.HasBt) { ids.Add("qbt"); glyphs.Add(""); ons.Add(Radios.BtOn); acts.Add(() => Radios.SetBluetooth(!Radios.BtOn)); tints.Add(Color.FromArgb(255, 10, 132, 255)); }
            ids.Add("qawake"); glyphs.Add(""); ons.Add(keepAwake); acts.Add(ToggleKeepAwake); tints.Add(Color.FromArgb(255, 255, 159, 10));
            ids.Add("qlock"); glyphs.Add(""); ons.Add(false); acts.Add(() => N.LockWorkStation()); tints.Add(Color.White);
            float tsz = 26 * S, tgap = 5 * S, tx = r.Right - ip - ids.Count * tsz - (ids.Count - 1) * tgap;
            using (var gf = new Font(Fonts.Icons, 12f * S, GraphicsUnit.Pixel))
                for (int i = 0; i < ids.Count; i++)
                {
                    var tr = new RectangleF(tx + i * (tsz + tgap), r.Y + (r.Height - tsz) / 2, tsz, tsz);
                    bool hot = Hover == ids[i];
                    Color bg = ons[i] ? (hot ? Mix(tints[i], Color.White, 0.15f) : tints[i]) : Color.FromArgb(hot ? 60 : 32, 255, 255, 255);
                    using (var bb = new SolidBrush(bg)) ui.G.FillEllipse(bb, tr);
                    ui.Glyph(glyphs[i], tr, gf, ons[i] || hot ? f.White : f.Dim);
                    var act = acts[i];
                    ui.AddHit(ids[i], tr, () => act(), true);
                }
        }
    }
}
