using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Windows.Forms;

namespace Harbor
{
    /// <summary>macOS-style menu bar: blurred strip along the top of a monitor, reserved as an AppBar.</summary>
    sealed class TopBar : Form
    {
        sealed class Zone
        {
            public string Id;
            public RectangleF R;
            public Action<Zone> Click;
            public TrayIcon Tray;
            public Func<string> Tip;
        }

        public readonly Display Disp;
        readonly Surface surf = new Surface();
        readonly List<Zone> zones = new List<Zone>();
        readonly Timer minute = new Timer();
        readonly Timer tipTimer = new Timer { Interval = 600 };
        AppBar bar;
        bool blurOn, hidden;
        float S = 1;
        int H;
        string hoverId, openId;
        List<TrayIcon> overflow = new List<TrayIcon>();
        readonly List<DockBack> backs = new List<DockBack>(); // real blur under each island
        int barX, barY;

        static int net = 1; // 0 offline, 1 wifi, 2 ethernet

        public TopBar(Display d)
        {
            Disp = d;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            Bounds = new Rectangle(0, -100, 10, 10);
            minute.Tick += delegate { ArmMinute(); if (Disp.Primary) RefreshNet(); RenderBar(); };
            tipTimer.Tick += delegate { tipTimer.Stop(); ShowTip(); };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { minute.Dispose(); tipTimer.Dispose(); surf.Dispose(); }
            base.Dispose(disposing);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.Style = N.WS_POPUP;
                cp.ExStyle |= N.WS_EX_LAYERED | N.WS_EX_TOOLWINDOW | N.WS_EX_NOACTIVATE | N.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            bar = new AppBar(Handle, "top" + Disp.Index);
            bar.FullScreenChanged += delegate { App.CheckFullscreenSoon(); };
            bar.PosChanged += delegate { if (Settings.ShowTopBar && !App.SnapshotMode) Reposition(); };
            ApplyBlur();
            ArmMinute();
        }

        protected override void WndProc(ref Message m)
        {
            if (bar != null && bar.Handle(ref m)) return;
            if (m.Msg == N.WM_MOUSEACTIVATE) { m.Result = (IntPtr)N.MA_NOACTIVATE; return; }
            if (m.Msg == N.WM_DPICHANGED) { App.ScheduleRelayout(); return; }
            if (m.Msg == N.WM_POWERBROADCAST) RenderBar();
            base.WndProc(ref m);
        }

        public void ApplyBlur()
        {
            blurOn = Settings.Blur && !App.SnapshotMode;
            if (!blurOn) foreach (var b in backs) b.Conceal();
            RenderBar();
        }

        public void Unregister() { if (bar != null) bar.Unregister(); }

        bool Wanted { get { return Settings.ShowTopBar && (Disp.Primary || Settings.BarAllDisplays); } }

        public void Reposition()
        {
            S = Disp.Scale;
            H = (int)Math.Round(Math.Max(20, Math.Min(56, Look.Int("BarHeight"))) * S);
            RECT mon = N.MonitorInfo(Disp.HMon).rcMonitor;
            // screen still waking up (or a stale handle): keep the current layout and look again shortly
            if (mon.Width <= 0 || mon.Height <= 0) { App.RetryRelayoutSoon(); return; }
            if (!Wanted)
            {
                bar.Unregister();
                N.ShowWindow(Handle, N.SW_HIDE);
                return;
            }
            RECT r = mon;
            if (!App.SnapshotMode)
            {
                bar.Register();
                r = bar.SetPos(N.ABE_TOP, mon, H);
            }
            barX = r.Left;
            barY = r.Top;
            N.SetWindowPos(Handle, N.HWND_TOPMOST, r.Left, r.Top, r.Width, H, N.SWP_NOACTIVATE);
            if (!hidden && !App.SnapshotMode) N.ShowWindow(Handle, N.SW_SHOWNOACTIVATE);
            surf.Ensure(r.Width, H);
            RenderBar();
        }

        public void SetHidden(bool hide)
        {
            if (hide == hidden) return;
            hidden = hide;
            if (!Wanted) return;
            if (hide) foreach (var b in backs) b.Conceal();
            N.ShowWindow(Handle, hide ? N.SW_HIDE : N.SW_SHOWNOACTIVATE);
            if (!hide) RenderBar();
        }

        public Rectangle ScreenBounds
        {
            get { RECT r; N.GetWindowRect(Handle, out r); return new Rectangle(r.Left, r.Top, r.Width, r.Height); }
        }

        public RectangleF ZoneRect(string id)
        {
            var z = zones.Find(q => q.Id == id);
            return z == null ? RectangleF.Empty : z.R;
        }

        void ArmMinute()
        {
            var now = DateTime.Now;
            minute.Stop();
            minute.Interval = Math.Max(1000, (60 - now.Second) * 1000 - now.Millisecond + 30);
            minute.Start();
        }

        public static void RefreshNetStatic() { RefreshNet(); }

        static void RefreshNet()
        {
            net = NetState();
            Wifi.Query();
            if (Wifi.Signal >= 0) net = 1;
        }

        static int NetState()
        {
            try
            {
                if (!NetworkInterface.GetIsNetworkAvailable()) return 0;
                bool eth = false, wifi = false;
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    var t = ni.NetworkInterfaceType;
                    if (t == NetworkInterfaceType.Loopback || t == NetworkInterfaceType.Tunnel) continue;
                    string d = (ni.Description + " " + ni.Name).ToLowerInvariant();
                    if (d.Contains("virtual") || d.Contains("hyper-v") || d.Contains("vmware") || d.Contains("vpn") ||
                        d.Contains("bluetooth") || d.Contains("loopback") || d.Contains("tap-") || d.Contains("wsl")) continue;
                    if (t == NetworkInterfaceType.Wireless80211) wifi = true;
                    else if (t == NetworkInterfaceType.Ethernet || t == NetworkInterfaceType.GigabitEthernet) eth = true;
                }
                return wifi ? 1 : (eth ? 2 : 0);
            }
            catch { return 1; }
        }

        // ------------------------------------------------------------------ rendering

        Zone AddZone(string id, float x, float w, Action<Zone> click, Func<string> tip = null)
        {
            var z = new Zone { Id = id, R = new RectangleF(x, 0, w, H), Click = click, Tip = tip };
            zones.Add(z);
            return z;
        }

