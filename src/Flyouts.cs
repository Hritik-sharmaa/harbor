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