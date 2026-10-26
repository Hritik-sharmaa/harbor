using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace Harbor
{
    sealed class WifiFlyout : Flyout
    {
        static WifiFlyout inst;
        public static void Toggle(TopBar bar, RectangleF zone) { (inst ?? (inst = new WifiFlyout())).ShowBelow(bar, zone, "net", 2); }

        readonly Timer poll = new Timer { Interval = 2500 };
        string expanded, status;

        internal WifiFlyout()
        {
            LogicalWidth = 310;
            poll.Tick += delegate { Wifi.LoadNetworks(); Refresh2(); };
        }

        protected override void OnOpened()
        {
            expanded = status = null;
            Fields.Clear();
            FocusField = null;
            Wifi.LoadNetworks();
            Wifi.Scan();
            poll.Start();
        }

        protected override void OnClosedFlyout()
        {
            poll.Stop();
            Fields.Clear(); // never keep a typed password around
        }

        protected override void Build(UI ui)
        {
            ui.Header("Wi-Fi", Radios.HasWifi ? Radios.WifiOn : (bool?)null, on => { Radios.SetWifi(on); status = null; });
            if (Radios.HasWifi && !Radios.WifiOn)
            {
                ui.Info("Wi-Fi is turned off");
            }
            else
            {
                var nets = Wifi.Networks;
                var conn = nets.Find(n => n.Connected);
                if (conn != null)
                {
                    ui.Section("Connected");
                    Row(ui, conn);
                }
                ui.Section(conn != null ? "Other Networks" : "Networks");
                int shown = 0;
                foreach (var n in nets)
                {
                    if (n.Connected) continue;
                    if (++shown > 14) break;
                    Row(ui, n);
                }
                if (shown == 0) ui.Info("Looking for networks…");
            }
            ui.Separator();
            ui.Item("hidden", "Other Network Settings…", () => Launcher.Run("ms-settings:network-wifi"));
        }

        void Row(UI ui, Wifi.Net n)
        {
            string sub = n.Connected ? "Connected" : (n.HasProfile ? "Saved" : (n.Secure ? "Secured" : "Open"));
            if (expanded == n.Ssid && status != null && !n.Connected) sub = status;
            ui.Item("n:" + n.Ssid, n.Ssid, () => Click(n), Wifi.SignalGlyph(n.Signal), sub: sub, circle: true,
                accent: n.Connected, keep: true);
            if (expanded != n.Ssid) return;
            if (n.Connected)
            {
                ui.Button("disc", "Disconnect", () => { Wifi.Disconnect(); expanded = null; Later(); }, false, true);
            }
            else if (n.Secure && !n.HasProfile)
            {
                ui.TextField("pw", "Password", true, () => Join(n));
                ui.Button("join", "Join", () => Join(n), true, true);
            }
        }

        void Click(Wifi.Net n)
        {
            if (expanded == n.Ssid) { expanded = null; return; }
            expanded = n.Ssid;
            status = null;
            Fields.Remove("pw");
            if (!n.Connected && (n.HasProfile || !n.Secure)) Join(n);
            else if (!n.Connected) FocusField = "pw";
        }

        void Join(Wifi.Net n)
        {
            string err = Wifi.Connect(n, Field("pw"));
            Fields.Remove("pw");
            if (err != null) { status = err; return; }
            status = "Connecting…";
            Later();
        }

        void Later()
        {
            var t = new Timer { Interval = 2500 };
            t.Tick += delegate { t.Stop(); t.Dispose(); Wifi.LoadNetworks(); Wifi.Query(); App.RenderBars(); Refresh2(); };
            t.Start();
        }
    }

    sealed class BluetoothFlyout : Flyout
    {
        static BluetoothFlyout inst;
        public static void Toggle(TopBar bar, RectangleF zone) { (inst ?? (inst = new BluetoothFlyout())).ShowBelow(bar, zone, "bt", 2); }

        internal BluetoothFlyout() { LogicalWidth = 300; }

        protected override void OnOpened() { BtDevices.Refresh(Refresh2); BtBattery.Refresh(); }

        protected override void Build(UI ui)
        {
            ui.Header("Bluetooth", Radios.HasBt ? Radios.BtOn : (bool?)null, on => { Radios.SetBluetooth(on); });
            if (!Radios.HasBt) ui.Info("No Bluetooth adapter found");
            else if (!Radios.BtOn) ui.Info("Bluetooth is turned off");
            else
            {
                ui.Section("My Devices");
                if (BtDevices.List.Count == 0) ui.Info(BtDevices.Loading ? "Loading…" : "No paired devices");
                foreach (var d in BtDevices.List)
                    ui.Item("d:" + d.Id, d.Name, () => Launcher.Run("ms-settings:bluetooth"), "",
                        sub: d.Connected ? (BtBattery.LevelFor(d.Name) >= 0 ? "Connected  ·  battery " + BtBattery.LevelFor(d.Name) + "%" : "Connected") : "Not connected", circle: true, accent: d.Connected);
            }
            ui.Separator();
            ui.Item("add", "Add a Device…", () => Launcher.Run("ms-settings:connecteddevices"));
            ui.Item("set", "Bluetooth Settings…", () => Launcher.Run("ms-settings:bluetooth"));
        }
    }

    sealed class SoundFlyout : Flyout
    {
        static SoundFlyout inst;
        public static void Toggle(TopBar bar, RectangleF zone) { (inst ?? (inst = new SoundFlyout())).ShowBelow(bar, zone, "vol", 2); }

        List<Volume.AppSession> sessions = new List<Volume.AppSession>();
        readonly Dictionary<string, Bitmap> icons = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);

        internal SoundFlyout()
        {
            LogicalWidth = 320;
            Volume.Changed += delegate { if (Visible && Dragging == null) Refresh2(); };
        }

        protected override void OnOpened()
        {
            Volume.Init(); // follows the current default device (headphones plugged in, etc.)
            sessions = Volume.Sessions();
        }

        protected override void OnClosedFlyout()
        {
            foreach (var s in sessions) try { System.Runtime.InteropServices.Marshal.ReleaseComObject(s.Vol); } catch { }
            sessions.Clear();
            foreach (var b in icons.Values) b.Dispose();
            icons.Clear();
        }

        Bitmap IconFor(string exe)
        {
            if (exe == null) return null;
            Bitmap b;
            if (icons.TryGetValue(exe, out b)) return b;
            var arr = ShellIcons.LoadIcons(exe, (int)Math.Round(16 * S));
            b = arr != null ? arr[0] : null;
            icons[exe] = b;
            return b;
        }

        protected override void Build(UI ui)
        {
            ui.Header("Sound", null, null, string.IsNullOrEmpty(Volume.DeviceName) ? null : Volume.DeviceName);
            ui.Slider("master", Volume.Muted ? 0 : Volume.Level, v => Volume.Set(v), Volume.Glyph, Volume.ToggleMute);
            if (sessions.Count > 0)
            {
                ui.Section("Apps");
                foreach (var a in sessions)
                {
                    var s = a;
                    ui.Slider("app" + s.Pid, s.Muted ? 0 : s.Level, v => Volume.SetApp(s, v), null, null, s.Name, IconFor(s.Exe));
                }
            }
            ui.Separator();
            ui.Item("out", "Output Device…", () => Launcher.Run("ms-settings:sound"));
            ui.Item("mix", "Volume Mixer…", () => Launcher.Run("ms-settings:apps-volume"));
        }
    }

    sealed class CalendarFlyout : Flyout
    {
        static CalendarFlyout inst;
        public static void Toggle(TopBar bar, RectangleF zone) { (inst ?? (inst = new CalendarFlyout())).ShowBelow(bar, zone, "clock", 2); }

        DateTime month;

        internal CalendarFlyout() { LogicalWidth = 300; }

        protected override void OnOpened()
        {
            var t = DateTime.Today;
            month = new DateTime(t.Year, t.Month, 1);
        }

        protected override void Build(UI ui)
        {
            var culture = CultureInfo.CurrentCulture;
            var today = DateTime.Today;
            var top = ui.Custom(58);
            ui.Text(today.ToString("dddd", culture), new RectangleF(top.X + 10 * S, top.Y + 6 * S, top.Width, 18 * S), ui.Small, ui.DimBrush);
            ui.Text(today.ToString("d MMMM yyyy", culture), new RectangleF(top.X + 10 * S, top.Y + 24 * S, top.Width, 26 * S), ui.Title, ui.TextBrush);

            var nav = ui.Custom(32);
            ui.Text(month.ToString("MMMM yyyy", culture), new RectangleF(nav.X + 10 * S, nav.Y, nav.Width - 80 * S, nav.Height), ui.Bold, ui.TextBrush);
            var prev = new RectangleF(nav.Right - 64 * S, nav.Y + 2 * S, 28 * S, 28 * S);
            var next = new RectangleF(nav.Right - 32 * S, nav.Y + 2 * S, 28 * S, 28 * S);
            if (IsHot("prev")) ui.Fill(prev, Theme.TileHover, 7 * S);
            if (IsHot("next")) ui.Fill(next, Theme.TileHover, 7 * S);
            using (var f = new Font(Fonts.Icons, 11f * S, GraphicsUnit.Pixel))
            {
                ui.Glyph("", prev, f, ui.TextBrush);
                ui.Glyph("", next, f, ui.TextBrush);
            }
            ui.AddHit("prev", prev, () => month = month.AddMonths(-1), true);
            ui.AddHit("next", next, () => month = month.AddMonths(1), true);
            if (month.Year != today.Year || month.Month != today.Month)
            {
                var tr = new RectangleF(nav.Right - 116 * S, nav.Y + 4 * S, 48 * S, 24 * S);
                if (IsHot("today")) ui.Fill(tr, Theme.TileHover, 7 * S);
                ui.Text("Today", tr, ui.Small, ui.TextBrush, 1);
                ui.AddHit("today", tr, () => month = new DateTime(today.Year, today.Month, 1), true);
            }

            var first = culture.DateTimeFormat.FirstDayOfWeek;
            var head = ui.Custom(24);
            float cw = head.Width / 7;
            for (int i = 0; i < 7; i++)
            {
                var dow = (DayOfWeek)(((int)first + i) % 7);
                string n = culture.DateTimeFormat.GetShortestDayName(dow);
                ui.Text(n, new RectangleF(head.X + cw * i, head.Y, cw, head.Height), ui.Small, ui.DimBrush, 1);
            }
            int offset = ((int)month.DayOfWeek - (int)first + 7) % 7;
            var start = month.AddDays(-offset);
            for (int row = 0; row < 6; row++)
            {
                var rr = ui.Custom(32);
                for (int c = 0; c < 7; c++)
                {
                    var d = start.AddDays(row * 7 + c);
                    var cell = new RectangleF(rr.X + cw * c, rr.Y, cw, rr.Height);
                    var dot = new RectangleF(cell.X + cell.Width / 2 - 14 * S, cell.Y + 2 * S, 28 * S, 28 * S);
                    bool isToday = d == today;
                    if (isToday) using (var br = new SolidBrush(Theme.Accent)) ui.G.FillEllipse(br, dot);
                    Brush b = isToday ? Brushes.White : (d.Month == month.Month ? (Brush)ui.TextBrush : ui.DimBrush);
                    ui.Text(d.Day.ToString(culture), dot, isToday ? ui.Bold : ui.Body, b, 1);
                }
            }
            ui.Gap(4);
            ui.Separator();
            ui.Item("notif", "Notifications", () => N.SendWinKey((byte)'N'), "");
            ui.Item("dt", "Date & Time Settings…", () => Launcher.Run("ms-settings:dateandtime"), "");
        }

        bool IsHot(string id) { return Hover == id; }
    }

    sealed class BatteryFlyout : Flyout
    {
        static BatteryFlyout inst;
        public static void Toggle(TopBar bar, RectangleF zone) { (inst ?? (inst = new BatteryFlyout())).ShowBelow(bar, zone, "battery", 2); }

        internal BatteryFlyout() { LogicalWidth = 280; }

        protected override void Build(UI ui)
        {
            var ps = SystemInformation.PowerStatus;
            bool charging = ps.PowerLineStatus == PowerLineStatus.Online;
            int pct = (int)Math.Round(ps.BatteryLifePercent * 100);
            ui.Header("Battery", null, null, charging ? "Power Source: Power Adapter" : "Power Source: Battery");
            var r = ui.Custom(56);
            var body = new RectangleF(r.X + 12 * S, r.Y + 12 * S, 54 * S, 26 * S);
            using (var pen = new Pen(Theme.TextDim, 1.5f * S)) using (var p = Gfx.RoundRect(body, 6 * S)) ui.G.DrawPath(pen, p);
            ui.Fill(new RectangleF(body.Right + 2 * S, body.Y + 8 * S, 3 * S, 10 * S), Theme.TextDim, 1.5f * S);
            var fill = new RectangleF(body.X + 3 * S, body.Y + 3 * S, Math.Max(4 * S, (body.Width - 6 * S) * ps.BatteryLifePercent), body.Height - 6 * S);
            ui.Fill(fill, charging ? Color.FromArgb(52, 199, 89) : (pct <= 20 ? Color.FromArgb(255, 69, 58) : Theme.Text), 3.5f * S);
            using (var f = new Font(Fonts.UiBold, 24f * S, GraphicsUnit.Pixel))
                ui.Text(pct + "%", new RectangleF(body.Right + 18 * S, r.Y + 4 * S, 120 * S, 40 * S), f, ui.TextBrush);
            string state = charging ? (pct >= 100 ? "Fully charged" : "Charging")
                : (ps.BatteryLifeRemaining > 0 ? string.Format("{0} h {1} min remaining", ps.BatteryLifeRemaining / 3600, ps.BatteryLifeRemaining / 60 % 60) : "On battery");
            ui.Info(state);
            ui.Separator();
            ui.Item("saver", "Battery Settings…", () => Launcher.Run("ms-settings:batterysaver"), "");
            ui.Item("power", "Power & Sleep…", () => Launcher.Run("ms-settings:powersleep"), "");
        }
    }

    /// <summary>
    /// Now Playing, Apple Music style: the panel takes its colours from the cover art, a seekable progress bar,
    /// big controls, and synced lyrics (from LRCLIB) that follow the song.
    /// </summary>
    sealed class MediaFlyout : Flyout
    {
        static MediaFlyout inst;
        public static void Toggle(TopBar bar, RectangleF zone) { (inst ?? (inst = new MediaFlyout())).ShowBelow(bar, zone, "media", 1); }

        const float Margin = 18, ArtSize = 92, LyricsBoxH = 212;

        readonly Timer tick = new Timer { Interval = 33 };
        int lastFrame;
        float lyricScroll, lyricTarget;
        bool snapLyrics = true;
        float scrub = -1;               // 0..1 while dragging the progress bar
        string lastSong;
        Bitmap ambient;                 // cover art shrunk to a few pixels: stretched, it's a soft colour wash
        Bitmap ambientSrc;

        internal MediaFlyout()
        {
            LogicalWidth = 340;
            Media.Changed += delegate { if (Visible) Refresh2(); };
            Lyrics.Changed += delegate { snapLyrics = true; if (Visible) Refresh2(); };
            tick.Tick += delegate { Tick(); };
        }

        protected override void OnOpened() { Media.WantTimeline = true; Media.RefreshTimeline(); snapLyrics = true; tick.Start(); }
        protected override void OnClosedFlyout() { Media.WantTimeline = false; tick.Stop(); }

        /// <summary>Redraws only while something moves: the progress bar a few times a second, lyrics at 30 fps while scrolling.</summary>
        void Tick()
        {
            if (!Visible) { tick.Stop(); return; }
            bool scrolling = Math.Abs(lyricTarget - lyricScroll) > 0.5f;
            int now = Environment.TickCount;
            if (scrolling || (Media.Playing && now - lastFrame > 250) || Dragging != null) { lastFrame = now; Refresh2(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (Dragging == "seek" && scrub >= 0)
            {
                Media.Seek(TimeSpan.FromTicks((long)(Media.Duration.Ticks * scrub)));
                scrub = -1;
                snapLyrics = true;
            }
            base.OnMouseUp(e);
        }

        static string Clock(TimeSpan t)
        {
            return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : ((int)t.TotalMinutes) + ":" + t.Seconds.ToString("00");
        }

        protected override void Build(UI ui)
        {
            string song = (Media.Artist ?? "") + "|" + (Media.Title ?? "");
            if (song != lastSong) { lastSong = song; snapLyrics = true; }

            Lyrics.Result lyr = null;
            var state = Media.Has ? Lyrics.For(out lyr) : Lyrics.State.Off;
            bool timeline = Media.Has && Media.Duration > TimeSpan.Zero;
            float W = ui.W, m = Margin * S;

            // ---- measure first, so the colour wash can cover the whole panel
            float h = Margin + ArtSize + 18 + (timeline ? 34 : 0) + 64;
            float plainH = 0;
            if (state == Lyrics.State.Ready && lyr != null && lyr.Plain != null)
                using (var f = new Font(Fonts.UiBold, 14f * S, GraphicsUnit.Pixel))
                    plainH = ui.G.MeasureString(lyr.Plain, f, (int)(W - 2 * m)).Height / S;
            if (state != Lyrics.State.Off)
                h += 8 + (lyr != null && lyr.Synced != null ? LyricsBoxH : (lyr != null && lyr.Plain != null ? plainH + 26 : 48)) + 22;
            h += 6;
            var all = new RectangleF(0, 0, W, h * S);
            DrawBackdrop(ui.G, all);

            var white = Color.FromArgb(250, 255, 255, 255);
            var dim = Color.FromArgb(165, 255, 255, 255);
            var faint = Color.FromArgb(110, 255, 255, 255);

            // ---- header: cover, title, artist, app, lyrics switch
            float y = Margin * S;
            var art = new RectangleF(m, y, ArtSize * S, ArtSize * S);
            for (int i = 1; i <= 6; i++)
                using (var sp = Gfx.RoundRect(new RectangleF(art.X - i * S, art.Y - i * S + 4 * S, art.Width + 2 * i * S, art.Height + 2 * i * S), 12 * S + i * S))
                using (var sb = new SolidBrush(Color.FromArgb(14, 0, 0, 0)))
                    ui.G.FillPath(sb, sp);
            DrawArt(ui, art, 12 * S);

            float tx = art.Right + 14 * S, tw = W - m - tx;
            using (var titleF = new Font(Fonts.UiBold, 16f * S, GraphicsUnit.Pixel))
            using (var bodyF = new Font(Fonts.Ui, 13.5f * S, GraphicsUnit.Pixel))
            using (var smallF = new Font(Fonts.Ui, 11.5f * S, GraphicsUnit.Pixel))
            using (var wb = new SolidBrush(white))
            using (var db = new SolidBrush(dim))
            using (var fb = new SolidBrush(faint))
            {
                ui.Text(Media.Has ? Media.Title : "Nothing playing", new RectangleF(tx, art.Y + 10 * S, tw, 22 * S), titleF, wb);
                ui.Text(Media.Has ? Media.Artist : "Play something in Spotify, a browser or any media app", new RectangleF(tx, art.Y + 34 * S, tw, 20 * S), bodyF, db);
                if (Media.Has)
                {
                    ui.Text(Media.AppName, new RectangleF(tx, art.Bottom - 22 * S, tw - 70 * S, 18 * S), smallF, fb);
                    // lyrics on/off
                    bool on = Look.Bool("Lyrics");
                    var pill = new RectangleF(W - m - 58 * S, art.Bottom - 24 * S, 58 * S, 22 * S);
                    ui.Fill(pill, on ? Color.FromArgb(Hover == "lyr" ? 255 : 235, 255, 255, 255) : Color.FromArgb(Hover == "lyr" ? 70 : 45, 255, 255, 255), 11 * S);
                    using (var pb = new SolidBrush(on ? Color.FromArgb(230, 20, 20, 24) : white))
                        ui.Text("Lyrics", pill, smallF, pb, 1);
                    ui.AddHit("lyr", pill, () => { Look.Set("Lyrics", !Look.Bool("Lyrics")); Settings.Save(); snapLyrics = true; }, true);
                }
                y = art.Bottom + 18 * S;

                // ---- progress
                if (timeline)
                {
                    var bar = new RectangleF(m, y + 4 * S, W - 2 * m, 5 * S);
                    float v = scrub >= 0 ? scrub : (float)(Media.Position.TotalSeconds / Media.Duration.TotalSeconds);
                    v = Math.Max(0, Math.Min(1, v));
                    bool hot = Hover == "seek" || Dragging == "seek";
                    if (hot) bar = RectangleF.Inflate(bar, 0, 1.5f * S);
                    ui.Fill(bar, Color.FromArgb(55, 255, 255, 255), bar.Height / 2);
                    ui.Fill(new RectangleF(bar.X, bar.Y, Math.Max(bar.Height, bar.Width * v), bar.Height), Color.FromArgb(hot ? 255 : 225, 255, 255, 255), bar.Height / 2);
                    var hit = ui.AddHit("seek", new RectangleF(bar.X - 6 * S, bar.Y - 9 * S, bar.Width + 12 * S, bar.Height + 18 * S), null);
                    hit.Track = new RectangleF(bar.X, bar.Y, bar.Width, 0);
                    hit.Slide = f => scrub = f;
                    var at = TimeSpan.FromTicks((long)(Media.Duration.Ticks * v));
                    ui.Text(Clock(at), new RectangleF(m, y + 14 * S, 80 * S, 16 * S), smallF, fb);
                    ui.Text("-" + Clock(Media.Duration - at), new RectangleF(W - m - 80 * S, y + 14 * S, 80 * S, 16 * S), smallF, fb, 2);
                    y += 34 * S;
                }

                // ---- controls
                float cx = W / 2, cy = y + 30 * S;
                var play = new RectangleF(cx - 26 * S, cy - 26 * S, 52 * S, 52 * S);
                using (var pb = new SolidBrush(Color.FromArgb(Hover == "play" ? 255 : 240, 255, 255, 255))) ui.G.FillEllipse(pb, play);
                using (var gf = new Font(Fonts.Icons, 20f * S, GraphicsUnit.Pixel))
                using (var gb = new SolidBrush(Color.FromArgb(235, 18, 18, 22)))
                    ui.Glyph(Media.Playing ? "" : "", new RectangleF(play.X + (Media.Playing ? 0 : 1.5f * S), play.Y, play.Width, play.Height), gf, gb);
                ui.AddHit("play", play, Media.PlayPause, true);
                using (var gf = new Font(Fonts.Icons, 17f * S, GraphicsUnit.Pixel))
                {
                    RoundButton(ui, "prev", new PointF(cx - 74 * S, cy), "", Media.Previous, gf, wb);
                    RoundButton(ui, "next", new PointF(cx + 74 * S, cy), "", Media.Next, gf, wb);
                }
                y += 64 * S;

                // ---- lyrics
                if (state != Lyrics.State.Off)
                {
                    y += 8 * S;
                    if (state == Lyrics.State.Loading) { Note(ui, y, "Looking for lyrics…", bodyF, db); y += 48 * S; }
                    else if (lyr == null || lyr.NotFound) { Note(ui, y, "No lyrics found for this song", bodyF, db); y += 48 * S; }
                    else if (lyr.Failed) { Note(ui, y, "Couldn't reach the lyrics service", bodyF, db); y += 48 * S; }
                    else if (lyr.Instrumental) { Note(ui, y, "♪  Instrumental", bodyF, db); y += 48 * S; }
                    else if (lyr.Synced != null) { DrawSynced(ui, new RectangleF(m - 4 * S, y, W - 2 * m + 8 * S, LyricsBoxH * S), lyr.Synced); y += LyricsBoxH * S; }
                    else
                    {
                        ui.Text("These lyrics aren't time-synced", new RectangleF(m, y, W - 2 * m, 18 * S), smallF, fb);
                        using (var f = new Font(Fonts.UiBold, 14f * S, GraphicsUnit.Pixel))
                        using (var sf = new StringFormat())
                            ui.G.DrawString(lyr.Plain, f, db, new RectangleF(m, y + 24 * S, W - 2 * m, plainH * S + 4 * S), sf);
                        y += (plainH + 26) * S;
                    }
                    ui.Text("Lyrics from LRCLIB", new RectangleF(m, y + 4 * S, W - 2 * m, 16 * S), smallF, fb, 1);
                    y += 22 * S;
                }
            }
            ui.Y = y - 2 * S; // the flyout adds 8 px below the content: 6 px of padding in all
        }

        void Note(UI ui, float y, string text, Font f, Brush b)
        {
            ui.Text(text, new RectangleF(Margin * S, y, ui.W - 2 * Margin * S, 40 * S), f, b, 1);
        }

        void RoundButton(UI ui, string id, PointF c, string glyph, Action act, Font f, Brush b)
        {
            var r = new RectangleF(c.X - 21 * S, c.Y - 21 * S, 42 * S, 42 * S);
            if (Hover == id) using (var hb = new SolidBrush(Color.FromArgb(40, 255, 255, 255))) ui.G.FillEllipse(hb, r);
            ui.Glyph(glyph, r, f, b);
            ui.AddHit(id, r, act, true);
        }

        /// <summary>Karaoke view: the current line bright in the middle, neighbours fading out, smooth scroll between lines. Click a line to jump there.</summary>
        void DrawSynced(UI ui, RectangleF box, List<Lyrics.Line> lines)
        {
            var now = Media.Position + TimeSpan.FromMilliseconds(150); // land slightly early, like Apple Music
            int cur = -1;
            for (int i = 0; i < lines.Count; i++) { if (lines[i].At <= now) cur = i; else break; }

            using (var f = new Font(Fonts.UiBold, 17f * S, GraphicsUnit.Pixel))
            using (var sf = new StringFormat())
            {
                float lineGap = 10 * S, wrapW = box.Width - 16 * S;
                // positions of every line in "lyrics space"
                var tops = new float[lines.Count];
                var heights = new float[lines.Count];
                float yy = 0;
                for (int i = 0; i < lines.Count; i++)
                {
                    string t = string.IsNullOrWhiteSpace(lines[i].Text) ? "♪" : lines[i].Text;
                    heights[i] = ui.G.MeasureString(t, f, (int)wrapW, sf).Height;
                    tops[i] = yy;
                    yy += heights[i] + lineGap;
                }
                int focus = Math.Max(0, cur);
                lyricTarget = tops.Length > 0 ? tops[focus] + heights[focus] / 2 - box.Height * 0.4f : 0;
                if (snapLyrics) { lyricScroll = lyricTarget; snapLyrics = false; }
                else lyricScroll += (lyricTarget - lyricScroll) * 0.18f;

                var clip = ui.G.Clip;
                ui.G.SetClip(box, CombineMode.Intersect);
                for (int i = 0; i < lines.Count; i++)
                {
                    float top = box.Y + tops[i] - lyricScroll;
                    if (top + heights[i] < box.Y || top > box.Bottom) continue;
                    var r = new RectangleF(box.X + 8 * S, top, wrapW, heights[i]);
                    // brightness: current line full, others fade with distance and near the edges of the box
                    float mid = top + heights[i] / 2, edge = Math.Min(mid - box.Y, box.Bottom - mid) / (box.Height * 0.32f);
                    float fade = Math.Max(0.15f, Math.Min(1, edge));
                    int a = i == cur ? 255 : (i < cur ? 70 : 120);
                    string id = "ly" + i;
                    if (Hover == id && i != cur) { ui.Fill(RectangleF.Inflate(r, 6 * S, 2 * S), Color.FromArgb(28, 255, 255, 255), 8 * S); a = Math.Max(a, 190); }
                    using (var b = new SolidBrush(Color.FromArgb((int)(a * fade), 255, 255, 255)))
                        ui.G.DrawString(string.IsNullOrWhiteSpace(lines[i].Text) ? "♪" : lines[i].Text, f, b, r, sf);
                    var at = lines[i].At;
                    ui.AddHit(id, RectangleF.Intersect(r, box), () => { Media.Seek(at); snapLyrics = true; }, true);
                }
                ui.G.Clip = clip;
            }
        }

        /// <summary>The cover art blurred into a colour wash behind everything, darkened so white text always reads.</summary>
        void DrawBackdrop(Graphics g, RectangleF r)
        {
            if (Media.Art != null && Media.Has)
            {
                if (ambientSrc != Media.Art)
                {
                    if (ambient != null) ambient.Dispose();
                    ambient = Gfx.Scale(Media.Art, 6);
                    ambientSrc = Media.Art;
                }
                var mode = g.InterpolationMode;
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                using (var ia = new System.Drawing.Imaging.ImageAttributes())
                {
                    ia.SetWrapMode(WrapMode.TileFlipXY); // no dark seams at the edges
                    g.DrawImage(ambient, Rectangle.Round(RectangleF.Inflate(r, r.Width * 0.15f, r.Height * 0.15f)), 0, 0, ambient.Width, ambient.Height, GraphicsUnit.Pixel, ia);
                }
                g.InterpolationMode = mode;
            }
            else
                using (var br = new LinearGradientBrush(r, Color.FromArgb(255, 70, 60, 130), Color.FromArgb(255, 150, 60, 110), 60f)) g.FillRectangle(br, r);
            using (var shade = new LinearGradientBrush(r, Color.FromArgb(120, 0, 0, 0), Color.FromArgb(175, 0, 0, 0), 90f))
                g.FillRectangle(shade, r);
        }

        public static void DrawArt(UI ui, RectangleF art, float radius)
        {
            if (Media.Art != null)
            {
                using (var p = Gfx.RoundRect(art, radius))
                {
                    var clip = ui.G.Clip;
                    ui.G.SetClip(p, CombineMode.Intersect);
                    ui.G.DrawImage(Media.Art, art);
                    ui.G.Clip = clip;
                }
            }
            else
            {
                using (var br = new LinearGradientBrush(art, Color.FromArgb(255, 255, 94, 98), Color.FromArgb(255, 191, 90, 242), 45f))
                using (var p = Gfx.RoundRect(art, radius)) ui.G.FillPath(br, p);
                using (var f = new Font(Fonts.Icons, art.Height * 0.4f, GraphicsUnit.Pixel))
                    ui.Glyph("", art, f, Brushes.White);
            }
        }
    }

    /// <summary>Tray icons that don't fit in the menu bar.</summary>
    sealed class TrayFlyout : Flyout
    {
        static TrayFlyout inst;
        List<TrayIcon> icons = new List<TrayIcon>();

        public static void Toggle(TopBar bar, RectangleF zone, List<TrayIcon> overflow)
        {
            if (inst == null) inst = new TrayFlyout();
            inst.icons = overflow;
            inst.ShowBelow(bar, zone, "trayMore", 2);
        }

        internal TrayFlyout() { LogicalWidth = 244; }

        protected override void Build(UI ui)
        {
            ui.Header("Background Apps", null, null, "Right-click an icon for its menu");
            ui.Gap(2);
            int cols = 5;
            float cell = (LogicalWidth * S - ui.Pad * 2) / cols;
            for (int i = 0; i < icons.Count; i += cols)
            {
                var row = ui.Custom(cell / S);
                for (int c = 0; c < cols && i + c < icons.Count; c++)
                {
                    var ic = icons[i + c];
                    var r = new RectangleF(row.X + c * cell, row.Y, cell, cell);
                    string id = "t" + (i + c);
                    if (Hover == id) ui.Fill(RectangleF.Inflate(r, -2 * S, -2 * S), Theme.TileHover, 7 * S);
                    float isz = 20 * S;
                    if (ic.Image != null) ui.G.DrawImage(ic.Image, new RectangleF(r.X + (cell - isz) / 2, r.Y + (cell - isz) / 2, isz, isz));
                    var screen = PointToScreen(new Point((int)(r.X + r.Width / 2), (int)(r.Y + r.Height / 2)));
                    ui.AddHit(id, r, () => TrayService.Click(ic, LastButton == MouseButtons.Right, false, screen));
                }
            }
            if (icons.Count == 0) ui.Info("No more icons");
        }
    }

    /// <summary>macOS-style stack: the newest files of a docked folder (Downloads...) in a grid.</summary>
    sealed class StackFlyout : Flyout
    {
        static StackFlyout inst;
        string folder, title;
        List<System.IO.FileSystemInfo> entries = new List<System.IO.FileSystemInfo>();
        readonly Dictionary<string, Bitmap> icons = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);

        public static void ShowFor(Dock d, DockItem it)
        {
            if (inst == null) inst = new StackFlyout();
            inst.folder = ShellIcons.ParsingName(it.Path);
            inst.title = it.Name;
            d.OpenBeside(inst, it);
        }

        internal StackFlyout() { LogicalWidth = 360; }

        protected override void OnOpened()
        {
            entries = new List<System.IO.FileSystemInfo>();
            try
            {
                var di = new System.IO.DirectoryInfo(folder);
                foreach (var e in di.GetFileSystemInfos())
                {
                    if ((e.Attributes & (System.IO.FileAttributes.Hidden | System.IO.FileAttributes.System)) != 0) continue;
                    entries.Add(e);
                }
                entries.Sort((a, b) => b.LastWriteTime.CompareTo(a.LastWriteTime));
                if (entries.Count > 16) entries.RemoveRange(16, entries.Count - 16);
            }
            catch { }
        }

        protected override void OnClosedFlyout()
        {
            foreach (var b in icons.Values) if (b != null) b.Dispose();
            icons.Clear();
        }

        Bitmap IconFor(string p)
        {
            Bitmap b;
            if (icons.TryGetValue(p, out b)) return b;
            var arr = ShellIcons.LoadIcons(p, (int)Math.Round(44 * S));
            b = arr != null ? arr[0] : null;
            icons[p] = b;
            return b;
        }

        protected override void Build(UI ui)
        {
            ui.Header(title, null, null, entries.Count == 0 ? "Empty" : "Most recent first");
            int cols = 4;
            float cell = (ui.Right - ui.Pad) / cols, cellH = 92 * S;
            for (int i = 0; i < entries.Count; i += cols)
            {
                var row = ui.Custom(cellH / S);
                for (int c = 0; c < cols && i + c < entries.Count; c++)
                {
                    var e = entries[i + c];
                    var r = new RectangleF(row.X + c * cell, row.Y, cell, cellH);
                    string id = "e" + (i + c);
                    if (Hover == id) ui.Fill(RectangleF.Inflate(r, -3 * S, -2 * S), Theme.TileHover, 10 * S);
                    var ic = IconFor(e.FullName);
                    float isz = 44 * S;
                    if (ic != null) ui.G.DrawImage(ic, new RectangleF(r.X + (cell - isz) / 2, r.Y + 8 * S, isz, isz));
                    using (var sf = new StringFormat { Alignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter })
                        ui.G.DrawString(e.Name, ui.Small, ui.TextBrush, new RectangleF(r.X + 4 * S, r.Y + 56 * S, cell - 8 * S, 32 * S), sf);
                    string path = e.FullName;
                    paths[id] = path;
                    ui.AddHit(id, r, () => { if (LastButton == MouseButtons.Right) Launcher.Reveal(path); else Launcher.Open(path); });
                }
            }
            ui.Gap(4);
            ui.Button("openfolder", "Open in File Explorer", () => Launcher.Open(folder));
            ui.Text("Drag a file out to drop it into a browser, chat or folder", new RectangleF(ui.Pad, ui.Y, ui.Right - ui.Pad, 20 * S), ui.Small, ui.DimBrush, 1);
            ui.Y += 22 * S;
        }

        // ---- drag files out (browser uploads, chat, mail, folders)
        readonly Dictionary<string, string> paths = new Dictionary<string, string>();
        string downPath;
        Point downAt;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            string p;
            downPath = e.Button == MouseButtons.Left && Pressed != null && paths.TryGetValue(Pressed, out p) ? p : null;
            downAt = e.Location;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (downPath != null && e.Button == MouseButtons.Left)
            {
                var d = SystemInformation.DragSize;
                if (Math.Abs(e.X - downAt.X) > d.Width || Math.Abs(e.Y - downAt.Y) > d.Height)
                {
                    string path = downPath;
                    downPath = null;
                    Pressed = null;
                    HoldOpen = true; // dropping onto another app activates it; don't vanish mid-drag
                    try { ShellDrag.Drag(Handle, path); }
                    finally { HoldOpen = false; }
                    CloseFlyout();
                    return;
                }
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            downPath = null;
            base.OnMouseUp(e);
        }
    }
}