        static float Measure(Graphics g, string s, Font f)
        {
            return string.IsNullOrEmpty(s) ? 0 : g.MeasureString(s, f, 4000, StringFormat.GenericTypographic).Width;
        }

        bool IslandStyle { get { return Look.Get("BarStyle") == "islands"; } }
        float IY { get { return IslandStyle ? (float)Math.Round(Look.Int("BarMargin") * S) : 0; } }
        float IH { get { return H - 2 * IY; } }

        /// <summary>Draws one background block (the full bar, or an island) and keeps its blur layer underneath it.</summary>
        RectangleF Island(Graphics g, float x, float w, int idx)
        {
            var r = new RectangleF((float)Math.Round(x), IY, (float)Math.Round(w), IH);
            float rad = IslandStyle ? Math.Min(r.Height / 2, Look.Int("BarRadius") * S) : 0;
            bool blur = blurOn && Look.Bool("BarBlur");
            if (blur)
            {
                while (backs.Count <= idx) { var b = new DockBack { Below = Handle }; var hh = b.Handle; backs.Add(b); }
                if (backs[idx].BlurOk) backs[idx].Place(barX + (int)r.X, barY + (int)r.Y, (int)r.Width, (int)r.Height, (int)rad);
            }
            else if (backs.Count > idx) backs[idx].Conceal();

            Color fill = Look.BarFill;
            if (fill.A < 2) fill = Color.FromArgb(2, fill); // keep the bar clickable even when fully clear
            using (var p = rad > 0.5f ? Gfx.RoundRect(r, rad) : null)
            {
                if (IslandStyle)
                    using (var sh = Gfx.RoundRect(new RectangleF(r.X - 0.5f, r.Y + 1.2f * S, r.Width + 1, r.Height), Math.Max(0.5f, rad)))
                    using (var sb = new SolidBrush(Color.FromArgb(Theme.Dark ? 50 : 22, 0, 0, 0))) g.FillPath(sb, sh);
                using (var br = new SolidBrush(fill))
                {
                    if (p != null) g.FillPath(br, p); else g.FillRectangle(br, r);
                }
                var bc = Look.WithOpacity(Theme.Dark ? Color.White : Color.Black, "BarBorderOpacity");
                if (bc.A > 0)
                    using (var pen = new Pen(bc, 1f))
                    {
                        if (p != null)
                            using (var ip = Gfx.RoundRect(RectangleF.Inflate(r, -0.5f, -0.5f), Math.Max(0.5f, rad - 0.5f))) g.DrawPath(pen, ip);
                        else g.DrawLine(pen, r.X, r.Bottom - 0.5f, r.Right, r.Bottom - 0.5f);
                    }
            }
            return r;
        }

