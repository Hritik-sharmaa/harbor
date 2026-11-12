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
    sealed partial class Island : Flyout
    {
        static Island inst;

        // every tab the island has, in the default order; Harbor → General → Notch tabs picks which show and in what order
        static readonly string[] allTabs = { "dashboard", "media", "calendar", "pomodoro", "timers", "weather", "screentime", "reminders", "notes", "clipboard", "shelf", "downloads", "ai", "usage" };
        static readonly string[] allNames = { "Dashboard", "Media", "Calendar", "Pomodoro", "Timers", "Weather", "Screen Time", "Reminders", "Notes", "Clipboard", "Shelf", "Downloads", "AI Coding", "Claude Usage" };
        static readonly string[] allGlyphs = { "", "", "", "", "", null, "", "", "", "", "", "", "", "" }; // weather draws its own icon
        static string[] tabs { get { return ShownTabs().ToArray(); } }

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
            LogicalWidth = (int)(BodyW + 2 * (Ear + SideRoom));
            FixedLogicalHeight = (int)(BodyH + Bounce);
            var shown = tabs;
            tab = Array.IndexOf(shown, Look.Get("IslandTab")) >= 0 ? Look.Get("IslandTab") : (shown.Length > 0 ? shown[0] : "media");
            Media.Changed += delegate { if (Visible && tab == "media") Refresh2(); };
            Lyrics.Changed += delegate { view.Snap(); if (Visible && tab == "media") Refresh2(); };
            Pomodoro.Changed += delegate { if (Visible && tab == "pomodoro") Refresh2(); };
            Weather.Changed += delegate { if (Visible && (tab == "weather" || tab == "dashboard")) Refresh2(); };
            Reminders.Changed += delegate { if (Visible) Refresh2(); };
            ClaudeBridge.Changed += delegate { if (Visible) Refresh2(); };
            ClaudeHistory.Changed += delegate { if (Visible && tab == "ai") Refresh2(); };
            ClipboardHistory.Changed += delegate { if (Visible && tab == "clipboard") Refresh2(); };
            Shelf.Changed += delegate { if (Visible && tab == "shelf") Refresh2(); };
            ClaudeUsage.Changed += delegate { if (Visible && tab == "usage") Refresh2(); };
            FocusMode.Changed += delegate { if (Visible) Refresh2(); };
            Timers.Changed += delegate { if (Visible && (tab == "timers" || tab == "dashboard")) Refresh2(); };
            CalendarFeed.Changed += delegate { if (Visible && (tab == "calendar" || tab == "dashboard")) Refresh2(); };
            Notes.Changed += delegate { if (Visible && tab == "notes") Refresh2(); };
            Downloads.Changed += delegate { if (Visible && tab == "downloads") Refresh2(); };
            LastPlayed.Changed += delegate { if (Visible && tab == "media") Refresh2(); };
            AllowDrop = true; // files dropped on the notch go on the shelf
            tick.Tick += delegate { Tick(); };
            motion.Tick += delegate { Motion(); };
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
            if (inst.Visible)
            {
                if (inst.closing) { inst.closing = false; inst.Spring("w", 1, 190, 17); inst.Spring("h", 1, 170, 16); } // was folding away: grow back
                inst.Refresh2();
                return;
            }
            var center = new RectangleF(sb.Width / 2f - 1, 0, 2, sb.Height);
            inst.ShowBelow(bar, center, "island", 1);
        }

        void SetTab(string t, bool remember)
        {
            // any tab can be opened for something (a dropped file, Claude asking) even if it's hidden from the bar
            if (Array.IndexOf(allTabs, t) < 0 || t == tab) return;
            if (Visible)
            {
                // the new page slides in from the side it's on
                var shown = tabs;
                slideDir = Array.IndexOf(shown, t) >= Array.IndexOf(shown, tab) ? 1 : -1;
                Spring("slide", 0, 260, 23);
                Jump("slide", 1);
            }
            tab = t;
            Media.WantTimeline = t == "media";
            if (t == "media") { Media.RefreshTimeline(); view.Snap(); }
            if (remember) { Look.Set("IslandTab", t); Settings.Save(); }
        }

        protected override void OnOpened()
        {
            outsideMs = 0;
            Jump("dots", Math.Max(0, Array.IndexOf(tabs, tab)));
            Jump("slide", 0);
            Media.WantTimeline = tab == "media";
            if (tab == "media") Media.RefreshTimeline();
            view.Snap();
            tick.Start();
            watch.Start();
            Grow();
        }

        protected override void OnClosedFlyout()
        {
            motion.Stop();
            springs.Clear();
            closing = false;
            tick.Stop();
            watch.Stop();
            Media.WantTimeline = false;
            byKeyboard = false;
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
            if (me.Contains(p) || RectangleF.Inflate(trigger, 6, 0).Contains(p) || Dragging != null || dragOut || byKeyboard || StillDropping() || FocusField != null || ClaudeBridge.Pending.Count > 0) { outsideMs = 0; return; }
            outsideMs += watch.Interval;
            if (outsideMs >= 400) Shrink();
        }

        /// <summary>Redraw only what moves: lyrics at 30 fps while gliding, the song and timer a few times a second.</summary>
        void Tick()
        {
            if (!Visible) { tick.Stop(); return; }
            int now = Environment.TickCount, age = now - lastFrame;
            if (motion.Enabled) return; // the springs are redrawing anyway
            bool need = Dragging != null
                || (Media.Playing && age > 60)  // the sound bars in the header
                || DateTime.Now.Minute != lastMinute
                || (tab == "media" && (view.Animating || (Media.Playing && age > 250)))
                || ((tab == "pomodoro" || tab == "dashboard" || tab == "ai") && age > 1000)
                || (tab == "downloads" && DownloadsMoving(now))
                || ((tab == "screentime" || tab == "usage" || tab == "calendar" || tab == "timers") && age > 15000);
            if (need) { lastFrame = now; Refresh2(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            dragKey = null;
            view.MouseUp(this);
            base.OnMouseUp(e);
        }

        /// <summary>Clicking a text field is the one time the island takes the keyboard (it never does on hover).</summary>
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            StartDragFrom(e);
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

        // the notch body is 724 x 280; the window is a little bigger so the springy open can overshoot,
        // and the 'ears' that curve it into the menu bar fit on either side
        const float BodyW = 724, BodyH = 280, Ear = 12, SideRoom = 12, Bounce = 14;
        const float Pad = 44, HeaderH = 48, DotsH = 24;

        // ------------------------------------------------------------------ the notch: shape, open / close

        protected override float GapBelowBar { get { return 0; } } // attached to the menu bar, like a notch
        protected override bool OwnGlass { get { return true; } }
        protected override bool AlwaysDark { get { return true; } }
        protected override bool Layered { get { return true; } }   // anti-aliased curves, drawn with per-pixel alpha
        protected override void ApplyShape() { }                   // the shape is in the pixels, not a window region

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int none = 1; // DWMWCP_DONOTROUND
            try { N.DwmSetWindowAttribute(Handle, 33, ref none, 4); } catch { }
        }

        bool closing;

        /// <summary>Springs out of the menu bar: wide and short first, then down with a little bounce.</summary>
        void Grow()
        {
            closing = false;
            Jump("w", 0.42f); Jump("h", 0.08f);
            Spring("w", 1, 190, 17);
            Spring("h", 1, 170, 16);
            if (App.SnapshotMode) { Jump("w", 1); Jump("h", 1); }
        }

        /// <summary>Folds back up into the menu bar (quick, no bounce), then hides.</summary>
        void Shrink()
        {
            if (closing || !Visible) return;
            closing = true;
            Spring("w", 0.42f, 420, 42);
            Spring("h", 0, 380, 40);
        }

        /// <summary>The notch outline for a body bx..bx+bw wide and bh tall, with concave ears into the menu bar.</summary>
        static GraphicsPath NotchPath(float bx, float bw, float bh, float r, float e)
        {
            var p = new GraphicsPath();
            r = Math.Max(1, Math.Min(r, Math.Min(bw, bh) / 2));
            if (e > 0.5f) p.AddArc(bx - 2 * e, 0, 2 * e, 2 * e, 270, 90);
            else p.AddLine(bx, 0, bx, 0);
            p.AddArc(bx, bh - 2 * r, 2 * r, 2 * r, 180, -90);
            p.AddArc(bx + bw - 2 * r, bh - 2 * r, 2 * r, 2 * r, 90, -90);
            if (e > 0.5f) p.AddArc(bx + bw, 0, 2 * e, 2 * e, 180, 90);
            else p.AddLine(bx + bw, 0, bx + bw, 0);
            p.CloseFigure();
            return p;
        }

        protected override void Build(UI ui)
        {
            var g = ui.G;
            ui.Flat = true;
            float W = ui.W;
            float sw = Val("w"), sh = Val("h");
            ui.Y = FixedLogicalHeight * S;
            if (closing && sh <= 0.04f) { BeginInvoke((Action)(() => { if (closing) { closing = false; CloseFlyout(); } })); return; }
            float bw = Math.Min(BodyW + 2 * SideRoom, BodyW * sw) * S, bh = Math.Max(2, Math.Min(BodyH + Bounce, BodyH * sh) * S);
            float bx = (W - bw) / 2;
            float ear = Ear * S * Math.Min(1, sh * 2.5f);
            var body = new RectangleF((W - BodyW * S) / 2, 0, BodyW * S, BodyH * S); // where the content sits when fully open

            drags.Clear();
            using (var shape = NotchPath(bx, bw, bh, 30 * S, ear))
            using (var f = new Fonts2(S))
            {
                // the body: pure black, or the album's colours rising from the bottom on Media
                using (var black = new SolidBrush(Color.FromArgb(255, 0, 0, 0))) g.FillPath(black, shape);
                var saved = g.Save();
                g.SetClip(shape, CombineMode.Intersect);
                if (tab == "media" && Media.Has) view.Wash(g, new RectangleF(bx, 0, bw, bh));
                // the content fades and settles in as the notch drops (and out as it folds away)
                float show = closing ? Math.Max(0, (sh - 0.35f) / 0.5f) : Math.Max(0, Math.Min(1, (sh - 0.55f) / 0.4f));
                if (show > 0.01f)
                {
                    g.TranslateTransform(0, (1 - Math.Min(1, show)) * -10 * S);
                    Header(ui, body, f);
                    var c = new RectangleF(body.X + Pad * S, HeaderH * S, body.Width - 2 * Pad * S, (BodyH - HeaderH - DotsH) * S);
                    float slide = Val("slide");
                    var inner = g.Save();
                    if (Math.Abs(slide) > 0.001f) g.TranslateTransform(slide * slideDir * 46 * S, 0);
                    Content(ui, c, f);
                    g.Restore(inner);
                    float hide = Math.Min(1, Math.Abs(slide)) * 0.9f;
                    if (hide > 0.01f)
                        using (var veil = new SolidBrush(Color.FromArgb((int)(255 * hide), 0, 0, 0)))
                            g.FillRectangle(veil, RectangleF.Inflate(c, 6 * S, 6 * S));
                    Chevrons(ui, c, f);
                    Dots(ui, new RectangleF(body.X, (BodyH - DotsH) * S - 4 * S, body.Width, DotsH * S), f);
                    if (show < 1) using (var fade = new SolidBrush(Color.FromArgb((int)(255 * (1 - Math.Min(1, show))), 0, 0, 0))) g.FillRectangle(fade, 0, -20 * S, W, bh + 40 * S);
                }
                g.Restore(saved);
                // a whisper of an edge so the black reads on dark wallpapers, brighter toward the bottom
                using (var eb = new LinearGradientBrush(new RectangleF(0, -1, W, bh + 2), Color.FromArgb(0, 255, 255, 255), Color.FromArgb(34, 255, 255, 255), 90f))
                using (var pen = new Pen(eb, 1f * S))
                    g.DrawPath(pen, shape);
            }
        }

        void Content(UI ui, RectangleF c, Fonts2 f)
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
                case "clipboard": ClipboardTab(ui, c, f); break;
                case "shelf": ShelfTab(ui, c, f); break;
                case "usage": UsageTab(ui, c, f); break;
                case "calendar": CalendarTab(ui, c, f); break;
                case "timers": TimersTab(ui, c, f); break;
                case "notes": NotesTab(ui, c, f); break;
                case "downloads": DownloadsTab(ui, c, f); break;
            }
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

        // ------------------------------------------------------------------ header: tab name · battery, date and time · sound bars

        int lastMinute = -1;

        void Header(UI ui, RectangleF body, Fonts2 f)
        {
            var g = ui.G;
            float y = 12 * S, h = 26 * S;
            lastMinute = DateTime.Now.Minute;

            // the tab's name, sliding in with the tab
            float slide = Math.Min(1, Math.Abs(Val("slide")));
            using (var tf = new Font(Fonts.UiBold, 16f * S, GraphicsUnit.Pixel))
            using (var tb = new SolidBrush(Color.FromArgb((int)(250 * (1 - slide)), 255, 255, 255)))
                ui.Text(NameOf(tab), new RectangleF(body.X + 26 * S, y + slide * 8 * S, 220 * S, h), tf, tb);

            // centre: battery ring, date, time
            var now = DateTime.Now;
            string date = now.ToString("ddd d MMM yyyy", CultureInfo.CurrentCulture), time = now.ToString(Look.Int("BarClock") == 12 ? "h:mm" : "HH:mm", CultureInfo.CurrentCulture);
            float dw = g.MeasureString(date, f.Body, 400, StringFormat.GenericTypographic).Width, tw = g.MeasureString(time, f.Bold, 200, StringFormat.GenericTypographic).Width;
            float ring = 15 * S, total = ring + 8 * S + dw + 7 * S + tw;
            float x = body.X + (body.Width - total) / 2, cy = y + h / 2;
            BatteryRing(g, new RectangleF(x, cy - ring / 2, ring, ring));
            x += ring + 8 * S;
            ui.Text(date, new RectangleF(x, y, dw + 4 * S, h), f.Body, f.White);
            x += dw + 7 * S;
            ui.Text(time, new RectangleF(x, y, tw + 6 * S, h), f.Bold, f.White);

            // right: sound bars, dancing while something plays (click for Media)
            var br = new RectangleF(body.Right - 26 * S - 24 * S, y, 24 * S, h);
            float hot = Hot("hdr:bars");
            SoundBars(g, RectangleF.Inflate(br, hot * 1.5f * S, 0), Media.Playing);
            ui.AddHit("hdr:bars", RectangleF.Inflate(br, 6 * S, 4 * S), () => Go("media"), true);
        }

        void BatteryRing(Graphics g, RectangleF r)
        {
            var ps = SystemInformation.PowerStatus;
            bool none = (ps.BatteryChargeStatus & BatteryChargeStatus.NoSystemBattery) != 0;
            float pct = none ? 1 : Math.Max(0, Math.Min(1, ps.BatteryLifePercent));
            bool charging = ps.PowerLineStatus == PowerLineStatus.Online;
            Color c = pct <= 0.2f && !charging ? Color.FromArgb(255, 255, 69, 58) : Color.FromArgb(255, 52, 199, 89);
            float th = 2.6f * S;
            var rr = RectangleF.Inflate(r, -th / 2, -th / 2);
            using (var track = new Pen(Color.FromArgb(46, 255, 255, 255), th)) g.DrawEllipse(track, rr);
            if (pct * 360 >= 1)
                using (var arc = new Pen(c, th) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(arc, rr, -90, 360 * pct);
        }

        /// <summary>Five little bars: they bounce to an invented beat while music plays and rest low when it doesn't.</summary>
        static void SoundBars(Graphics g, RectangleF r, bool playing)
        {
            double t = Environment.TickCount / 1000.0;
            float bw = r.Width / 9f, mid = r.Y + r.Height / 2, maxH = r.Height * 0.62f;
            float[] speed = { 5.1f, 7.3f, 6.2f, 8.4f, 5.7f }, phase = { 0.2f, 1.7f, 0.9f, 2.6f, 1.3f }, rest = { 0.35f, 0.6f, 0.85f, 0.55f, 0.3f };
            using (var b = new SolidBrush(Color.FromArgb(playing ? 245 : 150, 255, 255, 255)))
                for (int i = 0; i < 5; i++)
                {
                    float k = playing ? 0.3f + 0.7f * (float)(0.5 + 0.5 * Math.Sin(t * speed[i] + phase[i]) * Math.Sin(t * speed[i] * 0.37 + phase[i] * 2)) : rest[i] * 0.45f;
                    float bh = Math.Max(bw, maxH * k);
                    using (var p = Gfx.RoundRect(new RectangleF(r.X + i * 2 * bw, mid - bh / 2, bw, bh), bw / 2)) g.FillPath(b, p);
                }
        }

        // ------------------------------------------------------------------ paging: dots at the bottom, chevrons at the sides

        int slideDir = 1;

        void Go(string t)
        {
            if (t == tab) return;
            SetTab(t, true);
            Refresh2();
        }

        void Step(int d)
        {
            var shown = tabs;
            int i = Array.IndexOf(shown, tab) + d;
            if (i >= 0 && i < shown.Length) Go(shown[i]);
        }

        void Dots(UI ui, RectangleF r, Fonts2 f)
        {
            var shown = tabs;
            int n = shown.Length;
            if (n < 2) return;
            float at = Spring("dots", Math.Max(0, Array.IndexOf(shown, tab)), 300, 24); // the active pill glides between dots
            float d = 6 * S, gap = 5.5f * S, wide = 20 * S;
            var widths = new float[n];
            float total = 0;
            for (int i = 0; i < n; i++)
            {
                float k = Math.Max(0, 1 - Math.Abs(i - at));
                widths[i] = d + (wide - d) * k;
                total += widths[i] + (i > 0 ? gap : 0);
            }
            float x = r.X + (r.Width - total) / 2, cy = r.Y + r.Height / 2;
            string label = null;
            float labelX = 0;
            for (int i = 0; i < n; i++)
            {
                string id = "dot:" + shown[i];
                float k = Math.Max(0, 1 - Math.Abs(i - at)), hot = Hot(id);
                float dh = d + hot * 2 * S;
                var dr = new RectangleF(x, cy - dh / 2, widths[i], dh);
                using (var b = new SolidBrush(Color.FromArgb((int)(85 + 165 * Math.Max(k, hot * 0.7f)), 255, 255, 255)))
                using (var p = Gfx.RoundRect(dr, dh / 2)) ui.G.FillPath(b, p);
                string t = shown[i];
                ui.AddHit(id, new RectangleF(x - gap / 2, r.Y, widths[i] + gap, r.Height), () => Go(t), true);
                if (hot > 0.05f && t != tab) { label = t; labelX = x + widths[i] / 2; }
                x += widths[i] + gap;
            }
            if (label != null) // the name of the page under the mouse, just above the dots
            {
                float a = Hot("dot:" + label);
                float nw = ui.G.MeasureString(NameOf(label), f.Small).Width + 14 * S;
                var nr = new RectangleF(labelX - nw / 2, cy - 30 * S + (1 - a) * 6 * S, nw, 20 * S);
                using (var bg = new SolidBrush(Color.FromArgb((int)(240 * a), 44, 44, 48))) using (var p = Gfx.RoundRect(nr, 10 * S)) ui.G.FillPath(bg, p);
                using (var nb = new SolidBrush(Color.FromArgb((int)(240 * a), 255, 255, 255))) ui.Text(NameOf(label), nr, f.Small, nb, 1);
            }
        }

        void Chevrons(UI ui, RectangleF c, Fonts2 f)
        {
            var shown = tabs;
            int i = Array.IndexOf(shown, tab);
            float cy = c.Y + c.Height / 2;
            if (i > 0) Chevron(ui, "chev:l", new PointF(c.X - 22 * S, cy), "", () => Step(-1));
            if (i < shown.Length - 1) Chevron(ui, "chev:r", new PointF(c.Right + 22 * S, cy), "", () => Step(1));
        }

        void Chevron(UI ui, string id, PointF c, string glyph, Action act)
        {
            float k = Scale(id), r = 13 * S * k;
            var rr = new RectangleF(c.X - r, c.Y - r, 2 * r, 2 * r);
            using (var b = new SolidBrush(Color.FromArgb((int)(50 + 55 * Hot(id)), 255, 255, 255))) ui.G.FillEllipse(b, rr);
            using (var gf = new Font(Fonts.Icons, 10f * S * k, GraphicsUnit.Pixel))
            using (var wb = new SolidBrush(Color.FromArgb(235, 255, 255, 255)))
                ui.Glyph(glyph, rr, gf, wb);
            ui.AddHit(id, RectangleF.Inflate(rr, 4 * S, 4 * S), act, true);
        }

        // a horizontal wheel, or a two-finger swipe on a touchpad, pages through the tabs
        int hwheel;
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x020E /*WM_MOUSEHWHEEL*/ && Visible)
            {
                hwheel += (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
                if (Math.Abs(hwheel) >= 120) { Step(hwheel > 0 ? 1 : -1); hwheel = 0; }
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }

        // ------------------------------------------------------------------ springs: all the motion on the island

        sealed class SpringVal { public float V, Vel, Target, K, C; }
        readonly Dictionary<string, SpringVal> springs = new Dictionary<string, SpringVal>();
        readonly Timer motion = new Timer { Interval = 15 };
        readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        double lastStep;

        float Val(string id) { SpringVal s; return springs.TryGetValue(id, out s) ? s.V : 0; }

        /// <summary>Sets a spring's target and returns where it is now. k = stiffness, c = damping (less damping, more bounce).</summary>
        float Spring(string id, float target, float k = 320, float c = 24)
        {
            SpringVal s;
            if (!springs.TryGetValue(id, out s)) { springs[id] = s = new SpringVal { V = target, Target = target, K = k, C = c }; return s.V; }
            s.K = k; s.C = c;
            if (s.Target != target) { s.Target = target; if (App.SnapshotMode) { s.V = target; s.Vel = 0; } else Wake(); }
            return s.V;
        }

        /// <summary>Puts a spring somewhere without animating (it then moves back toward its target).</summary>
        void Jump(string id, float v)
        {
            SpringVal s;
            if (!springs.TryGetValue(id, out s)) springs[id] = s = new SpringVal { Target = v, K = 320, C = 24 };
            s.V = v; s.Vel = 0;
            if (s.Target != v) Wake();
        }

        /// <summary>0..1, easing in while the mouse is over the hit with this id.</summary>
        float Hot(string id) { return Math.Max(0, Math.Min(1, Spring("hot:" + id, Hover == id ? 1 : 0, 420, 26))); }

        /// <summary>A button's size: grows a touch under the mouse, squashes while pressed, and bounces back.</summary>
        float Scale(string id)
        {
            float hot = Spring("hot:" + id, Hover == id ? 1 : 0, 420, 26), press = Spring("press:" + id, Pressed == id ? 1 : 0, 700, 26);
            return 1 + 0.07f * hot - 0.12f * press;
        }

        void Wake()
        {
            if (motion.Enabled || App.SnapshotMode) return;
            lastStep = clock.Elapsed.TotalSeconds;
            motion.Start();
        }

        void Motion()
        {
            if (!Visible) { motion.Stop(); return; }
            double now = clock.Elapsed.TotalSeconds;
            float dt = (float)Math.Min(0.05, now - lastStep);
            lastStep = now;
            bool moving = false;
            foreach (var s in springs.Values)
            {
                if (s.V == s.Target && s.Vel == 0) continue;
                // small fixed steps keep the stiff springs stable
                for (float left = dt; left > 0; left -= 0.004f)
                {
                    float h = Math.Min(0.004f, left);
                    s.Vel += (s.K * (s.Target - s.V) - s.C * s.Vel) * h;
                    s.V += s.Vel * h;
                }
                if (Math.Abs(s.Target - s.V) < 0.002f && Math.Abs(s.Vel) < 0.02f) { s.V = s.Target; s.Vel = 0; }
                else moving = true;
            }
            lastFrame = Environment.TickCount;
            Refresh2();
            if (!moving) motion.Stop();
        }

        static string Span(TimeSpan t)
        {
            if (t.TotalMinutes < 1) return "<1m";
            return t.TotalHours >= 1 ? (int)t.TotalHours + "h " + t.Minutes + "m" : (int)t.TotalMinutes + "m";
        }

        void Card(UI ui, RectangleF r) { GlassShape(ui, r, 16 * S, 14, 26); }

        void GlassShape(UI ui, RectangleF r, float radius, int fill, int edge) { ui.Glass(r, radius, fill, edge); }

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
            for (int k = 0; k < list.Count && k < 5 && ay + 26 * S <= c.Bottom; k++)
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

        /// <summary>The small, faint caption at the top of a widget.</summary>
        void Caption(UI ui, RectangleF card, float ip, string text, Fonts2 f)
        {
            using (var cf = new Font(Fonts.UiBold, 10.5f * S, GraphicsUnit.Pixel))
                ui.Text(text, new RectangleF(card.X + ip, card.Y + 12 * S, card.Width - 2 * ip, 16 * S), cf, f.Faint);
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

        // ------------------------------------------------------------------ AI coding (Claude Code)

        static readonly Color Running = Color.FromArgb(255, 48, 209, 88), Waiting = Color.FromArgb(255, 255, 159, 10);

        // multi-select answers being ticked for the current question
        readonly HashSet<string> ticked = new HashSet<string>();
        string tickedFor;

        /// <summary>Claude is asking a multiple-choice question: show it, and send the pick straight back.</summary>
        void QuestionCard(UI ui, RectangleF c, Fonts2 f, ClaudeBridge.Request r)
        {
            var q = ClaudeBridge.NextQuestion(r);
            if (q == null) return;
            if (tickedFor != q.Text) { ticked.Clear(); tickedFor = q.Text; }
            var card = new RectangleF(c.X - 6 * S, c.Y - 8 * S, c.Width + 12 * S, c.Bottom - c.Y + 6 * S);
            GlassShape(ui, card, 18 * S, 16, 34);
            float ix = card.X + 14 * S, iw = card.Width - 28 * S, y = card.Y + 10 * S;

            // header chip, who's asking, and how long it's been waiting
            float hx = ix;
            if (!string.IsNullOrEmpty(q.Header))
            {
                float hw = PillWidth(ui, q.Header, f.Small);
                Pill(ui, new RectangleF(hx, y, hw, 20 * S), q.Header, Color.FromArgb(70, 255, 159, 10), f.Small, f.White);
                hx += hw + 8 * S;
            }
            ui.Text("Claude is asking  ·  " + r.Project, new RectangleF(hx, y, iw - (hx - ix) - 120 * S, 20 * S), f.Small, f.Dim);
            int idx = r.Questions.IndexOf(q);
            ui.Text((r.Questions.Count > 1 ? (idx + 1) + " of " + r.Questions.Count + "  ·  " : "") + "waiting " + Span(DateTime.Now - r.At), new RectangleF(ix, y, iw, 20 * S), f.Small, f.Faint, 2);
            y += 26 * S;
            ui.Text(q.Text, new RectangleF(ix, y, iw, 22 * S), f.Title, f.White);
            y += 30 * S;

            // the options: a 2-column grid of glass tiles (label + description)
            int n = q.Options.Count, cols = n <= 1 ? 1 : 2, rows = (n + cols - 1) / cols;
            float gap = 8 * S, bottomRow = 30 * S;
            float tileH = Math.Min(46 * S, (card.Bottom - 12 * S - bottomRow - 8 * S - y - (rows - 1) * gap) / Math.Max(1, rows));
            float tileW = (iw - (cols - 1) * gap) / cols;
            for (int i = 0; i < n; i++)
            {
                var o = q.Options[i];
                var t = new RectangleF(ix + (i % cols) * (tileW + gap), y + (i / cols) * (tileH + gap), tileW, tileH);
                string id = "qo" + i;
                bool on = q.Multi && ticked.Contains(o[0]);
                GlassShape(ui, t, 12 * S, Hover == id || on ? 34 : 14, on ? 90 : (Hover == id ? 60 : 28));
                float tx = t.X + 12 * S;
                if (q.Multi)
                {
                    var box = new RectangleF(tx, t.Y + (tileH - 16 * S) / 2, 16 * S, 16 * S);
                    TickCircle(ui, box, on, Hover == id);
                    tx = box.Right + 10 * S;
                }
                bool oneLine = string.IsNullOrEmpty(o[1]) || tileH < 38 * S;
                ui.Text(o[0], new RectangleF(tx, oneLine ? t.Y : t.Y + 5 * S, t.Right - tx - 10 * S, oneLine ? tileH : 19 * S), f.Bold, f.White);
                if (!oneLine) ui.Text(o[1], new RectangleF(tx, t.Y + 23 * S, t.Right - tx - 10 * S, 17 * S), f.Small, f.Dim);
                string label = o[0];
                ui.AddHit(id, t, () =>
                {
                    if (!q.Multi) { ClaudeBridge.Pick(r, q, label); return; }
                    if (!ticked.Remove(label)) ticked.Add(label);
                }, true);
            }

            // bottom row: leave it to the editor (e.g. to type your own answer), or send the ticked ones
            float by = card.Bottom - 12 * S - bottomRow;
            var editor = new RectangleF(ix, by, 190 * S, bottomRow);
            ui.Fill(editor, Color.FromArgb(Hover == "qEditor" ? 46 : 26, 255, 255, 255), bottomRow / 2);
            ui.Text("Dismiss  ·  answer in editor", editor, f.Small, Hover == "qEditor" ? f.White : f.Dim, 1);
            ui.AddHit("qEditor", editor, () => ClaudeBridge.Answer(r, null), true);
            if (q.Multi)
            {
                var send = new RectangleF(card.Right - 14 * S - 110 * S, by, 110 * S, bottomRow);
                bool can = ticked.Count > 0;
                ui.Fill(send, can ? Color.FromArgb(Hover == "qSend" ? 255 : 238, 255, 255, 255) : Color.FromArgb(36, 255, 255, 255), bottomRow / 2);
                using (var dark = new SolidBrush(Color.FromArgb(235, 18, 18, 22)))
                    ui.Text(can ? "Send (" + ticked.Count + ")" : "Pick one or more", send, f.Bold, can ? (Brush)dark : f.Faint, 1);
                if (can)
                {
                    var labels = new List<string>();
                    foreach (var o in q.Options) if (ticked.Contains(o[0])) labels.Add(o[0]); // in Claude's order
                    ui.AddHit("qSend", send, () => ClaudeBridge.Pick(r, q, string.Join(", ", labels.ToArray())), true);
                }
            }
            else if (r.Questions.Count > 1 || r.Picked.Count > 0)
                ui.Text("Pick an answer to continue", new RectangleF(ix, by, iw, bottomRow), f.Small, f.Faint, 2);
        }

        void AiTab(UI ui, RectangleF c, Fonts2 f)
        {
            if (ClaudeBridge.Pending.Count > 0 && ClaudeBridge.Pending[0].Questions != null)
            {
                QuestionCard(ui, c, f, ClaudeBridge.Pending[0]);
                return;
            }
            var now = DateTime.Now;
            int active = 0;
            foreach (var s in ClaudeBridge.Sessions.Values) if ((s.Status == "Running" || s.Status == "Waiting") && (now - s.Updated).TotalMinutes < 30) active++;
            float hx = c.X;
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
                    ui.G.DrawString((r.Detail != null ? r.Detail + "\n" : "") + (r.Summary ?? ""), mono, f.Dim, new RectangleF(ix, card.Y + 30 * S, iw - 376 * S, 52 * S), sf);
                using (var dark = new SolidBrush(Color.FromArgb(235, 18, 18, 22)))
                {
                    float bh = 30 * S, by = card.Bottom - bh - 10 * S;
                    var allow = new RectangleF(card.Right - 14 * S - 80 * S, by, 80 * S, bh);
                    var always = new RectangleF(allow.X - 8 * S - 108 * S, by, 108 * S, bh);
                    var deny = new RectangleF(always.X - 8 * S - 70 * S, by, 70 * S, bh);
                    // Dismiss: off the notch without answering; Claude Code asks in the editor as it normally would
                    var dismiss = new RectangleF(deny.X - 8 * S - 78 * S, by, 78 * S, bh);
                    ui.Fill(dismiss, Color.FromArgb((int)(18 + 30 * Hot("cDismiss")), 255, 255, 255), bh / 2);
                    ui.Text("Dismiss", dismiss, f.Bold, Hover == "cDismiss" ? f.White : f.Dim, 1);
                    ui.AddHit("cDismiss", dismiss, () => ClaudeBridge.Answer(r, null), true);
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
