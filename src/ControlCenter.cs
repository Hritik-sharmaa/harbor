using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Harbor
{
    /// <summary>
    /// macOS Tahoe-style Control Center: a 4-column grid of frosted glass modules —
    /// connectivity, now playing, focus, round quick buttons, display + sound sliders.
    /// </summary>
    sealed class ControlCenter : Flyout
    {
        static ControlCenter inst;
        internal TopBar bar;
        RectangleF zone;
        System.Collections.Generic.List<Brightness> brights = new System.Collections.Generic.List<Brightness>();

        public static void Toggle(TopBar bar, RectangleF zone)
        {
            if (inst == null) inst = new ControlCenter();
            inst.bar = bar;
            inst.zone = zone;
            inst.ShowBelow(bar, zone, "cc", 2);
        }

        public static void ToggleOnPrimary()
        {
            var d = App.PrimaryDisplay;
            if (d == null || d.Bar == null) return;
            var r = d.Bar.ZoneRect("cc");
            if (r.Width == 0) { var sb = d.Bar.ScreenBounds; r = new RectangleF(sb.Width - 200 * d.Scale, 0, 30 * d.Scale, sb.Height); }
            Toggle(d.Bar, r);
        }

        const float Cell = 70, Gap = 10, Pad = 14;

        internal ControlCenter()
        {
            LogicalWidth = (int)(Pad * 2 + Cell * 4 + Gap * 3);
            Volume.Changed += delegate { if (Visible && Dragging == null) Refresh2(); };
            Media.Changed += delegate { if (Visible) Refresh2(); };
        }

        protected override void OnOpened()
        {
            Volume.Read();
            Wifi.Query();
            // one brightness slider per screen (hardware where possible, software dimming otherwise)
            var list = new System.Collections.Generic.List<Brightness>();
            foreach (var d in App.Displays) list.Add(new Brightness(d.HMon, d.Name));
            brights = list;
            Brightness.LoadAll(list, () => { if (brights == list) Refresh2(); });
        }

        RectangleF ZoneOf(string id)
        {
            var r = bar.ZoneRect(id);
            return r.Width > 0 ? r : zone;
        }

        // ---- grid helpers ------------------------------------------------------------

        RectangleF CellRect(float top, int col, int cols, float rowsTall)
        {
            float x = (Pad + col * (Cell + Gap)) * S;
            float w = (cols * Cell + (cols - 1) * Gap) * S;
            return new RectangleF(x, top, w, rowsTall * S);
        }

        /// <summary>Frosted glass module: translucent fill, bright rim at the top, hairline below.</summary>
        void Module(UI ui, RectangleF r, bool hot, float radius) { ui.Glass(r, radius, hot ? 24 : 13, hot ? 44 : 28); }

        void RoundModule(UI ui, string id, RectangleF cell, string glyph, Action act, bool on = false)
        {
            float d = Math.Min(cell.Width, cell.Height);
            var r = new RectangleF(cell.X + (cell.Width - d) / 2, cell.Y + (cell.Height - d) / 2, d, d);
            if (on)
                using (var br = new SolidBrush(Hover == id ? Gfx.Lighten(Theme.Accent, 0.1f) : Theme.Accent)) ui.G.FillEllipse(br, r);
            else
            {
                Module(ui, r, Hover == id, d / 2);
            }
            using (var f = new Font(Fonts.Icons, 19f * S, GraphicsUnit.Pixel))
                ui.Glyph(glyph, r, f, on ? Brushes.White : (Brush)ui.TextBrush);
            ui.AddHit(id, r, act);
        }

        void Circle(UI ui, string id, PointF c, float rad, bool on, string glyph, Action act)
        {
            var r = new RectangleF(c.X - rad, c.Y - rad, rad * 2, rad * 2);
            Color bg = on ? Theme.Accent : (Theme.Dark ? Color.FromArgb(Hover == id ? 60 : 40, 255, 255, 255) : Color.FromArgb(Hover == id ? 40 : 22, 0, 0, 0));
            if (on && Hover == id) bg = Gfx.Lighten(Theme.Accent, 0.12f);
            using (var br = new SolidBrush(bg)) ui.G.FillEllipse(br, r);
            using (var f = new Font(Fonts.Icons, 14f * S, GraphicsUnit.Pixel))
                ui.Glyph(glyph, r, f, on ? Brushes.White : (Brush)ui.TextBrush);
            if (act != null) ui.AddHit(id, r, act, true);
        }

        void ConnRow(UI ui, string id, RectangleF r, string glyph, string title, string sub, bool on, Action toggle, Action open)
        {
            if (open != null)
            {
                if (Hover == id + ":open")
                    ui.Fill(new RectangleF(r.X + 5 * S, r.Y + 2 * S, r.Width - 10 * S, r.Height - 4 * S), Color.FromArgb(Theme.Dark ? 18 : 90, 255, 255, 255), 12 * S);
                ui.AddHit(id + ":open", r, open);
            }
            Circle(ui, id, new PointF(r.X + 27 * S, r.Y + r.Height / 2), 16 * S, on, glyph, toggle);
            float tx = r.X + 52 * S, tw = r.Right - tx - 8 * S;
            ui.Text(title, new RectangleF(tx, r.Y + r.Height / 2 - 16 * S, tw, 17 * S), ui.Bold, ui.TextBrush);
            ui.Text(sub, new RectangleF(tx, r.Y + r.Height / 2 + 1 * S, tw, 15 * S), ui.Small, ui.DimBrush);
        }

        void Slider(UI ui, string id, RectangleF t, float v, Action<float> set, string glyph)
        {
            v = Math.Max(0, Math.Min(1, v));
            float rad = t.Height / 2;
            ui.Fill(t, Theme.Dark ? Color.FromArgb(34, 255, 255, 255) : Color.FromArgb(30, 0, 0, 0), rad);
            if (set != null)
            {
                float fw = t.Height + (t.Width - t.Height) * v;
                ui.Fill(new RectangleF(t.X, t.Y, fw, t.Height), Color.White, rad);
                var kr = new RectangleF(t.X + fw - t.Height, t.Y, t.Height, t.Height);
                using (var pen = new Pen(Color.FromArgb(28, 0, 0, 0), 1)) ui.G.DrawEllipse(pen, RectangleF.Inflate(kr, -0.5f, -0.5f));
                var hit = ui.AddHit(id, RectangleF.Inflate(t, 0, 4 * S), null);
                hit.Slide = set;
                hit.Track = t;
            }
            using (var gb = new SolidBrush(set != null ? Color.FromArgb(150, 0, 0, 0) : Theme.TextDim))
            using (var gf = new Font(Fonts.Icons, 12f * S, GraphicsUnit.Pixel))
                ui.Glyph(glyph, new RectangleF(t.X + 1 * S, t.Y, t.Height, t.Height), gf, gb);
        }

        // ---- layout ------------------------------------------------------------------

        protected override void Build(UI ui)
        {
            float y = Pad * S;
            float big = Cell * 2 + Gap;

            // row 1: connectivity (2x2) + now playing (2x2)
            var conn = CellRect(y, 0, 2, big);
            Module(ui, conn, false, 22 * S);
            bool airplane = Radios.HasWifi && !Radios.WifiOn && (!Radios.HasBt || !Radios.BtOn);
            float rh = (conn.Height - 12 * S) / 3;
            ConnRow(ui, "wifi", new RectangleF(conn.X, conn.Y + 6 * S, conn.Width, rh), "", "Wi-Fi",
                !Radios.WifiOn ? "Off" : (Wifi.Ssid ?? "Not connected"), Radios.WifiOn && Radios.HasWifi,
                () => Radios.SetWifi(!Radios.WifiOn), () => WifiFlyout.Toggle(bar, ZoneOf("net")));
            ConnRow(ui, "bt", new RectangleF(conn.X, conn.Y + 6 * S + rh, conn.Width, rh), "", "Bluetooth",
                !Radios.BtOn ? "Off" : (BtBattery.ConnectedName != null ? BtBattery.ConnectedName + "  " + BtBattery.ConnectedLevel + "%" : "On"), Radios.BtOn,
                () => Radios.SetBluetooth(!Radios.BtOn), () => BluetoothFlyout.Toggle(bar, ZoneOf("cc")));
            ConnRow(ui, "air", new RectangleF(conn.X, conn.Y + 6 * S + rh * 2, conn.Width, rh), "", "Airplane Mode",
                airplane ? "On" : "Off", airplane,
                () => { bool on = !airplane; Radios.SetWifi(!on); Radios.SetBluetooth(!on); }, null);

            var np = CellRect(y, 2, 2, big);
            Module(ui, np, Hover == "np", 22 * S);
            ui.AddHit("np", np, () => MediaFlyout.Toggle(bar, ZoneOf("cc")));
            var art = new RectangleF(np.X + 14 * S, np.Y + 14 * S, 50 * S, 50 * S);
            MediaFlyout.DrawArt(ui, art, 10 * S);
            ui.Text(Media.Has ? Media.Title : "Not Playing", new RectangleF(np.X + 14 * S, art.Bottom + 8 * S, np.Width - 26 * S, 17 * S), ui.Bold, ui.TextBrush);
            ui.Text(Media.Has ? (string.IsNullOrEmpty(Media.Artist) ? Media.AppName : Media.Artist) : "Music & video", new RectangleF(np.X + 14 * S, art.Bottom + 25 * S, np.Width - 26 * S, 15 * S), ui.Small, ui.DimBrush);
            float bw = 30 * S, by = np.Bottom - bw - 9 * S, bcx = np.X + np.Width / 2;
            MediaBtn(ui, "prev", new RectangleF(bcx - bw * 1.6f, by, bw, bw), "", Media.Previous, ui.Icon);
            MediaBtn(ui, "play", new RectangleF(bcx - bw / 2, by, bw, bw), Media.Playing ? "" : "", Media.PlayPause, ui.BigIcon);
            MediaBtn(ui, "next", new RectangleF(bcx + bw * 0.6f, by, bw, bw), "", Media.Next, ui.Icon);
            y += big * S + Gap * S;

            // row 2: focus (2x1) + two round buttons
            var focus = CellRect(y, 0, 2, Cell);
            Module(ui, focus, Hover == "focus", 22 * S);
            ui.AddHit("focus", focus, FocusMode.Toggle, true);
            Circle(ui, "focusc", new PointF(focus.X + 30 * S, focus.Y + focus.Height / 2), 16 * S, FocusMode.On, "", null);
            ui.Text("Focus", new RectangleF(focus.X + 56 * S, focus.Y + focus.Height / 2 - 16 * S, focus.Width - 62 * S, 17 * S), ui.Bold, ui.TextBrush);
            ui.Text(FocusMode.On ? (FocusMode.Left != null ? "On · " + FocusMode.LeftText() + " left" : "On") : "Off", new RectangleF(focus.X + 56 * S, focus.Y + focus.Height / 2 + 1 * S, focus.Width - 62 * S, 15 * S), ui.Small, ui.DimBrush);
            RoundModule(ui, "dark", CellRect(y, 2, 1, Cell), "\uE793", ToggleDarkMode, Theme.UserDark);
            RoundModule(ui, "cast", CellRect(y, 3, 1, Cell), "", () => N.SendWinKey((byte)'K'));
            y += Cell * S + Gap * S;

            // row 3: brightness — one slider per screen
            int nb = Math.Max(1, brights.Count);
            float dispH = 34 + nb * 34;
            var disp = CellRect(y, 0, 4, dispH);
            Module(ui, disp, false, 22 * S);
            ui.Text("Display", new RectangleF(disp.X + 16 * S, disp.Y + 9 * S, 200 * S, 17 * S), ui.Bold, ui.TextBrush);
            float sy = disp.Y + 32 * S;
            if (brights.Count == 0)
                ui.Text("Reading displays…", new RectangleF(disp.X + 16 * S, sy, disp.Width - 32 * S, 24 * S), ui.Small, ui.DimBrush);
            for (int i = 0; i < brights.Count; i++)
            {
                var b = brights[i];
                var dt = new RectangleF(disp.X + 14 * S, sy, disp.Width - 28 * S, 24 * S);
                if (b.Value >= 0) Slider(ui, "bri" + i, dt, b.Value / 100f, v => b.Set((int)Math.Round(v * 100)), "\uE706");
                else
                {
                    Slider(ui, "bri" + i, dt, 0, null, "\uE706");
                    ui.Text("Reading display…", new RectangleF(dt.X + 30 * S, dt.Y, dt.Width - 36 * S, dt.Height), ui.Small, ui.DimBrush);
                }
                if (brights.Count > 1 && b.Value >= 0)
                    using (var lb = new SolidBrush(Color.FromArgb(140, 0, 0, 0)))
                        ui.Text((b.Kind == "Built-in display" ? "Built-in" : "Display " + (i + 1)), new RectangleF(dt.X, dt.Y, dt.Width - 12 * S, dt.Height), ui.Small, lb, 2);
                sy += 34 * S;
            }
            y += dispH * S + Gap * S;

            // row 4: sound
            var snd = CellRect(y, 0, 4, 66);
            Module(ui, snd, false, 22 * S);
            ui.Text("Sound", new RectangleF(snd.X + 16 * S, snd.Y + 9 * S, 80 * S, 17 * S), ui.Bold, ui.TextBrush);
            ui.Text(Volume.DeviceName, new RectangleF(snd.X + 70 * S, snd.Y + 9 * S, snd.Width - 112 * S, 17 * S), ui.Small, ui.DimBrush);
            var more = new RectangleF(snd.Right - 34 * S, snd.Y + 6 * S, 24 * S, 22 * S);
            if (Hover == "sndmore") ui.Fill(more, Theme.TileHover, 8 * S);
            using (var cf = new Font(Fonts.Icons, 9f * S, GraphicsUnit.Pixel)) ui.Glyph("", more, cf, ui.DimBrush);
            ui.AddHit("sndmore", more, () => SoundFlyout.Toggle(bar, ZoneOf("vol")));
            var st = new RectangleF(snd.X + 14 * S, snd.Y + 32 * S, snd.Width - 28 * S, 24 * S);
            Slider(ui, "vol", st, Volume.Muted ? 0 : Volume.Level, v => Volume.Set(v), Volume.Glyph);
            ui.AddHit("vol:icon", new RectangleF(st.X, st.Y, st.Height, st.Height), Volume.ToggleMute, true);
            y += 66 * S + Gap * S;

            // row 5: round quick buttons
            RoundModule(ui, "shot", CellRect(y, 0, 1, Cell), "", () => N.SendWinKey((byte)'S', true));
            RoundModule(ui, "lock", CellRect(y, 1, 1, Cell), "", () => N.LockWorkStation());
            RoundModule(ui, "set", CellRect(y, 2, 1, Cell), "", () => Launcher.Run("ms-settings:"));
            RoundModule(ui, "power", CellRect(y, 3, 1, Cell), "", PowerMenu);
            y += Cell * S;

            // battery caption
            var ps = SystemInformation.PowerStatus;
            bool hasBattery = (ps.BatteryChargeStatus & BatteryChargeStatus.NoSystemBattery) == 0 &&
                              ps.BatteryChargeStatus != BatteryChargeStatus.Unknown && ps.BatteryLifePercent <= 1f;
            if (hasBattery)
            {
                y += 6 * S;
                string bt = "Battery " + (int)Math.Round(ps.BatteryLifePercent * 100) + "%" + (ps.PowerLineStatus == PowerLineStatus.Online ? "  ·  Charging" : "");
                var br = new RectangleF(Pad * S, y, ui.W - Pad * 2 * S, 24 * S);
                if (Hover == "batt") ui.Fill(br, Theme.TileHover, 8 * S);
                ui.Text(bt, br, ui.Small, ui.DimBrush, 1);
                ui.AddHit("batt", br, () => BatteryFlyout.Toggle(bar, ZoneOf("battery")));
                y += 24 * S;
            }
            ui.Y = y + 6 * S;
        }

        /// <summary>Switches Windows (apps + system) between light and dark, like the macOS Dark Mode button.</summary>
        internal static void ToggleDarkMode()
        {
            bool toDark = !Theme.UserDark;
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", true))
                {
                    if (k != null)
                    {
                        k.SetValue("AppsUseLightTheme", toDark ? 0 : 1, Microsoft.Win32.RegistryValueKind.DWord);
                        k.SetValue("SystemUsesLightTheme", toDark ? 0 : 1, Microsoft.Win32.RegistryValueKind.DWord);
                    }
                }
                IntPtr s = System.Runtime.InteropServices.Marshal.StringToHGlobalUni("ImmersiveColorSet"), r;
                N.SendMessageTimeout(N.HWND_BROADCAST, N.WM_SETTINGCHANGE, IntPtr.Zero, s, 0x2, 300, out r);
                System.Runtime.InteropServices.Marshal.FreeHGlobal(s);
            }
            catch (Exception ex) { App.Log(ex); }
            if (Settings.ThemeMode != "auto") { Settings.ThemeMode = "auto"; Settings.Save(); }
            App.OnThemeChanged();
        }

        void MediaBtn(UI ui, string id, RectangleF r, string glyph, Action act, Font f)
        {
            if (Hover == id) using (var br = new SolidBrush(Theme.TileHover)) ui.G.FillEllipse(br, r);
            ui.Glyph(glyph, r, f, ui.TextBrush);
            ui.AddHit(id, r, act, true);
        }

        void PowerMenu()
        {
            var m = new PopMenu();
            m.Add("Sleep", () => Application.SetSuspendState(PowerState.Suspend, false, false));
            m.Add("Restart…", () => { if (TopBar.Confirm("Are you sure you want to restart your computer now?")) Launcher.Run("shutdown.exe", "/r /t 0"); });
            m.Add("Shut Down…", () => { if (TopBar.Confirm("Are you sure you want to shut down your computer now?")) Launcher.Run("shutdown.exe", "/s /t 0"); });
            m.Sep();
            m.Add("Lock Screen", () => N.LockWorkStation());
            m.Add("Log Out…", () => { if (TopBar.Confirm("Are you sure you want to sign out?")) Launcher.Run("shutdown.exe", "/l"); });
            // its own id: Control Center has just closed under this click, and reopening "cc" right away counts as a toggle
            m.ShowBelow(bar, ZoneOf("cc"), "ccpower");
        }
    }
}