        public void RenderBar()
        {
            if (!IsHandleCreated || H <= 0 || surf.W <= 0) return;
            if ((hidden || !Wanted) && !App.SnapshotMode) return;
            var g = surf.G;
            int W = surf.W;
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            zones.Clear();
            var text = Look.BarText;
            float iy = IY, ih = IH;
            float fs = Look.Int("BarFont");
            using (var font = new Font(Fonts.Ui, fs * S, GraphicsUnit.Pixel))
            using (var bold = new Font(Fonts.UiBold, fs * S, GraphicsUnit.Pixel))
            using (var small = new Font(Fonts.UiBold, (fs - 0.5f) * S, GraphicsUnit.Pixel)) // one weight for every status value
            using (var iconFont = new Font(Fonts.Icons, (fs + 2f) * S, GraphicsUnit.Pixel))
            using (var tinyIcon = new Font(Fonts.Icons, (fs - 3) * S, GraphicsUnit.Pixel))
            using (var sf = new StringFormat(StringFormat.GenericTypographic) { LineAlignment = StringAlignment.Center, Alignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter })
            using (var lf = new StringFormat(sf) { Alignment = StringAlignment.Near })
            using (var brush = new SolidBrush(text))
            using (var dim = new SolidBrush(Color.FromArgb(150, text)))
            using (var faint = new SolidBrush(Color.FromArgb(80, text)))
            {
                float iw = (float)Math.Round(Look.Int("BarItemWidth") * S);
                float ip = IslandStyle ? (float)Math.Round(4 * S) : 0;
                float edge = IslandStyle ? (float)Math.Round(Math.Max(4, Look.Int("BarMargin") * 2) * S) : (float)Math.Round(6 * S);

                // ---------------- left: logo + app
                float x = edge;
                float lx0 = x;
                x += ip;
                var logo = AddZone("logo", x, iw, LogoMenu);
                x += iw;
                string appName = App.ActiveAppName();
                Zone app = null;
                if (Look.Bool("BarShowApp"))
                {
                    float aw = Math.Min(Measure(g, appName, bold) + 20 * S, 260 * S);
                    app = AddZone("app", x, aw, AppMenu);
                    x += aw;
                }
                // the app's own classic menus (File, Edit, View...) when it has them
                var menuZones = new List<KeyValuePair<Zone, string>>();
                if (Look.Bool("BarAppMenus"))
                    foreach (var mt in AppMenuTitles())
                    {
                        float mw2 = Measure(g, mt.Value, font) + 18 * S;
                        if (x + mw2 > W * 0.45f) break;
                        int mi = mt.Key;
                        var mz = AddZone("menu" + mi, x, mw2, z => OpenAppMenu(mi, z));
                        menuZones.Add(new KeyValuePair<Zone, string>(mz, mt.Value));
                        x += mw2;
                    }
                // virtual desktops
                var wsZones = new List<Zone>();
                if (Look.Bool("BarWorkspaces") && SysInfo.DesktopCount > 1)
                {
                    x += 4 * S;
                    for (int d = 0; d < SysInfo.DesktopCount && d < 10; d++)
                    {
                        int k = d;
                        float dw = (d == SysInfo.DesktopIndex ? 26 : 16) * S;
                        wsZones.Add(AddZone("ws" + d, x, dw, z => { SysInfo.SwitchDesktop(k); RenderBar(); }, () => "Desktop " + (k + 1)));
                        x += dw;
                    }
                    x += 4 * S;
                }
                x += ip;
                float leftW = x - lx0;

                // ---------------- right: tray, sound, wifi, battery, control center
                var ps = SystemInformation.PowerStatus;
                bool hasBattery = Look.Bool("BarShowBattery") && (ps.BatteryChargeStatus & BatteryChargeStatus.NoSystemBattery) == 0 &&
                                  ps.BatteryChargeStatus != BatteryChargeStatus.Unknown && ps.BatteryLifePercent <= 1f;
                bool showPct = Look.Bool("BarShowBatteryPct");
                string pct = hasBattery ? (int)Math.Round(ps.BatteryLifePercent * 100) + "%" : null;
                var icons = Settings.TrayIcons && Look.Bool("BarShowTray") && TrayService.Running ? TrayService.Visible() : new List<TrayIcon>();

                float rx1 = W - edge;
                float xr = rx1 - ip;
                Zone cc = null, bat = null, wifi = null, vol = null, tray = null;
                if (Look.Bool("BarShowCC")) { cc = AddZone("cc", xr - iw, iw, z => ControlCenter.Toggle(this, z.R), () => "Control Center"); xr -= iw; }
                bool clockRight = !IslandStyle;
                string clock = Look.Bool("BarShowClock") ? ClockText() : null;
                Zone clk = null;
                if (clock != null && clockRight)
                {
                    float cw0 = Measure(g, clock, bold) + 20 * S;
                    clk = AddZone("clock", xr - cw0, cw0, z => NotificationCenter.Toggle(this, z.R), () => DateTime.Now.ToString("D", CultureInfo.CurrentCulture));
                    // in the full bar the clock sits at the far right (macOS); move CC left of it
                    if (cc != null)
                    {
                        clk.R = new RectangleF(rx1 - ip - cw0, clk.R.Y, cw0, clk.R.Height);
                        cc.R = new RectangleF(clk.R.X - iw, cc.R.Y, iw, cc.R.Height);
                        xr = cc.R.X;
                    }
                    else xr -= cw0;
                }
                if (hasBattery)
                {
                    float bw = (showPct ? Measure(g, pct, small) + 8 * S : 0) + 38 * S;
                    bat = AddZone("battery", xr - bw, bw, z => BatteryFlyout.Toggle(this, z.R), BatteryTip);
                    xr -= bw;
                }
                if (Look.Bool("BarShowWifi")) { wifi = AddZone("net", xr - iw, iw, z => WifiFlyout.Toggle(this, z.R), NetTip); xr -= iw; }
                // Bluetooth: only while it's switched on (macOS shows it the same way)
                Zone btz = null;
                if (Look.Bool("BarShowBluetooth") && Radios.HasBt && Radios.BtOn)
                {
                    btz = AddZone("bt", xr - iw, iw, z => BluetoothFlyout.Toggle(this, z.R),
                        () => BtBattery.ConnectedName != null ? "Bluetooth  ·  " + BtBattery.ConnectedName + " connected" : "Bluetooth: On");
                    xr -= iw;
                }
                if (Look.Bool("BarShowVolume"))
                {
                    vol = AddZone("vol", xr - iw, iw, z => SoundFlyout.Toggle(this, z.R),
                        () => (Volume.Muted ? "Muted" : "Volume " + (int)Math.Round(Volume.Level * 100) + "%") + "  ·  scroll to adjust");
                    xr -= iw;
                }
                Zone hp = null;
                if (Look.Bool("BarHeadphones") && BtBattery.ConnectedLevel >= 0)
                {
                    float hw = 30 * S + Measure(g, "100%", small);
                    hp = AddZone("hp", xr - hw, hw, z => BluetoothFlyout.Toggle(this, z.R), () => BtBattery.ConnectedName + "  ·  battery " + BtBattery.ConnectedLevel + "%");
                    xr -= hw;
                }
                // widgets: CPU / RAM / network / keyboard / mic & camera
                var widgetZones = new List<KeyValuePair<Zone, string>>();
                Action<string, string, string> widget = (id, txt, tip) =>
                {
                    float ww = Measure(g, txt, small) + 16 * S;
                    var wz = AddZone(id, xr - ww, ww, z => Launcher.Run("taskmgr.exe"), () => tip);
                    widgetZones.Add(new KeyValuePair<Zone, string>(wz, txt));
                    xr -= ww;
                };
                if (Look.Bool("BarNet")) widget("wnet", "\u2193 " + SysInfo.Speed(SysInfo.DownBps), "Download " + SysInfo.Speed(SysInfo.DownBps) + "  ·  Upload " + SysInfo.Speed(SysInfo.UpBps));
                // system usage: live CPU sparkline + memory meter (click for the System panel)
                Zone sys = null;
                if (Look.Bool("BarCpu") || Look.Bool("BarRam") || (Look.Bool("BarTemp") && SysInfo.TempC > 0))
                {
                    float sw = StatsWidth(g) + 6 * S;
                    sys = AddZone("sys", xr - sw, sw, z => SystemFlyout.Toggle(this, z.R),
                        () => "CPU " + SysInfo.Cpu + "%  ·  Memory " + SysInfo.RamUsedGB.ToString("0.0") + " of " + SysInfo.RamTotalGB.ToString("0.0") + " GB" + (SysInfo.TempC > 0 ? "  ·  " + SysInfo.TempC + "\u00B0C" : ""));
                    xr -= sw;
                }
                if (Look.Bool("BarKeyboard"))
                {
                    string kb = SysInfo.KeyboardLayout();
                    float kw = Measure(g, kb, small) + 16 * S;
                    var kz = AddZone("kb", xr - kw, kw, z => N.SendWinKey(0x20), () => "Keyboard layout  ·  click to switch");
                    widgetZones.Add(new KeyValuePair<Zone, string>(kz, kb));
                    xr -= kw;
                }
                Zone privacy = null;
                if (Look.Bool("BarPrivacy") && (SysInfo.MicInUse || SysInfo.CamInUse))
                {
                    float pw = ((SysInfo.MicInUse ? 1 : 0) + (SysInfo.CamInUse ? 1 : 0)) * 12 * S + 10 * S;
                    privacy = AddZone("privacy", xr - pw, pw, null,
                        () => (SysInfo.MicInUse ? "Microphone in use" : "") + (SysInfo.MicInUse && SysInfo.CamInUse ? "  ·  " : "") + (SysInfo.CamInUse ? "Camera in use" : ""));
                    xr -= pw;
                }
                if (icons.Count > 0)
                {
                    tray = AddZone("tray", xr - iw, iw, z => TrayFlyout.Toggle(this, z.R, TrayService.Visible()), () => icons.Count + " background apps");
                    xr -= iw;
                }
                xr -= ip;
                float rightX = xr, rightW = rx1 - xr;

                // ---------------- centre: now playing (+ clock in island style)
                bool showMedia = Look.Bool("BarShowMedia") && Media.Has && Media.Playing;
                float mw = showMedia ? Math.Min(Measure(g, Media.Title, small) + 40 * S, 260 * S) : 0;
                float cw = (clock != null && !clockRight) ? Measure(g, clock, bold) + 22 * S : 0;
                float centerW = mw + cw + (mw > 0 || cw > 0 ? ip * 2 : 0);
                float cx = W / 2f - centerW / 2;
                if (cx < lx0 + leftW + 10 * S) cx = lx0 + leftW + 10 * S;
                if (centerW > 0 && cx + centerW > rightX - 10 * S)
                {
                    showMedia = false; mw = 0; centerW = cw + (cw > 0 ? ip * 2 : 0);
                    cx = Math.Min(cx, rightX - 10 * S - centerW);
                }
                float c = cx + ip;
                Zone media = null;
                if (showMedia)
                {
                    media = AddZone("media", c, mw, z => MediaFlyout.Toggle(this, z.R),
                        () => Media.Title + (string.IsNullOrEmpty(Media.Artist) ? "" : " — " + Media.Artist));
                    c += mw;
                }
                if (cw > 0)
                    clk = AddZone("clock", c, cw, z => NotificationCenter.Toggle(this, z.R), () => DateTime.Now.ToString("D", CultureInfo.CurrentCulture));

                if (tray != null && Disp.Primary)
                {
                    var tr = new Rectangle(barX + (int)tray.R.X, barY, (int)tray.R.Width, H);
                    foreach (var ic in icons) ic.ScreenRect = tr;
                    TrayService.SetBounds(new Rectangle(barX, barY, W, H), tr);
                }

                // ---------------- background(s)
                if (IslandStyle)
                {
                    Island(g, lx0, leftW, 0);
                    if (centerW > 0) Island(g, cx, centerW, 1); else if (backs.Count > 1) backs[1].Conceal();
                    Island(g, rightX, rightW, 2);
                }
                else
                {
                    Island(g, 0, W, 0);
                    for (int i = 1; i < backs.Count; i++) backs[i].Conceal();
                }

                // hover / open capsules
                foreach (var z in zones)
                {
                    bool open = z.Id == openId;
                    if (!open && z.Id != hoverId) continue;
                    float hy = iy + Math.Max(3 * S, ih * 0.14f);
                    var r = new RectangleF(z.R.X + 1 * S, hy, z.R.Width - 2 * S, ih - (hy - iy) * 2);
                    float hr = IslandStyle ? r.Height / 2 : Math.Min(r.Height / 2, 6 * S);
                    using (var path = Gfx.RoundRect(r, hr))
                    using (var br = new SolidBrush(Color.FromArgb(open ? 70 : 40, text)))
                        g.FillPath(br, path);
                }

                // ---------------- content
                var row = new Func<RectangleF, RectangleF>(rr => new RectangleF(rr.X, iy + 0.5f * S, rr.Width, ih));
                DrawWindowsLogo(g, logo.R, text);
                if (app != null) g.DrawString(appName, bold, brush, new RectangleF(app.R.X + 8 * S, iy + 0.5f * S, app.R.Width - 12 * S, ih), lf);
                foreach (var mz in menuZones) g.DrawString(mz.Value, font, brush, row(mz.Key.R), sf);
                for (int d = 0; d < wsZones.Count; d++)
                {
                    var zr = wsZones[d].R;
                    bool cur = d == SysInfo.DesktopIndex;
                    float pw = cur ? 18 * S : 7 * S, ph = 7 * S;
                    var pr = new RectangleF(zr.X + zr.Width / 2 - pw / 2, iy + ih / 2 - ph / 2, pw, ph);
                    using (var p = Gfx.RoundRect(pr, ph / 2))
                    using (var br = new SolidBrush(cur ? text : Color.FromArgb(90, text))) g.FillPath(br, p);
                }
                foreach (var wz in widgetZones) g.DrawString(wz.Value, small, dim, row(wz.Key.R), sf);
                if (privacy != null)
                {
                    float dx = privacy.R.X + 5 * S, dd = 7 * S;
                    if (SysInfo.MicInUse) { using (var b = new SolidBrush(Color.FromArgb(255, 255, 159, 10))) g.FillEllipse(b, dx + 2.5f * S, iy + ih / 2 - dd / 2, dd, dd); dx += 12 * S; }
                    if (SysInfo.CamInUse) using (var b = new SolidBrush(Color.FromArgb(255, 48, 209, 88))) g.FillEllipse(b, dx + 2.5f * S, iy + ih / 2 - dd / 2, dd, dd);
                }

                if (media != null)
                {
                    using (var mb = new SolidBrush(Theme.Accent))
                        g.DrawString("", tinyIcon, mb, new RectangleF(media.R.X + 6 * S, iy + 0.5f * S, 16 * S, ih), sf);
                    g.DrawString(Media.Title, small, dim, new RectangleF(media.R.X + 26 * S, iy + 0.5f * S, media.R.Width - 34 * S, ih), lf);
                    if (cw > 0)
                        using (var pen = new Pen(Color.FromArgb(50, text), 1))
                            g.DrawLine(pen, media.R.Right, iy + ih * 0.3f, media.R.Right, iy + ih * 0.7f);
                }
                if (clk != null) g.DrawString(clock, bold, brush, row(clk.R), sf);
                if (cc != null) DrawControlCenter(g, cc.R, text);
                if (bat != null)
                {
                    DrawBattery(g, bat.R.Right - 31 * S, ps.BatteryLifePercent, ps.PowerLineStatus == PowerLineStatus.Online, text);
                    if (showPct) g.DrawString(pct, small, brush, new RectangleF(bat.R.X + 6 * S, iy + 0.5f * S, bat.R.Width - 30 * S, ih), lf);
                }
                if (wifi != null)
                {
                    var wr = row(wifi.R);
                    if (Radios.HasWifi && !Radios.WifiOn) DrawGlyph(g, "", iconFont, faint, wr, sf);
                    else if (net == 2) DrawGlyph(g, "", iconFont, brush, wr, sf);
                    else
                    {
                        DrawGlyph(g, "", iconFont, faint, wr, sf);
                        if (net == 1) DrawGlyph(g, Wifi.BarsGlyph, iconFont, brush, wr, sf);
                    }
                }
                if (vol != null) DrawGlyph(g, Volume.Glyph, iconFont, brush, row(vol.R), sf);
                if (btz != null) DrawGlyph(g, "\uE702", iconFont, brush, row(btz.R), sf);
                if (hp != null)
                {
                    DrawGlyph(g, "\uE7F6", iconFont, brush, new RectangleF(hp.R.X + 2 * S, iy + 0.5f * S, 22 * S, ih), sf);
                    Color hc = BtBattery.ConnectedLevel <= 20 ? Color.FromArgb(255, 255, 69, 58) : text;
                    using (var hb = new SolidBrush(hc)) g.DrawString(BtBattery.ConnectedLevel + "%", small, hb, new RectangleF(hp.R.X + 24 * S, iy + 0.5f * S, hp.R.Width - 24 * S, ih), lf);
                }
                if (sys != null) DrawSystem(g, sys.R, iy, ih, small, text);
                if (tray != null)
                {
                    float d = 3.2f * S, gap = 3.6f * S, tx = tray.R.X + tray.R.Width / 2 - d * 1.5f - gap, ty = iy + ih / 2 - d / 2;
                    for (int i = 0; i < 3; i++) g.FillEllipse(brush, tx + i * (d + gap), ty, d, d);
                }
            }

            // hot corners: keep the two top corner pixels "solid" so the cursor is caught there
            using (var hb = new SolidBrush(Color.FromArgb(1, 0, 0, 0)))
            {
                if (Look.Get("CornerTL") != "none") g.FillRectangle(hb, 0, 0, 3, 3);
                if (Look.Get("CornerTR") != "none") g.FillRectangle(hb, W - 3, 0, 3, 3);
            }

            if (App.SnapshotMode) return;
            surf.Present(Handle, barX, barY);
        }

