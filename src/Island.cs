using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace Harbor
{
    /// <summary>
    /// The island: rest the mouse on the centre of the menu bar and a dark panel drops down with tabs for
    /// Media (with lyrics), Pomodoro, Weather and Screen Time. It never takes keyboard focus, and it closes
    /// when the mouse leaves. It redraws only while something on it moves.
    /// </summary>
    sealed class Island : Flyout
    {
        static Island inst;

        static readonly string[] tabs = { "dashboard", "media", "pomodoro", "weather", "screentime", "reminders", "ai" };
        static readonly string[] tabNames = { "Dashboard", "Media", "Pomodoro", "Weather", "Screen Time", "Reminders", "AI Coding" };
        static readonly string[] tabGlyphs = { "", "", "", null, "", "", "" }; // weather draws its own icon

        string tab;
        RectangleF trigger;                 // the menu-bar area that opened us (screen coordinates)
        readonly NowPlayingView view = new NowPlayingView();
        readonly Timer tick = new Timer { Interval = 33 };
        readonly Timer watch = new Timer { Interval = 100 };
        int lastFrame, outsideMs;
        readonly Dictionary<string, Bitmap> icons = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);

        public static bool IsOpen { get { return inst != null && inst.Visible; } }
        public static bool ShowingMedia { get { return IsOpen && inst.tab == "media"; } }

        Island()
        {
            NoActivate = true;
            LogicalWidth = 680;
            FixedLogicalHeight = 272;
            tab = Array.IndexOf(tabs, Look.Get("IslandTab")) >= 0 ? Look.Get("IslandTab") : "media";
            Media.Changed += delegate { if (Visible && tab == "media") Refresh2(); };
            Lyrics.Changed += delegate { view.Snap(); if (Visible && tab == "media") Refresh2(); };
            Pomodoro.Changed += delegate { if (Visible && tab == "pomodoro") Refresh2(); };
            Weather.Changed += delegate { if (Visible && (tab == "weather" || tab == "dashboard")) Refresh2(); };
            Reminders.Changed += delegate { if (Visible) Refresh2(); };
            ClaudeBridge.Changed += delegate { if (Visible) Refresh2(); };
            ClaudeHistory.Changed += delegate { if (Visible && tab == "ai") Refresh2(); };
            tick.Tick += delegate { Tick(); };
            watch.Tick += delegate { Watch(); };
        }

        /// <summary>--snapshot: one island per tab, to render without opening.</summary>
        public static Island ForSnapshot(string t)
        {
            var i = new Island();
            i.tab = t;
            return i;
        }

        /// <summary>Opens below the centre of the menu bar (or switches tab if it's already open there).</summary>
        public static void Open(TopBar bar, RectangleF zone, string startTab)
        {
            if (inst == null) inst = new Island();
            if (startTab != null) inst.SetTab(startTab, false);
            var sb = bar.ScreenBounds;
            inst.trigger = new RectangleF(sb.X + zone.X, sb.Y, zone.Width, sb.Height + 2);
            if (inst.Visible) { inst.Refresh2(); return; }
            var center = new RectangleF(sb.Width / 2f - 1, 0, 2, sb.Height);
            inst.ShowBelow(bar, center, "island", 1);
        }

        void SetTab(string t, bool remember)
        {
            if (Array.IndexOf(tabs, t) < 0 || t == tab) return;
            tab = t;
            Media.WantTimeline = t == "media";
            if (t == "media") { Media.RefreshTimeline(); view.Snap(); }
            if (remember) { Look.Set("IslandTab", t); Settings.Save(); }
        }

        protected override void OnOpened()
        {
            outsideMs = 0;
            Media.WantTimeline = tab == "media";
            if (tab == "media") Media.RefreshTimeline();
            view.Snap();
            tick.Start();
            watch.Start();
        }

        protected override void OnClosedFlyout()
        {
            tick.Stop();
            watch.Stop();
            Media.WantTimeline = false;
            foreach (var b in icons.Values) if (b != null) b.Dispose();
            icons.Clear();
        }

        /// <summary>Close once the mouse has been away from both the island and the menu-bar centre for a moment.</summary>
        void Watch()
        {
            if (!Visible) { watch.Stop(); return; }
            var p = Cursor.Position;
            var me = new RectangleF(Left, Top - 8, Width, Height + 8);
            // stay while you're over it, typing a reminder, or Claude is waiting for your answer
            if (me.Contains(p) || RectangleF.Inflate(trigger, 6, 0).Contains(p) || Dragging != null || FocusField != null || ClaudeBridge.Pending.Count > 0) { outsideMs = 0; return; }
            outsideMs += watch.Interval;
            if (outsideMs >= 400) CloseFlyout();
        }

        /// <summary>Redraw only what moves: lyrics at 30 fps while gliding, the song and timer a few times a second.</summary>
        void Tick()
        {
            if (!Visible) { tick.Stop(); return; }
            int now = Environment.TickCount, age = now - lastFrame;
            bool need = Dragging != null
                || (tab == "media" && (view.Animating || (Media.Playing && age > 250)))
                || ((tab == "pomodoro" || tab == "dashboard" || tab == "ai") && age > 1000)
                || (tab == "screentime" && age > 15000);
            if (need) { lastFrame = now; Refresh2(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            view.MouseUp(this);
            base.OnMouseUp(e);
        }

        /// <summary>Clicking a text field is the one time the island takes the keyboard (it never does on hover).</summary>
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (FocusField != null) { Activate(); N.SetForegroundWindow(Handle); }
        }

        /// <summary>Claude Code is asking permission: pop the island up on the AI tab of the main screen.</summary>
        public static void Attention()
        {
            var d = App.PrimaryDisplay;
            if (d == null || d.Bar == null || !d.Bar.IsHandleCreated) return;
            var sb = d.Bar.ScreenBounds;
            Open(d.Bar, new RectangleF(sb.Width / 2f - 120, 0, 240, sb.Height), "ai");
        }

        // ------------------------------------------------------------------ layout

        const float Pad = 22, TabBarH = 50;

        protected override void Build(UI ui)
        {
            float W = ui.W, H = FixedLogicalHeight * S;
            var all = new RectangleF(0, 0, W, H);
            if (tab == "media" && Media.Has) view.Backdrop(ui.G, all);
            else
                using (var br = new LinearGradientBrush(all, Color.FromArgb(250, 30, 30, 34), Color.FromArgb(250, 12, 12, 14), 90f))
                    ui.G.FillRectangle(br, all);

            var c = new RectangleF(Pad * S, 18 * S, W - 2 * Pad * S, H - 18 * S - TabBarH * S);
            using (var f = new Fonts2(S))
            {
                switch (tab)
                {
                    case "dashboard": DashboardTab(ui, c, f); break;
                    case "reminders": RemindersTab(ui, c, f); break;
                    case "ai": AiTab(ui, c, f); break;
                    case "media": MediaTab(ui, c, f); break;
                    case "pomodoro": PomodoroTab(ui, c, f); break;
                    case "weather": WeatherTab(ui, c, f); break;
                    case "screentime": ScreenTimeTab(ui, c, f); break;
                }
                TabBar(ui, new RectangleF(0, H - TabBarH * S, W, TabBarH * S), f);
            }
            ui.Y = H;
        }

        /// <summary>White-on-dark text kit for the island (it's always dark, like a notch).</summary>
        sealed class Fonts2 : IDisposable
        {
            public readonly Font Small, Body, Bold, Title, Big, Huge;
            public readonly SolidBrush White, Dim, Faint;
            public Fonts2(float s)
            {
                Small = new Font(Fonts.Ui, 11.5f * s, GraphicsUnit.Pixel);
                Body = new Font(Fonts.Ui, 13.5f * s, GraphicsUnit.Pixel);
                Bold = new Font(Fonts.UiBold, 13.5f * s, GraphicsUnit.Pixel);
                Title = new Font(Fonts.UiBold, 16f * s, GraphicsUnit.Pixel);
                Big = new Font(Fonts.UiBold, 26f * s, GraphicsUnit.Pixel);
                Huge = new Font(Fonts.UiBold, 42f * s, GraphicsUnit.Pixel);
                White = new SolidBrush(NowPlayingView.White);
                Dim = new SolidBrush(NowPlayingView.Dim);
                Faint = new SolidBrush(NowPlayingView.Faint);
            }
            public void Dispose() { Small.Dispose(); Body.Dispose(); Bold.Dispose(); Title.Dispose(); Big.Dispose(); Huge.Dispose(); White.Dispose(); Dim.Dispose(); Faint.Dispose(); }
        }

        void TabBar(UI ui, RectangleF r, Fonts2 f)
        {
            // a pill per tab; the selected one also shows its name
            float h = 30 * S, gap = 6 * S, icon = 34 * S;
            var widths = new float[tabs.Length];
            float total = 0;
            for (int i = 0; i < tabs.Length; i++)
            {
                widths[i] = tabs[i] == tab ? icon + ui.G.MeasureString(tabNames[i], f.Bold).Width + 6 * S : icon;
                total += widths[i] + (i > 0 ? gap : 0);
            }
            float x = r.X + (r.Width - total) / 2, y = r.Y + (r.Height - h) / 2 - 4 * S;
            using (var gf = new Font(Fonts.Icons, 14f * S, GraphicsUnit.Pixel))
                for (int i = 0; i < tabs.Length; i++)
                {
                    var pr = new RectangleF(x, y, widths[i], h);
                    bool sel = tabs[i] == tab;
                    string id = "tab:" + tabs[i];
                    if (sel || Hover == id) ui.Fill(pr, Color.FromArgb(sel ? 46 : 24, 255, 255, 255), h / 2);
                    var ir = new RectangleF(pr.X, pr.Y, icon, h);
                    if (tabGlyphs[i] != null) ui.Glyph(tabGlyphs[i], ir, gf, sel ? f.White : f.Dim);
                    else WeatherIcons.Draw(ui.G, RectangleF.Inflate(ir, -8 * S, -6 * S), Weather.Now != null ? Weather.Now.Code : 2, Weather.Now == null || Weather.Now.Day);
                    if (sel) ui.Text(tabNames[i], new RectangleF(pr.X + icon - 4 * S, pr.Y, pr.Width - icon, h), f.Bold, f.White);
                    string t = tabs[i];
                    ui.AddHit(id, pr, () => SetTab(t, true), true);
                    x += widths[i] + gap;
                }
        }

        static string Span(TimeSpan t)
        {
            if (t.TotalMinutes < 1) return "<1m";
            return t.TotalHours >= 1 ? (int)t.TotalHours + "h " + t.Minutes + "m" : (int)t.TotalMinutes + "m";
        }

        void Card(UI ui, RectangleF r) { ui.Fill(r, Color.FromArgb(18, 255, 255, 255), 14 * S); }

        // ------------------------------------------------------------------ media

        void MediaTab(UI ui, RectangleF c, Fonts2 f)
        {
            view.NoteSong();
            if (!Media.Has)
            {
                ui.Text("Nothing playing", new RectangleF(c.X, c.Y + c.Height / 2 - 26 * S, c.Width, 24 * S), f.Title, f.White, 1);
                ui.Text("Play something in Spotify, YouTube or any media app", new RectangleF(c.X, c.Y + c.Height / 2, c.Width, 20 * S), f.Body, f.Dim, 1);
                return;
            }
            float lw = 300 * S;
            var art = new RectangleF(c.X, c.Y, 88 * S, 88 * S);
            view.Cover(ui, art, 12 * S);
            float tx = art.Right + 14 * S, tw = c.X + lw - tx;
            ui.Text(Media.Title, new RectangleF(tx, art.Y + 12 * S, tw, 22 * S), f.Title, f.White);
            ui.Text(Media.Artist, new RectangleF(tx, art.Y + 36 * S, tw, 20 * S), f.Body, f.Dim);
            ui.Text(Media.AppName, new RectangleF(tx, art.Bottom - 22 * S, tw, 18 * S), f.Small, f.Faint);
            float y = art.Bottom + 14 * S;
            if (Media.Duration > TimeSpan.Zero) { view.Progress(ui, this, c.X, y, lw, f.Small); y += 34 * S; }
            view.Controls(ui, this, c.X + lw / 2, y + 22 * S, 0.82f);

            float rx = c.X + lw + 30 * S, rw = c.Right - rx;
            ui.Text("LYRICS", new RectangleF(rx, c.Y, 100 * S, 18 * S), f.Small, f.Faint);
            view.LyricsSwitch(ui, this, new RectangleF(c.Right - 58 * S, c.Y - 2 * S, 58 * S, 22 * S), f.Small);
            var box = new RectangleF(rx - 8 * S, c.Y + 26 * S, rw + 8 * S, c.Bottom - c.Y - 26 * S);
            if (!view.Lyrics(ui, this, box, 18f)) ui.Text("Lyrics are off", box, f.Body, f.Faint, 1);
        }

        // ------------------------------------------------------------------ pomodoro

        static readonly Color Ember = Color.FromArgb(255, 232, 160, 96);

        void PomodoroTab(UI ui, RectangleF c, Fonts2 f)
        {
            // ring timer
            float d = 128 * S, cx = c.X + 90 * S;
            var ring = new RectangleF(cx - d / 2, c.Y + 2 * S, d, d);
            using (var track = new Pen(Color.FromArgb(38, 255, 255, 255), 7 * S)) ui.G.DrawEllipse(track, ring);
            float p = Pomodoro.Progress;
            if (p > 0)
                using (var arc = new Pen(Pomodoro.Current == Pomodoro.Phase.Work ? Ember : Color.FromArgb(255, 100, 200, 160), 7 * S) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    ui.G.DrawArc(arc, ring, -90, 360 * p);
            var rem = Pomodoro.Remaining;
            ui.Text(((int)rem.TotalMinutes).ToString("00") + ":" + rem.Seconds.ToString("00"), new RectangleF(ring.X, ring.Y + d / 2 - 22 * S, d, 34 * S), f.Big, f.White, 1);
            ui.Text(Pomodoro.Running ? Pomodoro.Name(Pomodoro.Current) : (p > 0 ? "Paused" : "Ready to focus"), new RectangleF(ring.X, ring.Y + d / 2 + 12 * S, d, 18 * S), f.Small, f.Dim, 1);

            float by = ring.Bottom + 30 * S;
            using (var gf = new Font(Fonts.Icons, 14f * S, GraphicsUnit.Pixel))
            using (var big = new Font(Fonts.Icons, 16f * S, GraphicsUnit.Pixel))
            using (var dark = new SolidBrush(Color.FromArgb(235, 18, 18, 22)))
            {
                RoundBtn(ui, "preset", new PointF(cx - 52 * S, by), 17 * S, "", Pomodoro.Reset, gf, f.White, false);
                RoundBtn(ui, "ptoggle", new PointF(cx, by), 21 * S, Pomodoro.Running ? "" : "", Pomodoro.Toggle, big, dark, true);
                RoundBtn(ui, "pskip", new PointF(cx + 52 * S, by), 17 * S, "", Pomodoro.Skip, gf, f.White, false);
            }

            // phases + today's sessions
            float px = c.X + 200 * S, pw = 196 * S;
            int done = Pomodoro.SessionsToday;
            ui.Text(done == 0 ? "No sessions yet today" : done + (done == 1 ? " session" : " sessions") + " today", new RectangleF(px, c.Y, pw, 20 * S), f.Bold, f.White);
            for (int i = 0; i < 4; i++)
                using (var b = new SolidBrush(i < done % 4 || (done > 0 && done % 4 == 0) ? Ember : Color.FromArgb(50, 255, 255, 255)))
                    ui.G.FillEllipse(b, px + i * 13 * S, c.Y + 26 * S, 8 * S, 8 * S);
            float ry = c.Y + 46 * S;
            foreach (Pomodoro.Phase ph in Enum.GetValues(typeof(Pomodoro.Phase)))
            {
                var row = new RectangleF(px - 8 * S, ry, pw + 8 * S, 36 * S);
                string id = "ph" + (int)ph;
                bool sel = Pomodoro.Current == ph;
                if (sel || Hover == id) ui.Fill(row, Color.FromArgb(sel ? 34 : 18, 255, 255, 255), 10 * S);
                using (var dot = new SolidBrush(ph == Pomodoro.Phase.Work ? Ember : Color.FromArgb(255, 100, 200, 160)))
                    ui.G.FillEllipse(dot, row.X + 12 * S, row.Y + row.Height / 2 - 4 * S, 8 * S, 8 * S);
                ui.Text(Pomodoro.Name(ph), new RectangleF(row.X + 30 * S, row.Y, row.Width - 80 * S, row.Height), sel ? f.Bold : f.Body, sel ? f.White : f.Dim);
                ui.Text((int)Pomodoro.Length(ph).TotalMinutes + "m", new RectangleF(row.X, row.Y, row.Width - 12 * S, row.Height), f.Body, f.Dim, 2);
                var pp = ph;
                ui.AddHit(id, row, () => Pomodoro.Select(pp), true);
                ry += 40 * S;
            }

            // focus target: which reminder this session is for (click to step through them)
            var fr = new RectangleF(px - 8 * S, ry + 2 * S, pw + 8 * S, 30 * S);
            if (Hover == "pfocus") ui.Fill(fr, Color.FromArgb(18, 255, 255, 255), 10 * S);
            var focus = Pomodoro.Focus;
            using (var tf = new Font(Fonts.Icons, 12f * S, GraphicsUnit.Pixel)) ui.Glyph("", new RectangleF(fr.X + 6 * S, fr.Y, 20 * S, fr.Height), tf, focus != null ? f.White : f.Faint);
            ui.Text(focus != null ? focus.Text : (Reminders.Open().Count > 0 ? "No focus target (click to pick)" : "No focus target"), new RectangleF(fr.X + 30 * S, fr.Y, fr.Width - 36 * S, fr.Height), f.Small, focus != null ? f.White : f.Faint);
            ui.AddHit("pfocus", fr, Pomodoro.NextFocus, true);

            // day progress
            var card = new RectangleF(c.X + 420 * S, c.Y, c.Right - c.X - 420 * S, c.Height - 4 * S);
            Card(ui, card);
            float ix = card.X + 16 * S, iw = card.Width - 32 * S;
            int start = Math.Max(0, Math.Min(23, Look.Int("DayStart"))), end = Math.Max(start + 1, Math.Min(24, Look.Int("DayEnd")));
            var now = DateTime.Now;
            DateTime from = now.Date.AddHours(start), to = now.Date.AddHours(end);
            ui.Text("Day progress", new RectangleF(ix, card.Y + 12 * S, iw, 20 * S), f.Bold, f.White);
            ui.Text(start + ":00 – " + end + ":00", new RectangleF(ix, card.Y + 12 * S, iw, 20 * S), f.Small, f.Faint, 2);
            string head, sub;
            float frac;
            if (now < from) { head = Span(from - now); sub = "until your day starts"; frac = 0; }
            else if (now >= to) { head = "Done"; sub = "for today. See you tomorrow"; frac = 1; }
            else { head = Span(to - now); sub = "left in your day"; frac = (float)((now - from).TotalSeconds / (to - from).TotalSeconds); }
            ui.Text(head, new RectangleF(ix, card.Y + 44 * S, iw, 34 * S), f.Big, f.White);
            ui.Text(sub, new RectangleF(ix, card.Y + 78 * S, iw, 18 * S), f.Small, f.Dim);
            var bar = new RectangleF(ix, card.Y + 108 * S, iw, 6 * S);
            ui.Fill(bar, Color.FromArgb(40, 255, 255, 255), 3 * S);
            ui.Fill(new RectangleF(bar.X, bar.Y, Math.Max(6 * S, bar.Width * frac), bar.Height), Ember, 3 * S);
            ui.Text((int)Math.Round(frac * 100) + "% of your day", new RectangleF(ix, bar.Bottom + 6 * S, iw, 18 * S), f.Small, f.Faint);
            ui.Text("Focus today: " + Span(TimeSpan.FromMinutes(done * Pomodoro.Length(Pomodoro.Phase.Work).TotalMinutes)), new RectangleF(ix, card.Bottom - 30 * S, iw, 18 * S), f.Small, f.Dim);
        }

        void RoundBtn(UI ui, string id, PointF c, float r, string glyph, Action act, Font font, Brush b, bool filled)
        {
            var rr = new RectangleF(c.X - r, c.Y - r, r * 2, r * 2);
            if (filled) using (var fb = new SolidBrush(Color.FromArgb(Hover == id ? 255 : 238, 255, 255, 255))) ui.G.FillEllipse(fb, rr);
            else using (var hb = new SolidBrush(Color.FromArgb(Hover == id ? 50 : 26, 255, 255, 255))) ui.G.FillEllipse(hb, rr);
            ui.Glyph(glyph, rr, font, b);
            ui.AddHit(id, rr, act, true);
        }

        // ------------------------------------------------------------------ weather

        void WeatherTab(UI ui, RectangleF c, Fonts2 f)
        {
            if (!Weather.HasCity)
            {
                WeatherIcons.Draw(ui.G, new RectangleF(c.X + c.Width / 2 - 28 * S, c.Y + 16 * S, 56 * S, 56 * S), 2, true);
                ui.Text("Add your city to see the weather", new RectangleF(c.X, c.Y + 82 * S, c.Width, 22 * S), f.Title, f.White, 1);
                ui.Text("Harbor → General → Weather. Forecasts come from Open-Meteo (free, no account).", new RectangleF(c.X, c.Y + 106 * S, c.Width, 18 * S), f.Small, f.Dim, 1);
                var btn = new RectangleF(c.X + c.Width / 2 - 80 * S, c.Y + 136 * S, 160 * S, 30 * S);
                ui.Fill(btn, Color.FromArgb(Hover == "wset" ? 255 : 235, 255, 255, 255), 15 * S);
                using (var db = new SolidBrush(Color.FromArgb(235, 18, 18, 22))) ui.Text("Open Harbor settings", btn, f.Bold, db, 1);
                ui.AddHit("wset", btn, () => { CloseFlyout(); SettingsWindow.ShowWindow(); });
                return;
            }
            Weather.Ensure();
            var w = Weather.Now;
            if (w == null)
            {
                ui.Text(Weather.Loading || !Weather.Failed ? "Loading the weather for " + Weather.Place + "…" : Weather.Error ?? "Couldn't load the weather",
                    new RectangleF(c.X, c.Y + c.Height / 2 - 12 * S, c.Width, 24 * S), f.Body, f.Dim, 1);
                return;
            }
            string deg = "°";
            WeatherIcons.Draw(ui.G, new RectangleF(c.X - 4 * S, c.Y - 2 * S, 68 * S, 68 * S), w.Code, w.Day);
            float tx = c.X + 74 * S;
            ui.Text(Math.Round(w.Temp) + deg + (Weather.Fahrenheit ? "F" : "C"), new RectangleF(tx, c.Y - 6 * S, 220 * S, 52 * S), f.Huge, f.White);
            ui.Text(Weather.Describe(w.Code), new RectangleF(tx, c.Y + 44 * S, 220 * S, 18 * S), f.Body, f.Dim);
            ui.Text(Weather.Place + "   H " + Math.Round(w.High) + deg + "  L " + Math.Round(w.Low) + deg, new RectangleF(tx, c.Y + 62 * S, 320 * S, 16 * S), f.Small, f.Faint);

            var culture = CultureInfo.CurrentCulture;
            string fmt = culture.DateTimeFormat.ShortTimePattern.Contains("H") ? "HH:mm" : "h:mm tt";
            ui.Text("Sunrise " + w.Sunrise.ToString(fmt, culture) + "   Sunset " + w.Sunset.ToString(fmt, culture), new RectangleF(c.X, c.Y, c.Width, 16 * S), f.Small, f.Faint, 2);
            ui.Text("Feels like", new RectangleF(c.X, c.Y + 26 * S, c.Width, 16 * S), f.Small, f.Dim, 2);
            ui.Text(Math.Round(w.Feels) + deg, new RectangleF(c.X, c.Y + 42 * S, c.Width, 30 * S), f.Big, f.White, 2);

            // stats
            string[] labels = { "Humidity", "Wind", "UV", "Rain chance", "Pressure" };
            string[] values = { w.Humidity + "%", Math.Round(w.Wind) + (Weather.Fahrenheit ? " mph" : " km/h"), Math.Round(w.Uv).ToString(), w.Rain + "%", Math.Round(w.Pressure) + " hPa" };
            float sy = c.Y + 88 * S, cw = c.Width / labels.Length;
            for (int i = 0; i < labels.Length; i++)
            {
                ui.Text(labels[i], new RectangleF(c.X + i * cw, sy, cw, 16 * S), f.Small, f.Faint);
                ui.Text(values[i], new RectangleF(c.X + i * cw, sy + 16 * S, cw, 20 * S), f.Bold, f.White);
            }

            // next 12 hours
            float hy = c.Y + 132 * S;
            int n = Math.Min(12, w.Hours.Count);
            float hw = c.Width / Math.Max(1, n);
            for (int i = 0; i < n; i++)
            {
                var h = w.Hours[i];
                float hx = c.X + i * hw;
                ui.Text(i == 0 ? "Now" : h.At.ToString(fmt == "HH:mm" ? "HH" : "h tt", culture), new RectangleF(hx, hy, hw, 16 * S), f.Small, f.Faint, 1);
                WeatherIcons.Draw(ui.G, new RectangleF(hx + hw / 2 - 12 * S, hy + 19 * S, 24 * S, 24 * S), h.Code, h.Day);
                ui.Text(Math.Round(h.Temp) + deg, new RectangleF(hx, hy + 46 * S, hw, 18 * S), f.Bold, f.White, 1);
            }
        }

        // ------------------------------------------------------------------ screen time

        Bitmap IconFor(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            Bitmap b;
            if (icons.TryGetValue(path, out b)) return b;
            var arr = ShellIcons.LoadIcons(path, (int)Math.Round(20 * S));
            b = arr != null ? arr[0] : null;
            icons[path] = b;
            return b;
        }

        void ScreenTimeTab(UI ui, RectangleF c, Fonts2 f)
        {
            if (!Look.Bool("ScreenTime"))
            {
                ui.Text("Screen Time is off", new RectangleF(c.X, c.Y + 50 * S, c.Width, 24 * S), f.Title, f.White, 1);
                ui.Text("It counts time per app on this PC only. Nothing is sent anywhere.", new RectangleF(c.X, c.Y + 76 * S, c.Width, 18 * S), f.Small, f.Dim, 1);
                var btn = new RectangleF(c.X + c.Width / 2 - 70 * S, c.Y + 106 * S, 140 * S, 30 * S);
                ui.Fill(btn, Color.FromArgb(Hover == "ston" ? 255 : 235, 255, 255, 255), 15 * S);
                using (var db = new SolidBrush(Color.FromArgb(235, 18, 18, 22))) ui.Text("Turn it on", btn, f.Bold, db, 1);
                ui.AddHit("ston", btn, () => { Look.Set("ScreenTime", true); Settings.Save(); ScreenTime.Init(); }, true);
                return;
            }
            var list = ScreenTime.Today();
            double total = 0;
            var perCat = new double[ScreenTime.Categories.Length];
            foreach (var a in list) { total += a.Seconds; perCat[ScreenTime.CategoryOf(a)] += a.Seconds; }
            if (total < 60)
            {
                ui.Text("Nothing recorded yet today", new RectangleF(c.X, c.Y + c.Height / 2 - 24 * S, c.Width, 22 * S), f.Title, f.White, 1);
                ui.Text("Screen Time counts the app in front while you're using the PC.", new RectangleF(c.X, c.Y + c.Height / 2, c.Width, 18 * S), f.Small, f.Dim, 1);
                return;
            }

            // donut by category
            float d = 128 * S;
            var ring = new RectangleF(c.X + 4 * S, c.Y + 18 * S, d, d);
            float th = 15 * S, startAngle = -90;
            var inner = RectangleF.Inflate(ring, -th / 2, -th / 2);
            using (var trackPen = new Pen(Color.FromArgb(30, 255, 255, 255), th)) ui.G.DrawEllipse(trackPen, inner);
            for (int i = 0; i < perCat.Length; i++)
            {
                if (perCat[i] <= 0) continue;
                float sweep = (float)(perCat[i] / total * 360);
                using (var pen = new Pen(ScreenTime.CategoryColors[i], th)) ui.G.DrawArc(pen, inner, startAngle + 1, Math.Max(0.5f, sweep - 2));
                startAngle += sweep;
            }
            ui.Text(Span(TimeSpan.FromSeconds(total)), new RectangleF(ring.X, ring.Y + d / 2 - 16 * S, d, 22 * S), f.Title, f.White, 1);
            ui.Text("today", new RectangleF(ring.X, ring.Y + d / 2 + 6 * S, d, 16 * S), f.Small, f.Dim, 1);

            // categories
            float lx = c.X + 160 * S, lw = 170 * S, ly = c.Y + 4 * S;
            ui.Text("BY CATEGORY", new RectangleF(lx, ly, lw, 16 * S), f.Small, f.Faint);
            ly += 24 * S;
            var order = new List<int>();
            for (int i = 0; i < perCat.Length; i++) if (perCat[i] >= 60) order.Add(i);
            order.Sort((a, b) => perCat[b].CompareTo(perCat[a]));
            for (int k = 0; k < order.Count && k < 6; k++)
            {
                int i = order[k];
                using (var dot = new SolidBrush(ScreenTime.CategoryColors[i])) ui.G.FillEllipse(dot, lx, ly + 6 * S, 8 * S, 8 * S);
                ui.Text(ScreenTime.Categories[i], new RectangleF(lx + 16 * S, ly, lw - 70 * S, 20 * S), f.Body, f.White);
                ui.Text(Span(TimeSpan.FromSeconds(perCat[i])), new RectangleF(lx, ly, lw, 20 * S), f.Body, f.Dim, 2);
                ly += 25 * S;
            }

            // now + top apps
            float ax = c.X + 360 * S, aw = c.Right - ax;
            TimeSpan since;
            var cur = ScreenTime.Current(out since);
            if (cur != null)
            {
                var pill = new RectangleF(ax, c.Y, aw, 26 * S);
                ui.Fill(pill, Color.FromArgb(26, 255, 255, 255), 13 * S);
                ui.Text("NOW", new RectangleF(pill.X + 12 * S, pill.Y, 40 * S, pill.Height), f.Small, f.Faint);
                ui.Text(cur.Name, new RectangleF(pill.X + 46 * S, pill.Y, pill.Width - 110 * S, pill.Height), f.Bold, f.White);
                ui.Text(Span(since), new RectangleF(pill.X, pill.Y, pill.Width - 12 * S, pill.Height), f.Small, f.Dim, 2);
            }
            float ay = c.Y + 36 * S;
            double top = list.Count > 0 ? list[0].Seconds : 1;
            for (int k = 0; k < list.Count && k < 5; k++)
            {
                var a = list[k];
                var ic = IconFor(a.Icon);
                if (ic != null) ui.G.DrawImage(ic, new RectangleF(ax, ay + 2 * S, 20 * S, 20 * S));
                ui.Text(a.Name, new RectangleF(ax + 28 * S, ay, aw - 90 * S, 20 * S), f.Body, f.White);
                ui.Text(Span(TimeSpan.FromSeconds(a.Seconds)), new RectangleF(ax, ay, aw, 20 * S), f.Body, f.Dim, 2);
                var bar = new RectangleF(ax + 28 * S, ay + 22 * S, aw - 28 * S, 3 * S);
                ui.Fill(bar, Color.FromArgb(24, 255, 255, 255), 1.5f * S);
                ui.Fill(new RectangleF(bar.X, bar.Y, Math.Max(3 * S, bar.Width * (float)(a.Seconds / top)), bar.Height), ScreenTime.CategoryColors[ScreenTime.CategoryOf(a)], 1.5f * S);
                ay += 33 * S;
            }
        }

        // ------------------------------------------------------------------ shared bits

        static string Ago(DateTime t)
        {
            var d = DateTime.Now - t;
            if (d.TotalMinutes < 1) return "now";
            if (d.TotalMinutes < 60) return (int)d.TotalMinutes + "m ago";
            if (d.TotalHours < 24) return (int)d.TotalHours + "h ago";
            return d.TotalDays < 2 ? "yesterday" : (int)d.TotalDays + "d ago";
        }

        /// <summary>A tick circle (open, or filled with a check when done).</summary>
        void TickCircle(UI ui, RectangleF r, bool done, bool hot)
        {
            using (var pen = new Pen(Color.FromArgb(hot ? 230 : 150, 255, 255, 255), 1.6f * S)) ui.G.DrawEllipse(pen, r);
            if (done)
            {
                using (var b = new SolidBrush(Color.FromArgb(255, 48, 209, 88))) ui.G.FillEllipse(b, r);
                using (var gf = new Font(Fonts.Icons, r.Height * 0.55f, GraphicsUnit.Pixel)) ui.Glyph("", r, gf, Brushes.White);
            }
        }

        void Pill(UI ui, RectangleF r, string text, Color fill, Font font, Brush b)
        {
            ui.Fill(r, fill, r.Height / 2);
            ui.Text(text, r, font, b, 1);
        }

        float PillWidth(UI ui, string text, Font font) { return ui.G.MeasureString(text, font).Width + 14 * S; }

        // ------------------------------------------------------------------ dashboard

        [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern uint SetThreadExecutionState(uint flags);
        static bool keepAwake;

        static void ToggleKeepAwake()
        {
            keepAwake = !keepAwake;
            // ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED while on; ES_CONTINUOUS alone clears it
            SetThreadExecutionState(keepAwake ? 0x80000003u : 0x80000000u);
        }

        void DashboardTab(UI ui, RectangleF c, Fonts2 f)
        {
            float gap = 12 * S, cw = (c.Width - 2 * gap) / 3, rh = (c.Height - gap) / 2;
            var col1 = new RectangleF(c.X, c.Y, cw, c.Height);
            var b2 = new RectangleF(c.X + cw + gap, c.Y, cw, rh);
            var b3 = new RectangleF(b2.X, c.Y + rh + gap, cw, rh);
            var b4 = new RectangleF(c.X + 2 * (cw + gap), c.Y, cw, rh);
            var b5 = new RectangleF(b4.X, b3.Y, cw, rh);
            foreach (var r in new[] { col1, b2, b3, b4, b5 }) Card(ui, r);
            float ip = 14 * S;

            // reminders
            var open = Reminders.Open();
            ui.Text("Reminders", new RectangleF(col1.X + ip, col1.Y + 10 * S, cw - 2 * ip, 20 * S), f.Bold, f.White);
            ui.Text(open.Count == 0 ? "" : open.Count.ToString(), new RectangleF(col1.X + ip, col1.Y + 10 * S, cw - 2 * ip, 20 * S), f.Small, f.Faint, 2);
            float y = col1.Y + 38 * S;
            if (open.Count == 0) ui.Text("All caught up", new RectangleF(col1.X + ip, y, cw - 2 * ip, 20 * S), f.Body, f.Faint);
            for (int i = 0; i < open.Count && i < 4; i++)
            {
                var it = open[i];
                string due = Reminders.DueText(it);
                if (y + (due != null ? 36 : 24) * S > col1.Bottom - 38 * S) break;
                string id = "dr" + it.Id;
                var tick = new RectangleF(col1.X + ip, y + 2 * S, 16 * S, 16 * S);
                TickCircle(ui, tick, false, Hover == id);
                ui.AddHit(id, RectangleF.Inflate(tick, 4 * S, 4 * S), () => Reminders.Toggle(it), true);
                ui.Text(it.Text, new RectangleF(tick.Right + 8 * S, y, cw - 2 * ip - 26 * S, 20 * S), f.Body, f.White);
                if (due != null)
                    using (var db = new SolidBrush(Reminders.Overdue(it) ? Color.FromArgb(255, 255, 105, 97) : NowPlayingView.Faint))
                        ui.Text(due, new RectangleF(tick.Right + 8 * S, y + 18 * S, cw - 2 * ip - 26 * S, 16 * S), f.Small, db);
                y += (due != null ? 38 : 26) * S;
            }
            var add = new RectangleF(col1.X + ip - 6 * S, col1.Bottom - 34 * S, 130 * S, 26 * S);
            if (Hover == "dadd") ui.Fill(add, Color.FromArgb(24, 255, 255, 255), 13 * S);
            ui.Text("+  New reminder", new RectangleF(add.X + 6 * S, add.Y, add.Width, add.Height), f.Small, f.Dim);
            ui.AddHit("dadd", add, () => { SetTab("reminders", false); FocusField = "rem"; Activate(); N.SetForegroundWindow(Handle); }, true);

            // most-used apps today (a launcher that fills itself)
            ui.Text("Most used today", new RectangleF(b2.X + ip, b2.Y + 10 * S, cw - 2 * ip, 20 * S), f.Bold, f.White);
            var apps = new List<string>();
            foreach (var a in ScreenTime.Today()) if (a.Icon != null && apps.Count < 5) apps.Add(a.Icon);
            foreach (var p in Settings.Pinned) if (apps.Count < 5 && !apps.Contains(p)) apps.Add(p);
            float isz = 30 * S, slot = (cw - 2 * ip) / 5;
            for (int i = 0; i < apps.Count; i++)
            {
                var r = new RectangleF(b2.X + ip + i * slot + (slot - isz) / 2, b2.Y + 44 * S, isz, isz);
                string id = "la" + i, path = apps[i];
                if (Hover == id) ui.Fill(RectangleF.Inflate(r, 5 * S, 5 * S), Color.FromArgb(30, 255, 255, 255), 10 * S);
                var ic = IconFor(path, 30);
                if (ic != null) ui.G.DrawImage(ic, r);
                ui.AddHit(id, RectangleF.Inflate(r, 5 * S, 5 * S), () => Launcher.Open(path));
            }

            // quick toggles
            ui.Text("Quick toggles", new RectangleF(b3.X + ip, b3.Y + 10 * S, cw - 2 * ip, 20 * S), f.Bold, f.White);
            var ids = new List<string>(); var glyphs = new List<string>(); var ons = new List<bool>(); var acts = new List<Action>();
            ids.Add("qdark"); glyphs.Add(""); ons.Add(Theme.Dark); acts.Add(ControlCenter.ToggleDarkMode);
            if (Radios.HasWifi) { ids.Add("qwifi"); glyphs.Add(""); ons.Add(Radios.WifiOn); acts.Add(() => Radios.SetWifi(!Radios.WifiOn)); }
            if (Radios.HasBt) { ids.Add("qbt"); glyphs.Add(""); ons.Add(Radios.BtOn); acts.Add(() => Radios.SetBluetooth(!Radios.BtOn)); }
            ids.Add("qawake"); glyphs.Add(""); ons.Add(keepAwake); acts.Add(ToggleKeepAwake);
            ids.Add("qshot"); glyphs.Add(""); ons.Add(false); acts.Add(() => { CloseFlyout(); Launcher.Run("ms-screenclip:"); });
            ids.Add("qlock"); glyphs.Add(""); ons.Add(false); acts.Add(() => N.LockWorkStation());
            float tsz = 32 * S, tslot = (cw - 2 * ip) / 6;
            using (var gf = new Font(Fonts.Icons, 13f * S, GraphicsUnit.Pixel))
            using (var dark = new SolidBrush(Color.FromArgb(235, 18, 18, 22)))
                for (int i = 0; i < ids.Count; i++)
                {
                    var r = new RectangleF(b3.X + ip + i * tslot + (tslot - tsz) / 2, b3.Y + 44 * S, tsz, tsz);
                    using (var bg = new SolidBrush(ons[i] ? Color.FromArgb(Hover == ids[i] ? 255 : 235, 255, 255, 255) : Color.FromArgb(Hover == ids[i] ? 60 : 34, 255, 255, 255)))
                        ui.G.FillEllipse(bg, r);
                    ui.Glyph(glyphs[i], r, gf, ons[i] ? (Brush)dark : f.White);
                    var act = acts[i];
                    ui.AddHit(ids[i], r, () => act(), true);
                }

            // pomodoro + day progress
            var rem = Pomodoro.Remaining;
            ui.Text("Pomodoro", new RectangleF(b4.X + ip, b4.Y + 10 * S, cw - 2 * ip, 20 * S), f.Bold, f.White);
            ui.Text(((int)rem.TotalMinutes).ToString("00") + ":" + rem.Seconds.ToString("00"), new RectangleF(b4.X + ip, b4.Y + 32 * S, 120 * S, 32 * S), f.Big, f.White);
            ui.Text(Pomodoro.Running ? Pomodoro.Name(Pomodoro.Current) : (Pomodoro.Progress > 0 ? "Paused" : Pomodoro.Name(Pomodoro.Current)), new RectangleF(b4.X + ip, b4.Y + 64 * S, 120 * S, 16 * S), f.Small, f.Dim);
            using (var big = new Font(Fonts.Icons, 13f * S, GraphicsUnit.Pixel))
            using (var dark = new SolidBrush(Color.FromArgb(235, 18, 18, 22)))
                RoundBtn(ui, "dpomo", new PointF(b4.Right - ip - 17 * S, b4.Y + 50 * S), 17 * S, Pomodoro.Running ? "" : "", Pomodoro.Toggle, big, dark, true);
            int start = Math.Max(0, Math.Min(23, Look.Int("DayStart"))), end = Math.Max(start + 1, Math.Min(24, Look.Int("DayEnd")));
            var now = DateTime.Now;
            float frac = (float)Math.Max(0, Math.Min(1, (now - now.Date.AddHours(start)).TotalHours / (end - start)));
            var bar = new RectangleF(b4.X + ip, b4.Bottom - 12 * S, cw - 2 * ip, 4 * S);
            ui.Fill(bar, Color.FromArgb(36, 255, 255, 255), 2 * S);
            ui.Fill(new RectangleF(bar.X, bar.Y, Math.Max(4 * S, bar.Width * frac), bar.Height), Ember, 2 * S);
            ui.Text("Day " + (int)Math.Round(frac * 100) + "%", new RectangleF(b4.X + ip, bar.Y - 18 * S, cw - 2 * ip, 16 * S), f.Small, f.Faint, 2);

            // weather (or Claude when there's no city yet)
            var w = Weather.HasCity ? Weather.Now : null;
            if (Weather.HasCity) Weather.Ensure();
            if (w != null)
            {
                WeatherIcons.Draw(ui.G, new RectangleF(b5.X + ip - 4 * S, b5.Y + 12 * S, 44 * S, 44 * S), w.Code, w.Day);
                ui.Text(Math.Round(w.Temp) + "°", new RectangleF(b5.X + ip + 46 * S, b5.Y + 10 * S, 100 * S, 36 * S), f.Big, f.White);
                ui.Text(Weather.Describe(w.Code), new RectangleF(b5.X + ip + 46 * S, b5.Y + 44 * S, cw - 2 * ip - 46 * S, 16 * S), f.Small, f.Dim);
                ui.Text(Weather.Place + "  ·  H " + Math.Round(w.High) + "° L " + Math.Round(w.Low) + "°", new RectangleF(b5.X + ip, b5.Bottom - 24 * S, cw - 2 * ip, 16 * S), f.Small, f.Faint);
            }
            else
            {
                int active = 0;
                foreach (var s in ClaudeBridge.Sessions.Values) if ((s.Status == "Running" || s.Status == "Waiting") && (now - s.Updated).TotalMinutes < 30) active++;
                ui.Text("AI Coding", new RectangleF(b5.X + ip, b5.Y + 10 * S, cw - 2 * ip, 20 * S), f.Bold, f.White);
                ui.Text(ClaudeBridge.Pending.Count > 0 ? "Claude is waiting for you" : active > 0 ? active + " session" + (active == 1 ? "" : "s") + " active" : "No active sessions",
                    new RectangleF(b5.X + ip, b5.Y + 36 * S, cw - 2 * ip, 20 * S), f.Body, ClaudeBridge.Pending.Count > 0 ? f.White : f.Dim);
                if (!Weather.HasCity) ui.Text("Add your city in Harbor settings for weather", new RectangleF(b5.X + ip, b5.Bottom - 24 * S, cw - 2 * ip, 16 * S), f.Small, f.Faint);
            }
        }

        Bitmap IconFor(string path, int logical)
        {
            if (string.IsNullOrEmpty(path)) return null;
            string key = path + "|" + logical;
            Bitmap b;
            if (icons.TryGetValue(key, out b)) return b;
            var arr = ShellIcons.LoadIcons(path, (int)Math.Round(logical * S));
            b = arr != null ? arr[0] : null;
            icons[key] = b;
            return b;
        }

        // ------------------------------------------------------------------ reminders

        static readonly string[] dueNames = { "No time", "In 30 min", "In 1 hour", "This evening", "Tomorrow 9:00" };
        int dueChoice;

        static DateTime? DueFor(int choice)
        {
            var now = DateTime.Now;
            switch (choice)
            {
                case 1: return now.AddMinutes(30);
                case 2: return now.AddHours(1);
                case 3: return now.Hour < 18 ? now.Date.AddHours(18) : now.Hour < 20 ? now.Date.AddHours(20) : now.Date.AddDays(1).AddHours(18);
                case 4: return now.Date.AddDays(1).AddHours(9);
                default: return null;
            }
        }

        void AddReminder()
        {
            string t = Field("rem").Trim();
            if (t == "") return;
            Reminders.Add(t, DueFor(dueChoice));
            Fields["rem"] = "";
            dueChoice = 0;
            Refresh2();
        }

        void RemindersTab(UI ui, RectangleF c, Fonts2 f)
        {
            // new reminder
            float lw = 250 * S;
            ui.Text("New reminder", new RectangleF(c.X, c.Y, lw, 20 * S), f.Bold, f.White);
            ui.Pad = c.X - 8 * S;
            ui.Right = c.X + lw + 8 * S;
            ui.Y = c.Y + 20 * S;
            ui.TextField("rem", "What do you want to remember?", false, AddReminder);
            float cx = c.X, cy = c.Y + 64 * S;
            using (var dark = new SolidBrush(Color.FromArgb(235, 18, 18, 22)))
            {
                for (int i = 0; i < dueNames.Length; i++)
                {
                    float w = PillWidth(ui, dueNames[i], f.Small);
                    if (cx + w > c.X + lw) { cx = c.X; cy += 30 * S; }
                    var chip = new RectangleF(cx, cy, w, 24 * S);
                    string id = "due" + i;
                    bool sel = dueChoice == i;
                    Pill(ui, chip, dueNames[i], sel ? Color.FromArgb(235, 255, 255, 255) : Color.FromArgb(Hover == id ? 54 : 30, 255, 255, 255), f.Small, sel ? (Brush)dark : f.White);
                    int pick = i;
                    ui.AddHit(id, chip, () => dueChoice = pick, true);
                    cx += w + 6 * S;
                }
                var add = new RectangleF(c.X, c.Bottom - 34 * S, lw, 30 * S);
                bool can = Field("rem").Trim() != "";
                ui.Fill(add, can ? Color.FromArgb(Hover == "radd" ? 255 : 235, 255, 255, 255) : Color.FromArgb(40, 255, 255, 255), 15 * S);
                ui.Text("Add reminder", add, f.Bold, can ? (Brush)dark : f.Faint, 1);
                ui.AddHit("radd", add, AddReminder, true);
            }

            // the list
            float rx = c.X + lw + 30 * S, rw = c.Right - rx;
            var open = Reminders.Open();
            int doneToday = Reminders.DoneToday;
            ui.Text(open.Count == 0 ? "All caught up" : open.Count + " to do", new RectangleF(rx, c.Y, rw, 20 * S), f.Bold, f.White);
            if (doneToday > 0) ui.Text(doneToday + " done today", new RectangleF(rx, c.Y, rw, 20 * S), f.Small, f.Faint, 2);
            if (open.Count == 0) { ui.Text("Nothing to remember. Add something on the left.", new RectangleF(rx, c.Y + 70 * S, rw, 20 * S), f.Body, f.Faint, 1); return; }
            float y = c.Y + 30 * S;
            for (int i = 0; i < open.Count && y + 34 * S <= c.Bottom; i++)
            {
                var it = open[i];
                var row = new RectangleF(rx - 8 * S, y, rw + 8 * S, 34 * S);
                string id = "rr" + it.Id;
                bool hot = Hover == id || Hover == "rt" + it.Id || Hover == "rx" + it.Id;
                if (hot) ui.Fill(row, Color.FromArgb(16, 255, 255, 255), 9 * S);
                ui.AddHit(id, row, null);
                var tick = new RectangleF(rx, y + 9 * S, 16 * S, 16 * S);
                TickCircle(ui, tick, false, Hover == "rt" + it.Id);
                ui.AddHit("rt" + it.Id, RectangleF.Inflate(tick, 5 * S, 5 * S), () => Reminders.Toggle(it), true);
                string due = Reminders.DueText(it);
                ui.Text(it.Text, new RectangleF(tick.Right + 10 * S, y, rw - 150 * S, 34 * S), f.Body, f.White);
                if (due != null)
                    using (var db = new SolidBrush(Reminders.Overdue(it) ? Color.FromArgb(255, 255, 105, 97) : NowPlayingView.Dim))
                        ui.Text(due, new RectangleF(rx, y, rw - (hot ? 30 : 4) * S, 34 * S), f.Small, db, 2);
                if (hot)
                {
                    var x = new RectangleF(row.Right - 26 * S, y + 7 * S, 20 * S, 20 * S);
                    using (var gf = new Font(Fonts.Icons, 9f * S, GraphicsUnit.Pixel)) ui.Glyph("", x, gf, Hover == "rx" + it.Id ? f.White : f.Dim);
                    ui.AddHit("rx" + it.Id, x, () => Reminders.Remove(it), true);
                }
                y += 36 * S;
            }
        }

        // ------------------------------------------------------------------ AI coding (Claude Code)

        static readonly Color Running = Color.FromArgb(255, 48, 209, 88), Waiting = Color.FromArgb(255, 255, 159, 10);

        void AiTab(UI ui, RectangleF c, Fonts2 f)
        {
            var now = DateTime.Now;
            int active = 0;
            foreach (var s in ClaudeBridge.Sessions.Values) if ((s.Status == "Running" || s.Status == "Waiting") && (now - s.Updated).TotalMinutes < 30) active++;
            ui.Text("AI Coding", new RectangleF(c.X, c.Y - 2 * S, 200 * S, 22 * S), f.Title, f.White);
            float hx = c.X + ui.G.MeasureString("AI Coding", f.Title).Width + 10 * S;
            using (var dot = new SolidBrush(active > 0 ? Running : Color.FromArgb(90, 255, 255, 255))) ui.G.FillEllipse(dot, hx, c.Y + 7 * S, 7 * S, 7 * S);
            ui.Text(active + " active", new RectangleF(hx + 12 * S, c.Y, 120 * S, 18 * S), f.Small, active > 0 ? f.White : f.Faint);
            if (!ClaudeBridge.HooksOn)
            {
                var hint = new RectangleF(c.Right - 300 * S, c.Y - 2 * S, 300 * S, 22 * S);
                ui.Text("Live status & approvals: turn on in Harbor settings", hint, f.Small, Hover == "aiset" ? f.White : f.Faint, 2);
                ui.AddHit("aiset", hint, () => { CloseFlyout(); SettingsWindow.ShowWindow(); });
            }
            float y = c.Y + 30 * S;

            // Claude is asking permission
            if (ClaudeBridge.Pending.Count > 0)
            {
                var r = ClaudeBridge.Pending[0];
                var card = new RectangleF(c.X, y, c.Width, 92 * S);
                ui.Fill(card, Color.FromArgb(34, 255, 159, 10), 14 * S);
                using (var pen = new Pen(Color.FromArgb(140, 255, 159, 10), 1.2f * S)) using (var p = Gfx.RoundRect(card, 14 * S)) ui.G.DrawPath(pen, p);
                float ix = card.X + 14 * S, iw = card.Width - 28 * S;
                ui.Text(r.Project + "  ·  Claude wants to use " + r.Tool, new RectangleF(ix, card.Y + 8 * S, iw - 160 * S, 20 * S), f.Bold, f.White);
                ui.Text("waiting " + Span(now - r.At) + (ClaudeBridge.Pending.Count > 1 ? "  ·  +" + (ClaudeBridge.Pending.Count - 1) + " more" : ""), new RectangleF(ix, card.Y + 8 * S, iw, 20 * S), f.Small, f.Faint, 2);
                using (var mono = new Font("Consolas", 12f * S, GraphicsUnit.Pixel))
                using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter })
                    ui.G.DrawString((r.Detail != null ? r.Detail + "\n" : "") + (r.Summary ?? ""), mono, f.Dim, new RectangleF(ix, card.Y + 30 * S, iw - 290 * S, 52 * S), sf);
                using (var dark = new SolidBrush(Color.FromArgb(235, 18, 18, 22)))
                {
                    float bh = 30 * S, by = card.Bottom - bh - 10 * S;
                    var allow = new RectangleF(card.Right - 14 * S - 80 * S, by, 80 * S, bh);
                    var always = new RectangleF(allow.X - 8 * S - 108 * S, by, 108 * S, bh);
                    var deny = new RectangleF(always.X - 8 * S - 70 * S, by, 70 * S, bh);
                    ui.Fill(allow, Color.FromArgb(Hover == "cAllow" ? 255 : 238, 255, 255, 255), bh / 2);
                    ui.Text("Allow", allow, f.Bold, dark, 1);
                    ui.Fill(always, Color.FromArgb(Hover == "cAlways" ? 70 : 40, 255, 255, 255), bh / 2);
                    ui.Text("Always allow", always, f.Bold, f.White, 1);
                    ui.Fill(deny, Color.FromArgb(Hover == "cDeny" ? 110 : 60, 255, 69, 58), bh / 2);
                    ui.Text("Deny", deny, f.Bold, f.White, 1);
                    ui.AddHit("cAllow", allow, () => ClaudeBridge.Answer(r, "allow"), true);
                    ui.AddHit("cAlways", always, () => ClaudeBridge.Answer(r, "always"), true);
                    ui.AddHit("cDeny", deny, () => ClaudeBridge.Answer(r, "deny"), true);
                }
                y = card.Bottom + 10 * S;
            }

            // sessions: live ones (from the hook) merged with recent history
            var rows = new List<ClaudeHistory.Entry>(ClaudeHistory.Recent());
            var seen = new HashSet<string>();
            foreach (var e in rows) seen.Add(e.SessionId ?? "");
            foreach (var s in ClaudeBridge.Sessions.Values)
                if (!seen.Contains(s.Id ?? "")) rows.Insert(0, new ClaudeHistory.Entry { SessionId = s.Id, Cwd = s.Cwd, Model = s.Model, LastActive = s.Updated });
            if (rows.Count == 0)
            {
                ui.Text(ClaudeHistory.Available ? "No Claude Code sessions in the last 3 days" : "Claude Code isn't set up on this PC", new RectangleF(c.X, y + 30 * S, c.Width, 20 * S), f.Body, f.Faint, 1);
                return;
            }
            using (var mono = new Font("Consolas", 11.5f * S, GraphicsUnit.Pixel))
                foreach (var e in rows)
                {
                    if (y + 40 * S > c.Bottom) break;
                    ClaudeBridge.Session live;
                    ClaudeBridge.Sessions.TryGetValue(e.SessionId ?? "", out live);
                    string status = live != null && (now - live.Updated).TotalMinutes < 30 ? live.Status : null;
                    Color sc = status == "Running" ? Running : status == "Waiting" ? Waiting : Color.FromArgb(90, 255, 255, 255);
                    using (var dot = new SolidBrush(sc)) ui.G.FillEllipse(dot, c.X + 2 * S, y + 7 * S, 8 * S, 8 * S);
                    float x = c.X + 18 * S;
                    ui.Text(e.Project, new RectangleF(x, y, 180 * S, 20 * S), f.Bold, f.White);
                    x += Math.Min(180 * S, ui.G.MeasureString(e.Project, f.Bold).Width + 6 * S);
                    if (status != null && status != "Idle")
                    {
                        float pw = PillWidth(ui, status, f.Small);
                        Pill(ui, new RectangleF(x, y + 1 * S, pw, 18 * S), status, Color.FromArgb(60, sc), f.Small, f.White);
                        x += pw + 6 * S;
                    }
                    string model = ClaudeHistory.ShortModel(live != null && live.Model != null ? live.Model : e.Model);
                    if (model != null)
                    {
                        float pw = PillWidth(ui, model, f.Small);
                        Pill(ui, new RectangleF(x, y + 1 * S, pw, 18 * S), model, Color.FromArgb(30, 255, 255, 255), f.Small, f.Dim);
                        x += pw + 8 * S;
                    }
                    if (!string.IsNullOrEmpty(e.Branch)) ui.Text("⎇ " + e.Branch, new RectangleF(x, y + 1 * S, Math.Max(0, c.Right - x - 150 * S), 18 * S), mono, f.Faint);
                    ui.Text(Ago(live != null ? live.Updated : e.LastActive), new RectangleF(c.X, y, c.Width, 18 * S), f.Small, f.Faint, 2);
                    string line2 = live != null && live.LastMessage != null ? live.LastMessage : e.Title;
                    if (line2 != null) ui.Text(line2, new RectangleF(c.X + 18 * S, y + 20 * S, c.Width - 170 * S, 18 * S), f.Small, f.Dim);
                    if (e.Messages > 0) ui.Text(e.Messages + " msgs · " + ClaudeHistory.Tokens(e.Tokens) + " tok", new RectangleF(c.X, y + 20 * S, c.Width, 18 * S), f.Small, f.Faint, 2);
                    y += 42 * S;
                }
        }
    }
}