        // ---- classic app menus ----------------------------------------------------------

        static List<KeyValuePair<int, string>> AppMenuTitles()
        {
            var res = new List<KeyValuePair<int, string>>();
            IntPtr fg = Tracker.Foreground;
            if (fg == IntPtr.Zero || !N.IsWindow(fg)) return res;
            IntPtr hm = N.GetMenu(fg);
            if (hm == IntPtr.Zero) return res;
            int n = Math.Min(10, N.GetMenuItemCount(hm));
            for (int i = 0; i < n; i++)
            {
                var sb = new System.Text.StringBuilder(128);
                if (N.GetMenuString(hm, (uint)i, sb, sb.Capacity, 0x400) <= 0) continue;
                string s = sb.ToString().Replace("&&", "\u0001").Replace("&", "").Replace("\u0001", "&").Trim();
                int tab = s.IndexOf('\t');
                if (tab >= 0) s = s.Substring(0, tab);
                if (s.Length > 0 && N.GetSubMenu(hm, i) != IntPtr.Zero) res.Add(new KeyValuePair<int, string>(i, s));
            }
            return res;
        }

        /// <summary>Shows the focused app's real menu under our menu bar and forwards the choice to the app.</summary>
        void OpenAppMenu(int index, Zone z)
        {
            IntPtr target = Tracker.Foreground;
            IntPtr hm = N.GetMenu(target);
            IntPtr sub = hm == IntPtr.Zero ? IntPtr.Zero : N.GetSubMenu(hm, index);
            if (sub == IntPtr.Zero) return;
            IntPtr res;
            N.SendMessageTimeout(target, 0x117 /*WM_INITMENUPOPUP*/, sub, (IntPtr)index, 0x2, 300, out res); // lets the app update checks / greyed items
            MarkOpen(z.Id);
            var p = PointToScreen(new Point((int)z.R.X, H));
            N.SetForegroundWindow(App.Host.Handle);
            int id = N.TrackPopupMenuEx(sub, N.TPM_RETURNCMD | N.TPM_NONOTIFY, p.X, p.Y, App.Host.Handle, IntPtr.Zero);
            N.PostMessage(App.Host.Handle, N.WM_NULL, IntPtr.Zero, IntPtr.Zero);
            MarkOpen(null);
            Tracker.Activate(target);
            if (id > 0) N.PostMessage(target, 0x111 /*WM_COMMAND*/, (IntPtr)id, IntPtr.Zero);
        }

        // ---- hot corners -------------------------------------------------------------------

        Timer cornerTimer;
        string cornerPending;

        void CheckCorner(Point p)
        {
            string corner = p.X <= 2 && p.Y <= 2 ? "CornerTL" : (p.X >= surf.W - 3 && p.Y <= 2 ? "CornerTR" : null);
            if (corner == null) { if (cornerTimer != null) cornerTimer.Stop(); cornerPending = null; return; }
            if (corner == cornerPending) return;
            cornerPending = corner;
            if (cornerTimer == null)
            {
                cornerTimer = new Timer { Interval = 320 }; // a deliberate pause, not a mouse fling
                cornerTimer.Tick += delegate
                {
                    cornerTimer.Stop();
                    var c = PointToClient(Cursor.Position);
                    string still = c.X <= 2 && c.Y <= 2 ? "CornerTL" : (c.X >= surf.W - 3 && c.Y <= 2 ? "CornerTR" : null);
                    if (still != null && still == cornerPending) RunCorner(Look.Get(still), still == "CornerTR");
                };
            }
            cornerTimer.Stop();
            cornerTimer.Start();
        }

        void RunCorner(string action, bool right)
        {
            var zr = new RectangleF(right ? surf.W - 40 * S : 0, 0, 40 * S, H);
            switch (action)
            {
                case "taskview": N.SendWinKey(0x09); break;
                case "notifications": NotificationCenter.Toggle(this, ZoneRect("clock").Width > 0 ? ZoneRect("clock") : zr); break;
                case "control": ControlCenter.Toggle(this, ZoneRect("cc").Width > 0 ? ZoneRect("cc") : zr); break;
                case "desktop": N.SendWinKey((byte)'D'); break;
                case "lock": N.LockWorkStation(); break;
            }
        }

        static string WindowTitle(string appName)
        {
            var w = App.ActiveApp();
            if (w == null) return null;
            string t = N.TitleOf(w.Hwnd);
            if (string.IsNullOrEmpty(t)) return null;
            // drop the " - App Name" suffix most apps add
            foreach (var sep in new[] { " - ", " — ", " – " })
            {
                int i = t.LastIndexOf(sep, StringComparison.Ordinal);
                if (i > 0 && t.Substring(i + sep.Length).IndexOf(appName.Split(' ')[0], StringComparison.OrdinalIgnoreCase) >= 0) { t = t.Substring(0, i); break; }
            }
            if (string.Equals(t, appName, StringComparison.OrdinalIgnoreCase)) return null;
            return t.Length > 90 ? t.Substring(0, 88) + "…" : t;
        }

        RectangleF Center(RectangleF r) { return new RectangleF(r.X, r.Y + 0.5f * S, r.Width, r.Height); }
        RectangleF Inset(RectangleF r, float dx) { return new RectangleF(r.X + dx, r.Y + 0.5f * S, r.Width - dx * 2 + 4 * S, r.Height); }

        static string ClockText()
        {
            var now = DateTime.Now;
            var culture = CultureInfo.CurrentCulture;
            string mode = Look.Get("BarClock");
            bool h24 = mode == "24" || (mode != "12" && culture.DateTimeFormat.ShortTimePattern.Contains("H"));
            string time = now.ToString(h24 ? "HH:mm" : "h:mm tt", culture);
            return Look.Bool("BarShowDate") ? now.ToString("ddd d MMM", culture) + "   " + time : time;
        }

        static string NetTip()
        {
            if (Radios.HasWifi && !Radios.WifiOn) return "Wi-Fi: Off";
            if (net == 2) return "Ethernet: connected";
            if (net == 0) return "Not connected";
            return Wifi.Ssid != null ? "Wi-Fi: " + Wifi.Ssid : "Wi-Fi";
        }

        static string BatteryTip()
        {
            var ps = SystemInformation.PowerStatus;
            string s = "Battery " + (int)Math.Round(ps.BatteryLifePercent * 100) + "%";
            if (ps.PowerLineStatus == PowerLineStatus.Online) return s + " — charging";
            if (ps.BatteryLifeRemaining > 0)
            {
                var t = TimeSpan.FromSeconds(ps.BatteryLifeRemaining);
                s += string.Format(" — {0}:{1:00} left", (int)t.TotalHours, t.Minutes);
            }
            return s;
        }

        void DrawWindowsLogo(Graphics g, RectangleF z, Color c)
        {
            float size = (float)Math.Round(14 * S), gp = Math.Max(1, (float)Math.Round(1.5f * S));
            float q = (float)Math.Floor((size - gp) / 2);
            size = q * 2 + gp;
            float x0 = (float)Math.Round(z.X + (z.Width - size) / 2), y0 = (float)Math.Round((H - size) / 2f);
            var oldS = g.SmoothingMode;
            var oldP = g.PixelOffsetMode;
            g.SmoothingMode = SmoothingMode.None;
            g.PixelOffsetMode = PixelOffsetMode.None;
            using (var br = new SolidBrush(c))
            {
                g.FillRectangle(br, x0, y0, q, q);
                g.FillRectangle(br, x0 + q + gp, y0, q, q);
                g.FillRectangle(br, x0, y0 + q + gp, q, q);
                g.FillRectangle(br, x0 + q + gp, y0 + q + gp, q, q);
            }
            g.SmoothingMode = oldS;
            g.PixelOffsetMode = oldP;
        }

        void DrawControlCenter(Graphics g, RectangleF z, Color c)
        {
            float w = 15 * S, h = 6f * S, gp = 2.4f * S;
            float x0 = z.X + (z.Width - w) / 2, y0 = (H - (h * 2 + gp)) / 2f;
            using (var pen = new Pen(c, 1.5f * S))
            using (var br = new SolidBrush(c))
            {
                for (int i = 0; i < 2; i++)
                {
                    var r = new RectangleF(x0, y0 + i * (h + gp), w, h);
                    using (var p = Gfx.RoundRect(r, h / 2)) g.DrawPath(pen, p);
                    float kr = h / 2 - 1.3f * S;
                    float kx = i == 0 ? r.Right - h / 2 : r.X + h / 2;
                    g.FillEllipse(br, kx - kr, r.Y + h / 2 - kr, kr * 2, kr * 2);
                }
            }
        }

        /// <summary>Icon-font glyphs are hairline-thin next to semibold text: draw them twice, a hair apart, to match the text weight.</summary>
        void DrawGlyph(Graphics g, string glyph, Font f, Brush b, RectangleF r, StringFormat sf)
        {
            g.DrawString(glyph, f, b, r, sf);
            g.DrawString(glyph, f, b, new RectangleF(r.X + 0.45f * S, r.Y, r.Width, r.Height), sf);
        }

        /// <summary>Stats-style widgets: tiny label stacked over the value (RAM 86% · CPU 17% · 42°).</summary>
        List<KeyValuePair<string, string>> Stats()
        {
            var l = new List<KeyValuePair<string, string>>();
            if (Look.Bool("BarRam")) l.Add(new KeyValuePair<string, string>("RAM", SysInfo.Ram + "%"));
            if (Look.Bool("BarCpu")) l.Add(new KeyValuePair<string, string>("CPU", SysInfo.Cpu + "%"));
            if (Look.Bool("BarTemp") && SysInfo.TempC > 0) l.Add(new KeyValuePair<string, string>("TEMP", SysInfo.TempC + "\u00B0"));
            return l;
        }

        Font StatLabelFont() { return new Font(Fonts.UiBold, Math.Max(7f, Look.Int("BarFont") * 0.56f) * S, GraphicsUnit.Pixel); }
        Font StatValueFont() { return new Font(Fonts.UiBold, Math.Max(10f, Look.Int("BarFont") - 1f) * S, GraphicsUnit.Pixel); }

        float StatsWidth(Graphics g)
        {
            float w = 0;
            using (var lf = StatLabelFont())
            using (var vf = StatValueFont())
                foreach (var s in Stats())
                    w += Math.Max(Measure(g, s.Key, lf), Measure(g, s.Key == "TEMP" ? "100\u00B0" : "100%", vf)) + 12 * S;
            return w;
        }

        void DrawSystem(Graphics g, RectangleF z, float iy, float ih, Font small, Color text)
        {
            float x = z.X + 3 * S;
            using (var lf = StatLabelFont())
            using (var vf = StatValueFont())
            using (var sf = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap })
            using (var tb = new SolidBrush(text))
            using (var lb = new SolidBrush(Color.FromArgb(Math.Min(255, text.A * 70 / 100), text)))
            {
                float lh = lf.GetHeight(g), vh = vf.GetHeight(g) * 0.92f;
                float top = iy + (ih - (lh + vh - 1.5f * S)) / 2f;
                foreach (var s in Stats())
                {
                    float w = Math.Max(Measure(g, s.Key, lf), Measure(g, s.Key == "TEMP" ? "100\u00B0" : "100%", vf)) + 12 * S;
                    g.DrawString(s.Key, lf, lb, new RectangleF(x, top, w, lh), sf);
                    Brush vb = tb;
                    int v;
                    if (int.TryParse(s.Value.TrimEnd('%', '\u00B0'), out v) && (s.Key == "TEMP" ? v >= 85 : v >= 90))
                        vb = Brushes.OrangeRed; // only colour it when it really needs attention
                    g.DrawString(s.Value, vf, vb, new RectangleF(x, top + lh - 2f * S, w, vh + 2 * S), sf);
                    x += w;
                }
            }
        }
        void DrawBattery(Graphics g, float x0, float pct, bool charging, Color c)
        {
            float w = 23 * S, h = 11f * S;
            float y0 = (float)Math.Round((H - h) / 2f) + 0.5f;
            var body = new RectangleF(x0, y0, w, h);
            var outline = Color.FromArgb(Math.Min(255, c.A * 50 / 100), c);
            using (var pen = new Pen(outline, 1.3f * S))
            using (var p = Gfx.RoundRect(body, 3.2f * S)) g.DrawPath(pen, p);
            using (var br = new SolidBrush(outline))
            using (var p = Gfx.RoundRect(new RectangleF(body.Right + 1.2f * S, y0 + h / 2 - 2 * S, 1.8f * S, 4 * S), 0.9f * S))
                g.FillPath(br, p);
            float inset = 2f * S;
            var fill = new RectangleF(x0 + inset, y0 + inset, Math.Max(2f * S, (w - inset * 2) * Math.Max(0, Math.Min(1, pct))), h - inset * 2);
            Color fc = charging ? Color.FromArgb(255, 52, 199, 89) : (pct <= 0.2f ? Color.FromArgb(255, 255, 69, 58) : c);
            using (var br = new SolidBrush(fc))
            using (var p = Gfx.RoundRect(fill, 1.6f * S)) g.FillPath(br, p);
            if (charging)
            {
                using (var f = new Font(Fonts.Icons, 9f * S, GraphicsUnit.Pixel))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                using (var br = new SolidBrush(Color.White))
                    g.DrawString("", f, br, new RectangleF(body.X, body.Y + 0.5f * S, body.Width, body.Height), sf);
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (surf.W <= 0) return;
            IntPtr hdc = e.Graphics.GetHdc();
            surf.BlitTo(hdc);
            e.Graphics.ReleaseHdc(hdc);
        }

        // ------------------------------------------------------------------ input

        Zone ZoneAt(Point p)
        {
            foreach (var z in zones) if (z.R.Contains(p)) return z;
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            CheckCorner(e.Location);
            var z = ZoneAt(e.Location);
            string id = z == null ? null : z.Id;
            if (id != hoverId)
            {
                hoverId = id;
                Tip.Hide();
                tipTimer.Stop();
                if (z != null && z.Tip != null && !Flyout.AnyOpen) tipTimer.Start();
                RenderBar();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            tipTimer.Stop();
            Tip.Hide();
            if (hoverId != null) { hoverId = null; RenderBar(); }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            var z = ZoneAt(e.Location);
            if (z == null || z.Id != "vol") return;
            Volume.Set((Volume.Muted ? 0 : Volume.Level) + (e.Delta > 0 ? 0.04f : -0.04f));
            RenderBar();
            if (hoverId == "vol") { tipTimer.Stop(); ShowTip(); }
        }

        void ShowTip()
        {
            var z = zones.Find(q => q.Id == hoverId);
            if (z == null || z.Tip == null) return;
            string t = z.Tip();
            if (string.IsNullOrEmpty(t)) return;
            var p = PointToScreen(new Point((int)(z.R.X + z.R.Width / 2), H + (int)(6 * S)));
            Tip.Show(t, p.X, p.Y, S);
        }

        Point AnchorOf(Zone z) { return PointToScreen(new Point((int)(z.R.X + z.R.Width / 2), H)); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            tipTimer.Stop();
            Tip.Hide();
            var z = ZoneAt(e.Location);
            if (z == null) return;
            if (z.Tray != null)
            {
                if (e.Clicks == 1) TrayService.Click(z.Tray, e.Button == MouseButtons.Right, false, AnchorOf(z));
                return;
            }
            if (e.Button != MouseButtons.Left || z.Click == null) return;
            try { z.Click(z); }
            catch (Exception ex) { App.Log(ex); }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            var z = ZoneAt(e.Location);
            if (z != null && z.Tray != null && e.Button == MouseButtons.Left) TrayService.Click(z.Tray, false, true, AnchorOf(z));
        }

        public void MarkOpen(string id) { openId = id; RenderBar(); }

        // ------------------------------------------------------------------ menus

        void LogoMenu(Zone z)
        {
            var m = new PopMenu();
            m.Add("About This PC", () => Launcher.Run("ms-settings:about"));
            m.Sep();
            m.Add("System Settings…", () => Launcher.Run("ms-settings:"));
            m.Add("Microsoft Store…", () => Launcher.Run("ms-windows-store:"));
            m.Sep();
            var recent = m.Sub("Recent Items");
            recent.Add("Recent Files", () => Launcher.Run("explorer.exe", "shell:recent"));
            recent.Add("Downloads", () => Launcher.Open(ShellIcons.Downloads));
            recent.Add("Documents", () => Launcher.Open(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)));
            m.Sep();
            m.Add("Force Quit…\tCtrl Shift Esc", () => Launcher.Run("taskmgr.exe"));
            m.Sep();
            m.Add("Sleep", () => Application.SetSuspendState(PowerState.Suspend, false, false));
            m.Add("Restart…", () => { if (Confirm("Are you sure you want to restart your computer now?")) Launcher.Run("shutdown.exe", "/r /t 0"); });
            m.Add("Shut Down…", () => { if (Confirm("Are you sure you want to shut down your computer now?")) Launcher.Run("shutdown.exe", "/s /t 0"); });
            m.Sep();
            m.Add("Lock Screen\tWin L", () => N.LockWorkStation());
            m.Add("Log Out " + PopMenu.Escape(Environment.UserName) + "…", () => { if (Confirm("Are you sure you want to sign out? Unsaved work may be lost.")) Launcher.Run("shutdown.exe", "/l"); });
            m.Sep();
            m.Add("Harbor Settings…", SettingsWindow.ShowWindow);
            m.ShowBelow(this, z.R, z.Id);
        }

        public static bool Confirm(string text)
        {
            return MessageBox.Show(text, "Harbor", MessageBoxButtons.OKCancel, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2, MessageBoxOptions.DefaultDesktopOnly) == DialogResult.OK;
        }

        void AppMenu(Zone z)
        {
            var m = new PopMenu();
            var info = App.ActiveApp();
            string name = PopMenu.Escape(App.ActiveAppName());
            if (info == null)
            {
                m.Add("New File Explorer Window", () => Launcher.Run("explorer.exe"));
                m.Add("Show Desktop\tWin D", () => N.SendWinKey((byte)'D'));
            }
            else
            {
                var wins = Tracker.WindowsOfKey(info.Key);
                string path = info.Aumid != null ? @"shell:AppsFolder\" + info.Aumid : info.Exe;
                if (path != null) m.Add("New " + name + " Window\tCtrl N", () => Launcher.NewWindow(path, info.Exe, info.Hwnd));
                if (info.Exe != null) m.Add("Show in File Explorer", () => Launcher.Reveal(info.Exe));
                if (path != null)
                {
                    bool pinned = Settings.Pinned.Exists(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
                    m.Add("Keep in Dock", () => App.TogglePin(path), pinned);
                }
                m.Sep();
                m.Add("Hide " + name, () => { foreach (var w in wins) Tracker.Minimize(w.Hwnd); }, false, wins.Count > 0);
                m.Add("Hide Others", () =>
                {
                    foreach (var w in Tracker.Windows) if (w.Key != info.Key && !N.IsIconic(w.Hwnd)) Tracker.Minimize(w.Hwnd);
                });
                m.Add("Show All", () =>
                {
                    foreach (var w in Tracker.Windows) if (N.IsIconic(w.Hwnd)) N.ShowWindowAsync(w.Hwnd, N.SW_SHOWNOACTIVATE);
                });
                m.Sep();
                m.Add("Quit " + name + "\tAlt F4", () => { foreach (var w in wins) Tracker.Close(w.Hwnd); }, false, wins.Count > 0);
            }
            m.ShowBelow(this, z.R, z.Id);
        }

        void WindowMenu(Zone z)
        {
            var m = new PopMenu();
            IntPtr fg = Tracker.Foreground;
            var info = App.ActiveApp();
            bool ok = info != null && N.IsWindow(fg);
            m.Add("Minimize", () => Tracker.Minimize(fg), false, ok);
            m.Add(ok && N.IsZoomed(fg) ? "Restore" : "Zoom", () => Tracker.ToggleMaximize(fg), false, ok);
            m.Add("Tile Left", () => { Tracker.Activate(fg); N.SendWinKey(0x25); }, false, ok);
            m.Add("Tile Right", () => { Tracker.Activate(fg); N.SendWinKey(0x27); }, false, ok);
            m.Add("Close Window", () => Tracker.Close(fg), false, ok);
            if (ok)
            {
                var wins = Tracker.WindowsOfKey(info.Key);
                if (wins.Count > 1)
                {
                    m.Sep();
                    foreach (var w in wins)
                    {
                        IntPtr h = w.Hwnd;
                        m.Add(PopMenu.Escape(N.TitleOf(h)), () => Tracker.Activate(h), h == fg);
                    }
                }
            }
            m.ShowBelow(this, z.R, z.Id);
        }

        public Bitmap Snapshot()
        {
            RenderBar();
            return new Bitmap(surf.Bitmap); // a real copy: Clone() still points at the window's DIB
        }
    }

    /// <summary>Small mac-style tooltip (layered, click-through).</summary>
    static class Tip
    {
        sealed class TipForm : Form
        {
            public readonly Surface Surf = new Surface();
            public TipForm()
            {
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                AutoScaleMode = AutoScaleMode.None;
            }
            protected override bool ShowWithoutActivation { get { return true; } }
            protected override CreateParams CreateParams
            {
                get
                {
                    var cp = base.CreateParams;
                    cp.Style = N.WS_POPUP;
                    cp.ExStyle |= N.WS_EX_LAYERED | N.WS_EX_TRANSPARENT | N.WS_EX_TOOLWINDOW | N.WS_EX_NOACTIVATE | N.WS_EX_TOPMOST;
                    return cp;
                }
            }
        }

        static TipForm form;

        public static void Show(string text, int cx, int top, float s)
        {
            if (form == null) { form = new TipForm(); var h = form.Handle; }
            using (var f = new Font(Fonts.Ui, 12f * s, GraphicsUnit.Pixel))
            {
                SizeF sz;
                using (var tmp = new Bitmap(1, 1)) using (var mg = Graphics.FromImage(tmp)) sz = mg.MeasureString(text, f, 1000, StringFormat.GenericTypographic);
                int w = (int)Math.Ceiling(sz.Width + 20 * s), h = (int)Math.Ceiling(24 * s);
                var surf = form.Surf;
                surf.Ensure(w + 2, h + 2);
                var g = surf.G;
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                var r = new RectangleF(1, 1, w - 1, h - 1);
                using (var p = Gfx.RoundRect(r, 6 * s))
                {
                    using (var br = new SolidBrush(Theme.LabelBg)) g.FillPath(br, p);
                    using (var pen = new Pen(Theme.LabelBorder, 1)) g.DrawPath(pen, p);
                }
                using (var br = new SolidBrush(Theme.Text))
                using (var sf = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(text, f, br, r, sf);
                int x = cx - (w + 2) / 2;
                var scr = Screen.FromPoint(new Point(cx, top)).Bounds;
                x = Math.Max(scr.Left + 4, Math.Min(scr.Right - w - 6, x));
                surf.Present(form.Handle, x, top);
                N.ShowWindow(form.Handle, N.SW_SHOWNOACTIVATE);
            }
        }

        public static void Hide()
        {
            if (form != null && form.IsHandleCreated) N.ShowWindow(form.Handle, N.SW_HIDE);
        }
    }
}
