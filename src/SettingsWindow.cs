using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace Harbor
{
    /// <summary>The Harbor app window: every look & behaviour setting, applied live.</summary>
    sealed class SettingsWindow : Flyout
    {
        static SettingsWindow inst;

        public static void ShowWindow()
        {
            if (inst == null) inst = new SettingsWindow();
            if (inst.Visible) { inst.Activate(); N.SetForegroundWindow(inst.Handle); return; }
            var d = App.PrimaryDisplay;
            inst.Open(Screen.PrimaryScreen.WorkingArea, false, 1, d != null ? d.Scale : 1f, null);
        }

        static readonly string[] pages = { "General", "Dock", "Menu Bar", "Appearance", "Themes", "Pinned Apps", "About" };
        static readonly string[] pageGlyphs = { "\uE9E9", "\uEBC6", "\uE7F4", "\uE706", "\uE790", "\uE718", "\uE946" };
        int page;
        Bitmap appIcon;
        readonly Dictionary<string, Bitmap> icons = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);

        const float SidebarW = 210;

        internal int Page { get { return page; } set { page = value; } }

        internal SettingsWindow()
        {
            WindowMode = true;
            LogicalWidth = 820;
            FixedLogicalHeight = 640;
            Text = "Harbor";
            ShowInTaskbar = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        }

        protected override void OnOpened()
        {
            if (appIcon == null)
            {
                var arr = ShellIcons.LoadIcons(Application.ExecutablePath, (int)Math.Round(64 * S));
                appIcon = arr != null ? arr[0] : null;
            }
        }

        protected override void OnClosedFlyout()
        {
            foreach (var b in icons.Values) if (b != null) b.Dispose();
            icons.Clear();
        }

        protected override bool IsDragArea(Point p) { return p.Y < 52 * S && p.X < Width - 100 * S; }

        Bitmap IconFor(string path)
        {
            Bitmap b;
            if (icons.TryGetValue(path, out b)) return b;
            var arr = ShellIcons.LoadIcons(path, (int)Math.Round(28 * S));
            b = arr != null ? arr[0] : null;
            icons[path] = b;
            return b;
        }

        // ---- helpers bound to settings ------------------------------------------------

        static void Live() { App.ApplyLive(); }
        static void Full() { App.ApplySettings(); }

        static void Int(UI ui, string key, string label, int min, int max, string unit)
        {
            ui.ValueSlider(key, label, Look.Int(key), min, max, unit, v => { Look.Set(key, v); Live(); });
        }

        static void Toggle(UI ui, string key, string label, string sub = null)
        {
            ui.ToggleRow(key, label, Look.Bool(key), v => { Look.Set(key, v); Live(); }, sub);
        }

        static void Colour(UI ui, string key, string label, bool allowAuto = true)
        {
            ui.ColorPicker(key, label, Look.Get(key), v => { Look.Set(key, v); Live(); }, allowAuto);
        }

        // ---- grouped cards (macOS System Settings style) ------------------------------
        // Rows are drawn immediately, so each card's background is painted from the previous frame's
        // measurements; when the layout changes we simply render once more.
        List<RectangleF> cards = new List<RectangleF>(), building = new List<RectangleF>();
        float cardTop = -1;

        void Card(UI ui, string title)
        {
            EndCard(ui);
            ui.Gap(10);
            ui.Text(title, new RectangleF(ui.Pad + 6 * S, ui.Y, 400 * S, 20 * S), ui.Bold, ui.DimBrush);
            ui.Y += 26 * S;
            cardTop = ui.Y;
            ui.Gap(4);
        }

        void EndCard(UI ui)
        {
            if (cardTop < 0) return;
            ui.Gap(4);
            building.Add(new RectangleF(ui.Pad - 2 * S, cardTop, ui.Right - ui.Pad + 4 * S, ui.Y - cardTop));
            cardTop = -1;
        }

        public const string Version = "1.0.0";
        public const string RepoUrl = "https://github.com/hritik-sharmaa/harbor";
        static readonly string[] pageInfo =
        {
            "Startup, displays, shortcuts and backup",
            "Size, spacing, colours, icons and behaviour of the dock",
            "Style, colours and what shows in the menu bar",
            "Light or dark, accent colour and blur",
            "One-click looks, wallpaper colours and theme files",
            "The apps that stay in your dock",
            "",
        };

        // ---- layout --------------------------------------------------------------------

        protected override void Build(UI ui)
        {
            var g = ui.G;
            float sw = SidebarW * S, W = LogicalWidth * S;

            // ---- page content (scrolls)
            ui.Pad = sw + 28 * S;
            ui.Right = W - 34 * S;
            ui.Y = 26 * S;
            // card backgrounds measured on the previous frame
            foreach (var c in cards)
            {
                using (var p = Gfx.RoundRect(c, 12 * S))
                {
                    using (var br = new SolidBrush(Theme.Dark ? Color.FromArgb(18, 255, 255, 255) : Color.FromArgb(255, 255, 255, 255))) g.FillPath(br, p);
                    using (var pen = new Pen(Theme.Dark ? Color.FromArgb(22, 255, 255, 255) : Color.FromArgb(20, 0, 0, 0), 1)) g.DrawPath(pen, p);
                }
            }
            building = new List<RectangleF>();
            cardTop = -1;
            if (page != 6)
            {
                using (var hf = new Font(Fonts.UiBold, 24f * S, GraphicsUnit.Pixel))
                    ui.Text(pages[page], new RectangleF(ui.Pad + 6 * S, ui.Y, 400 * S, 34 * S), hf, ui.TextBrush);
                ui.Text(pageInfo[page], new RectangleF(ui.Pad + 7 * S, ui.Y + 34 * S, 520 * S, 18 * S), ui.Small, ui.DimBrush);
                ui.Y += 60 * S;
            }
            switch (page)
            {
                case 0: General(ui); break;
                case 1: DockPage(ui); break;
                case 2: BarPage(ui); break;
                case 3: Appearance(ui); break;
                case 4: Themes(ui); break;
                case 5: Pinned(ui); break;
                case 6: About(ui); break;
            }
            EndCard(ui);
            ui.Y += 20 * S;
            if (!SameCards(cards, building)) { cards = building; BeginInvoke((Action)Refresh2); }
            float contentEnd = ui.Y;

            // ---- sidebar + window buttons (fixed, drawn on top of the scrolled content)
            var saved = g.Transform;
            g.ResetTransform();
            using (var br = new SolidBrush(Theme.Dark ? Color.FromArgb(255, 26, 26, 30) : Color.FromArgb(255, 230, 230, 236)))
                g.FillRectangle(br, 0, 0, sw, FixedLogicalHeight * S + 2);
            if (appIcon != null) g.DrawImage(appIcon, new RectangleF(20 * S, 22 * S, 34 * S, 34 * S));
            using (var tf = new Font(Fonts.UiBold, 17f * S, GraphicsUnit.Pixel))
                ui.Text("Harbor", new RectangleF(62 * S, 19 * S, sw - 70 * S, 24 * S), tf, ui.TextBrush);
            ui.Text("Version " + Version, new RectangleF(63 * S, 40 * S, sw - 70 * S, 16 * S), ui.Small, ui.DimBrush);
            float y = 78 * S;
            for (int i = 0; i < pages.Length; i++)
            {
                var r = new RectangleF(10 * S, y, sw - 20 * S, 36 * S);
                string id = "nav" + i;
                if (i == page) ui.Fill(r, Theme.Accent, 9 * S);
                else if (Hover == id) ui.Fill(r, Theme.TileHover, 9 * S);
                Brush fg = i == page ? Brushes.White : (Brush)ui.TextBrush;
                ui.Glyph(pageGlyphs[i], new RectangleF(r.X + 8 * S, r.Y, 24 * S, r.Height), ui.Icon, fg);
                ui.Text(pages[i], new RectangleF(r.X + 42 * S, r.Y, r.Width - 46 * S, r.Height), ui.Body, fg);
                int k = i;
                ui.AddHit(id, r, () => { page = k; Hover = null; ResetScroll(); }, true).Fixed = true;
                y += 40 * S;
            }
            var gh = new RectangleF(10 * S, FixedLogicalHeight * S - 50 * S, sw - 20 * S, 32 * S);
            if (Hover == "gh") ui.Fill(gh, Theme.TileHover, 9 * S);
            using (var gf = new Font(Fonts.Icons, 13f * S, GraphicsUnit.Pixel)) ui.Glyph("\uE71B", new RectangleF(gh.X + 8 * S, gh.Y, 24 * S, gh.Height), gf, ui.DimBrush);
            ui.Text("Harbor on GitHub", new RectangleF(gh.X + 42 * S, gh.Y, gh.Width - 46 * S, gh.Height), ui.Small, ui.DimBrush);
            ui.AddHit("gh", gh, () => Launcher.Run(RepoUrl), true).Fixed = true;

            var close = new RectangleF(W - 46 * S, 0, 46 * S, 38 * S);
            var min = new RectangleF(W - 92 * S, 0, 46 * S, 38 * S);
            if (Hover == "close") using (var b = new SolidBrush(Color.FromArgb(255, 232, 17, 35))) g.FillRectangle(b, close);
            if (Hover == "min") using (var b = new SolidBrush(Theme.TileHover)) g.FillRectangle(b, min);
            using (var cf = new Font(Fonts.Icons, 10f * S, GraphicsUnit.Pixel))
            {
                ui.Glyph("", close, cf, Hover == "close" ? Brushes.White : (Brush)ui.TextBrush);
                ui.Glyph("", min, cf, ui.TextBrush);
            }
            ui.AddHit("close", close, CloseFlyout).Fixed = true;
            ui.AddHit("min", min, () => N.ShowWindow(Handle, N.SW_MINIMIZE), true).Fixed = true;
            g.Transform = saved;
            ui.Y = contentEnd;
        }

        static bool SameCards(List<RectangleF> a, List<RectangleF> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (Math.Abs(a[i].Y - b[i].Y) > 0.5f || Math.Abs(a[i].Height - b[i].Height) > 0.5f || Math.Abs(a[i].Width - b[i].Width) > 0.5f) return false;
            return true;
        }

        // ---- pages -----------------------------------------------------------------------

        void General(UI ui)
        {
            Card(ui, "Startup");
            ui.ToggleRow("login", "Launch at login", Settings.LaunchAtLogin, v => { Settings.LaunchAtLogin = v; Full(); }, "Start Harbor automatically when you sign in");
            ui.ToggleRow("taskbar", "Hide Windows taskbar", Settings.HideTaskbar, v => { Settings.HideTaskbar = v; Full(); }, "Use the dock and menu bar instead of the taskbar");
            Card(ui, "Displays");
            ui.ToggleRow("alldock", "Dock on all displays", Settings.DockAllDisplays, v => { Settings.DockAllDisplays = v; Full(); });
            ui.ToggleRow("allbar", "Menu bar on all displays", Settings.BarAllDisplays, v => { Settings.BarAllDisplays = v; Full(); });
            ui.ToggleRow("bar", "Show menu bar", Settings.ShowTopBar, v => { Settings.ShowTopBar = v; Full(); });
            Card(ui, "Menu bar extras");
            ui.ToggleRow("tray", "Background apps (tray) in the menu bar", Settings.TrayIcons, v => { Settings.TrayIcons = v; Full(); });
            Toggle(ui, "NcWidgets", "Widgets in Notification Center", "Calendar, now playing, CPU / memory / battery");

            Card(ui, "Hot corners");
            string[] acts = { "none", "taskview", "notifications", "control", "desktop", "lock" };
            string[] names = { "Off", "Task View", "Notifs", "Control", "Desktop", "Lock" };
            ui.Info("Top-left corner");
            ui.Choice("ctl", names, Math.Max(0, Array.IndexOf(acts, Look.Get("CornerTL"))), i => { Look.Set("CornerTL", acts[i]); Live(); });
            ui.Info("Top-right corner");
            ui.Choice("ctr", names, Math.Max(0, Array.IndexOf(acts, Look.Get("CornerTR"))), i => { Look.Set("CornerTR", acts[i]); Live(); });

            Card(ui, "Keyboard shortcuts");
            Toggle(ui, "Hotkeys", "Global shortcuts", "Alt+Win+N notifications  ·  Alt+Win+C Control Center  ·  Alt+Win+D dock auto-hide");

            Card(ui, "Backup");
            ui.Button("export", "Export Settings…", ExportSettings, false, true);
            ui.Button("import", "Import Settings…", ImportSettings, false, true);
        }

        void ExportSettings()
        {
            Settings.Save();
            using (var dlg = new SaveFileDialog { Title = "Export Harbor settings", FileName = "Harbor settings.ini", Filter = "Harbor settings (*.ini)|*.ini" })
                if (dlg.ShowDialog(this) == DialogResult.OK) File.Copy(Path.Combine(Settings.Dir, "settings.ini"), dlg.FileName, true);
        }

        void ImportSettings()
        {
            using (var dlg = new OpenFileDialog { Title = "Import Harbor settings", Filter = "Harbor settings (*.ini)|*.ini" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                File.Copy(dlg.FileName, Path.Combine(Settings.Dir, "settings.ini"), true);
                App.Restart();
            }
        }

        void DockPage(UI ui)
        {
            Card(ui, "Position");
            string[] posv = { "bottom", "left", "right" };
            ui.Choice("dpos", new[] { "Bottom", "Left", "Right" }, Math.Max(0, Array.IndexOf(posv, Look.Get("DockPosition"))),
                i => { Look.Set("DockPosition", posv[i]); Full(); });

            Card(ui, "Size & spacing");
            ui.ValueSlider("isize", "Icon size", Settings.IconSize, 24, 96, " px", v => { Settings.IconSize = v; Live(); });
            Int(ui, "DockSpacing", "Space between icons", 0, 30, " px");
            Int(ui, "DockPadH", "Padding left & right", 0, 40, " px");
            Int(ui, "DockPadV", "Padding top & bottom", 0, 30, " px");
            Int(ui, "DockRadius", "Corner roundness", 0, 48, " px");
            Int(ui, "DockMargin", "Distance from screen edge", 0, 60, " px");

            Card(ui, "Background");
            Toggle(ui, "DockBlur", "Blur behind the dock", "Frosted glass effect");
            Colour(ui, "DockColor", "Colour");
            Int(ui, "DockOpacity", "Colour opacity", 0, 100, " %");

            Card(ui, "Border");
            Colour(ui, "DockBorderColor", "Border colour");
            Int(ui, "DockBorderOpacity", "Border opacity", 0, 100, " %");
            Int(ui, "DockBorderWidth", "Border width", 0, 6, " px");

            Card(ui, "Icons & effects");
            string[] styles = { "whitesur", "original", "tiles" };
            ui.Choice("istyle", new[] { "macOS (WhiteSur)", "Original", "Glass" }, Math.Max(0, Array.IndexOf(styles, Settings.IconStyle)),
                i => { Settings.IconStyle = styles[i]; Live(); });
            ui.ToggleRow("mag", "Magnification", Settings.Magnify, v => { Settings.Magnify = v; Live(); }, "Icons grow under the cursor");
            if (Settings.Magnify)
                ui.ValueSlider("magamt", "Magnification amount", Settings.MagnifyPercent, 110, 250, " %", v => { Settings.MagnifyPercent = v; Live(); });
            Toggle(ui, "DockShadow", "Shadow under the dock");
            Toggle(ui, "DockDots", "Dots under open apps");
            Toggle(ui, "DockBadges", "Unread badges", "Counts like (3) from app titles, red dot when an app wants attention");
            Toggle(ui, "DockPreviews", "Window previews on hover", "Live thumbnails · click to switch, × to close");
            Toggle(ui, "DockBounce", "Bounce when opening apps");

            Card(ui, "Folders & windows");
            Toggle(ui, "DockStacks", "Folders open as stacks", "Click Downloads to see recent files in a grid");
            Toggle(ui, "DockMinimized", "Minimize windows into the dock", "Minimized windows appear on the right side");

            Card(ui, "Behaviour");
            ui.ToggleRow("hide", "Automatically hide the dock", Settings.AutoHide, v => { Settings.AutoHide = v; Full(); });
            if (Settings.AutoHide) Toggle(ui, "DockSmartHide", "Smart hide", "Stay visible on the desktop; hide only when a window covers the dock");
            ui.ToggleRow("run", "Show open apps", Settings.ShowRunning, v => { Settings.ShowRunning = v; Live(); });
            if (Settings.HideDockFor.Count > 0)
            {
                Card(ui, "Dock hides while using");
                foreach (var key in Settings.HideDockFor.ToArray())
                {
                    string k = key;
                    string name = k.StartsWith("exe:") ? ShellIcons.DisplayName(k.Substring(4)) : (k.StartsWith("aumid:") ? ShellIcons.DisplayName(@"shell:AppsFolder\" + k.Substring(6)) : k);
                    ui.Item("r:" + k, name, () => { Settings.HideDockFor.Remove(k); Live(); }, rightText: "Remove", keep: true);
                }
            }
            if (Settings.CustomIcons.Count > 0)
            {
                Card(ui, "Custom icons");
                foreach (var kv in new List<KeyValuePair<string, string>>(Settings.CustomIcons))
                {
                    string k = kv.Key;
                    ui.Item("ci:" + k, ShellIcons.DisplayName(k), () => { Settings.CustomIcons.Remove(k); App.ApplySettings(); }, sub: Path.GetFileName(kv.Value), rightText: "Reset", keep: true);
                }
            }
            if (Settings.HiddenApps.Count > 0)
            {
                Card(ui, "Never shown in the dock");
                foreach (var key in Settings.HiddenApps.ToArray())
                {
                    string k = key;
                    string name = k.StartsWith("exe:") ? ShellIcons.DisplayName(k.Substring(4)) : k;
                    ui.Item("h:" + k, name, () => { Settings.HiddenApps.Remove(k); Live(); }, rightText: "Show again", keep: true);
                }
            }
            ui.Gap(10);
            ui.Button("resetdock", "Reset Dock to Defaults", () =>
            {
                Look.Reset("Dock");
                Settings.IconSize = 40; Settings.Magnify = true; Settings.MagnifyPercent = 150; Settings.IconStyle = "whitesur";
                Live();
            }, false, true);
        }

        void BarPage(UI ui)
        {
            Card(ui, "Style");
            ui.Choice("barstyle", new[] { "Full width", "Floating islands" }, Look.Get("BarStyle") == "islands" ? 1 : 0,
                i => { Look.Set("BarStyle", i == 1 ? "islands" : "full"); Live(); });
            Int(ui, "BarHeight", "Height", 22, 52, " px");
            Int(ui, "BarFont", "Text size", 10, 18, " px");
            Int(ui, "BarItemWidth", "Space per icon", 22, 52, " px");
            if (Look.Get("BarStyle") == "islands")
            {
                Int(ui, "BarRadius", "Island roundness", 0, 26, " px");
                Int(ui, "BarMargin", "Island margin", 0, 14, " px");
            }

            Card(ui, "Background");
            Toggle(ui, "BarBlur", "Blur behind the menu bar");
            Colour(ui, "BarColor", "Colour");
            Int(ui, "BarOpacity", "Colour opacity", 0, 100, " %");
            Int(ui, "BarBorderOpacity", "Border / divider opacity", 0, 100, " %");
            Colour(ui, "BarTextColor", "Text & icon colour");

            Card(ui, "Show in menu bar");
            Toggle(ui, "BarShowApp", "App name");
            Toggle(ui, "BarShowMedia", "Now playing");
            Toggle(ui, "BarShowClock", "Clock");
            Toggle(ui, "BarShowDate", "Date next to the clock");
            string[] modes = { "auto", "12", "24" };
            ui.Choice("clockfmt", new[] { "Clock: Auto", "12-hour", "24-hour" }, Math.Max(0, Array.IndexOf(modes, Look.Get("BarClock"))),
                i => { Look.Set("BarClock", modes[i]); Live(); });
            Toggle(ui, "BarShowWifi", "Wi-Fi");
            Toggle(ui, "BarShowBluetooth", "Bluetooth (when on)");
            Toggle(ui, "BarShowVolume", "Sound");
            Toggle(ui, "BarShowBattery", "Battery");
            Toggle(ui, "BarShowBatteryPct", "Battery percentage");
            Toggle(ui, "BarShowTray", "Background apps button");
            Toggle(ui, "BarShowCC", "Control Center");

            Card(ui, "Extras");
            Toggle(ui, "BarAppMenus", "App menus", "File, Edit, View… for apps that have classic menus");
            Toggle(ui, "BarWorkspaces", "Desktops", "Dots for Windows virtual desktops · click to switch");
            Toggle(ui, "BarPrivacy", "Mic & camera indicators", "Orange dot = microphone, green dot = camera in use");
            Toggle(ui, "BarKeyboard", "Keyboard layout");
            Toggle(ui, "BarCpu", "CPU usage");
            Toggle(ui, "BarRam", "Memory usage");
            Toggle(ui, "BarTemp", "CPU temperature");
            Toggle(ui, "BarNet", "Network speed");
            ui.Gap(10);
            ui.Button("resetbar", "Reset Menu Bar to Defaults", () => { Look.Reset("Bar"); Live(); }, false, true);
        }

        void Appearance(UI ui)
        {
            Card(ui, "Theme");
            string[] modes = { "auto", "light", "dark" };
            ui.Choice("theme", new[] { "Match Windows", "Light", "Dark" }, Math.Max(0, Array.IndexOf(modes, Settings.ThemeMode)),
                i => { Settings.ThemeMode = modes[i]; Live(); });
            Card(ui, "Accent colour");
            Colour(ui, "Accent", "Used for switches, sliders and highlights", false);
            Card(ui, "Panels");
            ui.ToggleRow("pblur", "Blur in Control Center & menus", Settings.Blur, v => { Settings.Blur = v; Live(); });
        }

        static readonly string[][] presets =
        {
            new[] { "macOS Dark", "theme=dark", "DockColor=auto", "DockOpacity=40", "DockBlur=1", "DockBorderColor=auto", "DockBorderOpacity=22", "DockBorderWidth=1", "DockRadius=20", "DockShadow=1",
                    "BarStyle=full", "BarColor=auto", "BarOpacity=50", "BarBlur=1", "BarBorderOpacity=12", "BarTextColor=auto", "Accent=#0A84FF" },
            new[] { "macOS Light", "theme=light", "DockColor=auto", "DockOpacity=45", "DockBlur=1", "DockBorderColor=auto", "DockBorderOpacity=18", "DockBorderWidth=1", "DockRadius=20", "DockShadow=1",
                    "BarStyle=full", "BarColor=auto", "BarOpacity=55", "BarBlur=1", "BarBorderOpacity=10", "BarTextColor=auto", "Accent=#0A84FF" },
            new[] { "Glass", "theme=auto", "DockColor=#FFFFFF", "DockOpacity=8", "DockBlur=1", "DockBorderColor=#FFFFFF", "DockBorderOpacity=45", "DockBorderWidth=1", "DockRadius=26", "DockShadow=1",
                    "BarStyle=islands", "BarColor=#FFFFFF", "BarOpacity=10", "BarBlur=1", "BarBorderOpacity=30", "BarRadius=16", "BarTextColor=auto", "Accent=#64D2FF" },
            new[] { "Minimal", "theme=auto", "DockColor=auto", "DockOpacity=0", "DockBlur=0", "DockBorderOpacity=0", "DockBorderWidth=0", "DockShadow=0",
                    "BarStyle=full", "BarColor=auto", "BarOpacity=0", "BarBlur=0", "BarBorderOpacity=0", "BarTextColor=auto", "Accent=#8E8E93" },
            new[] { "Neon", "theme=dark", "DockColor=#0B0B14", "DockOpacity=85", "DockBlur=1", "DockBorderColor=#BF5AF2", "DockBorderOpacity=90", "DockBorderWidth=2", "DockRadius=22", "DockShadow=1",
                    "BarStyle=islands", "BarColor=#0B0B14", "BarOpacity=85", "BarBlur=1", "BarBorderOpacity=40", "BarRadius=14", "BarTextColor=#F2E9FF", "Accent=#BF5AF2" },
            new[] { "Solid", "theme=auto", "DockColor=auto", "DockOpacity=100", "DockBlur=0", "DockBorderColor=auto", "DockBorderOpacity=15", "DockBorderWidth=1", "DockShadow=1",
                    "BarStyle=full", "BarColor=auto", "BarOpacity=100", "BarBlur=0", "BarBorderOpacity=15", "BarTextColor=auto", "Accent=#0A84FF" },
        };

        static void ApplyPreset(string[] p)
        {
            for (int i = 1; i < p.Length; i++)
            {
                int eq = p[i].IndexOf('=');
                string k = p[i].Substring(0, eq), v = p[i].Substring(eq + 1);
                if (k == "theme") Settings.ThemeMode = v; else Look.Set(k, v);
            }
            Live();
        }

        void Themes(UI ui)
        {
            Card(ui, "Presets");
            float cols = 3, gap = 10 * S;
            float cw = ((ui.Right - ui.Pad) - gap * (cols - 1)) / cols, ch = 70 * S;
            for (int i = 0; i < presets.Length; i++)
            {
                int c = i % 3;
                if (c == 0 && i > 0) ui.Y += ch + gap;
                var r = new RectangleF(ui.Pad + c * (cw + gap), ui.Y, cw, ch);
                var p = presets[i];
                string id = "preset" + i;
                PresetPreview(ui, r, p, Hover == id);
                ui.AddHit(id, r, () => ApplyPreset(p), true);
            }
            ui.Y += ch + 12 * S;

            Card(ui, "From your wallpaper");
            ui.Button("wall", "Match Wallpaper Colours", () =>
            {
                var c = SysInfo.WallpaperColor();
                if (!c.HasValue) return;
                var col = c.Value;
                var deep = Gfx.Mix(Color.FromArgb(255, 20, 20, 24), col, 0.35f);
                Look.Set("DockColor", Look.Hex(deep));
                Look.Set("BarColor", Look.Hex(deep));
                Look.Set("Accent", Look.Hex(Gfx.Mix(col, Color.White, 0.1f)));
                Live();
            }, true, true);

            Card(ui, "Share");
            ui.Button("texp", "Export Theme…", ExportTheme, false, true);
            ui.Button("timp", "Import Theme…", ImportTheme, false, true);
        }

        void PresetPreview(UI ui, RectangleF r, string[] p, bool hot)
        {
            var map = new Dictionary<string, string>();
            for (int i = 1; i < p.Length; i++) { int eq = p[i].IndexOf('='); map[p[i].Substring(0, eq)] = p[i].Substring(eq + 1); }
            Func<string, string, string> get = (k, d) => map.ContainsKey(k) ? map[k] : d;
            bool dark = get("theme", "auto") == "dark" || (get("theme", "auto") == "auto" && Theme.Dark);
            // little desktop: wallpaper gradient, a bar at the top and a dock at the bottom
            using (var path = Gfx.RoundRect(r, 12 * S))
            {
                using (var br = new LinearGradientBrush(r, dark ? Color.FromArgb(255, 40, 48, 90) : Color.FromArgb(255, 150, 180, 240),
                    dark ? Color.FromArgb(255, 90, 50, 110) : Color.FromArgb(255, 240, 180, 200), 30f))
                    ui.G.FillPath(br, path);
                using (var pen = new Pen(hot ? Theme.Accent : Theme.TileBorder, hot ? 2 * S : 1)) ui.G.DrawPath(pen, path);
            }
            Color auto = dark ? Color.FromArgb(255, 30, 30, 34) : Color.FromArgb(255, 246, 246, 249);
            Color bc, dc;
            if (!Look.TryParse(get("BarColor", "auto"), out bc)) bc = auto;
            if (!Look.TryParse(get("DockColor", "auto"), out dc)) dc = auto;
            int bo = int.Parse(get("BarOpacity", "50")), dopa = int.Parse(get("DockOpacity", "40"));
            bool islands = get("BarStyle", "full") == "islands";
            var barR = islands ? new RectangleF(r.X + 6 * S, r.Y + 5 * S, r.Width - 12 * S, 9 * S) : new RectangleF(r.X + 1, r.Y + 1, r.Width - 2, 10 * S);
            using (var br = new SolidBrush(Color.FromArgb(Math.Max(30, bo * 255 / 100), bc)))
            using (var bp = Gfx.RoundRect(barR, islands ? 4.5f * S : 0.5f)) ui.G.FillPath(br, bp);
            var dockR = new RectangleF(r.X + r.Width * 0.22f, r.Bottom - 22 * S, r.Width * 0.56f, 15 * S);
            using (var br = new SolidBrush(Color.FromArgb(Math.Max(30, dopa * 255 / 100), dc)))
            using (var dp = Gfx.RoundRect(dockR, 5 * S)) ui.G.FillPath(br, dp);
            Color acc;
            if (!Look.TryParse(get("Accent", "#0A84FF"), out acc)) acc = Theme.Accent;
            for (int i = 0; i < 4; i++)
                using (var br = new SolidBrush(i == 1 ? acc : Color.FromArgb(220, 255, 255, 255)))
                    ui.G.FillEllipse(br, dockR.X + 7 * S + i * (dockR.Width - 14 * S) / 3.6f, dockR.Y + 3.5f * S, 8 * S, 8 * S);
            using (var f = new Font(Fonts.UiBold, 11.5f * S, GraphicsUnit.Pixel))
            using (var sh = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
            {
                ui.Text(p[0], new RectangleF(r.X + 11 * S, r.Y + r.Height / 2 - 9 * S, r.Width, 20 * S), f, sh);
                ui.Text(p[0], new RectangleF(r.X + 10 * S, r.Y + r.Height / 2 - 10 * S, r.Width, 20 * S), f, Brushes.White);
            }
        }

        void ExportTheme()
        {
            using (var dlg = new SaveFileDialog { Title = "Export theme", FileName = "My Harbor theme.harbortheme", Filter = "Harbor theme (*.harbortheme)|*.harbortheme" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var lines = new List<string> { "; Harbor theme", "Theme=" + Settings.ThemeMode };
                Look.Save(lines);
                File.WriteAllLines(dlg.FileName, lines.ToArray());
            }
        }

        void ImportTheme()
        {
            using (var dlg = new OpenFileDialog { Title = "Import theme", Filter = "Harbor theme (*.harbortheme)|*.harbortheme" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                foreach (var raw in File.ReadAllLines(dlg.FileName))
                {
                    string line = raw.Trim();
                    int eq = line.IndexOf('=');
                    if (line.StartsWith(";") || eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                    if (k.Equals("Theme", StringComparison.OrdinalIgnoreCase)) Settings.ThemeMode = v; else Look.Set(k, v);
                }
                Live();
            }
        }

        void Pinned(UI ui)
        {
            ui.Button("add", "Add App…", AddApp, true, true);
            Card(ui, "In your dock");
            ui.Gap(4);
            var list = Settings.Pinned;
            if (list.Count == 0) ui.Info("Nothing pinned yet. Drag apps onto the dock or use Add App.");
            for (int i = 0; i < list.Count; i++)
            {
                string p = list[i];
                int k = i;
                var r = new RectangleF(ui.Pad, ui.Y, ui.Right - ui.Pad, 40 * S);
                if (Hover != null && Hover.StartsWith("p" + k + ":")) ui.Fill(r, Theme.TileHover, 8 * S);
                var ic = IconFor(p);
                if (ic != null) ui.G.DrawImage(ic, new RectangleF(r.X + 10 * S, r.Y + 6 * S, 28 * S, 28 * S));
                ui.Text(ShellIcons.DisplayName(p), new RectangleF(r.X + 48 * S, r.Y, r.Width - 170 * S, r.Height), ui.Body, ui.TextBrush);
                ui.AddHit("p" + k + ":row", r, null);
                string[] gl = { "", "", "" };
                Action[] acts = {
                    () => Move(k, -1),
                    () => Move(k, 1),
                    () => { Settings.Pinned.RemoveAt(k); Settings.Save(); App.RebuildDocks(); } };
                for (int b = 0; b < 3; b++)
                {
                    var br = new RectangleF(r.Right - (3 - b) * 34 * S - 4 * S, r.Y + 4 * S, 32 * S, 32 * S);
                    string id = "p" + k + ":" + b;
                    if (Hover == id) using (var hb = new SolidBrush(b == 2 ? Color.FromArgb(60, 255, 69, 58) : Theme.TileHover)) ui.G.FillEllipse(hb, br);
                    using (var f = new Font(Fonts.Icons, 11f * S, GraphicsUnit.Pixel)) ui.Glyph(gl[b], br, f, ui.DimBrush);
                    ui.AddHit(id, br, acts[b], true);
                }
                ui.Y += 42 * S;
            }
        }

        void Move(int i, int d)
        {
            int j = i + d;
            var l = Settings.Pinned;
            if (j < 0 || j >= l.Count) return;
            string t = l[i]; l[i] = l[j]; l[j] = t;
            Settings.Save();
            App.RebuildDocks();
        }

        void AddApp()
        {
            using (var dlg = new OpenFileDialog
            {
                Title = "Add to Dock",
                Filter = "Apps and shortcuts (*.exe;*.lnk)|*.exe;*.lnk|All files (*.*)|*.*",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu) + @"\Programs",
                DereferenceLinks = false,
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                if (!Settings.Pinned.Exists(p => string.Equals(p, dlg.FileName, StringComparison.OrdinalIgnoreCase)))
                    Settings.Pinned.Add(dlg.FileName);
                Settings.Save();
                App.RebuildDocks();
                Refresh2();
            }
        }

        void About(UI ui)
        {
            // hero
            var r = ui.Custom(190);
            float cx = r.X + r.Width / 2;
            if (appIcon != null) ui.G.DrawImage(appIcon, new RectangleF(cx - 44 * S, r.Y + 14 * S, 88 * S, 88 * S));
            using (var tf = new Font(Fonts.UiBold, 28f * S, GraphicsUnit.Pixel))
                ui.Text("Harbor", new RectangleF(r.X, r.Y + 108 * S, r.Width, 36 * S), tf, ui.TextBrush, 1);
            ui.Text("A macOS-style dock and menu bar for Windows", new RectangleF(r.X, r.Y + 144 * S, r.Width, 18 * S), ui.Body, ui.DimBrush, 1);
            long ws = 0;
            try { using (var p = Process.GetCurrentProcess()) ws = p.WorkingSet64 / (1024 * 1024); } catch { }
            ui.Text("Version " + Version + "  ·  using " + ws + " MB of memory", new RectangleF(r.X, r.Y + 166 * S, r.Width, 16 * S), ui.Small, ui.DimBrush, 1);

            Card(ui, "Project");
            ui.Item("gh2", "View on GitHub", () => Launcher.Run(RepoUrl), "\uE71B", sub: RepoUrl.Replace("https://", ""), keep: true, chevron: true);
            ui.Item("issue", "Report a Problem or Suggest a Feature", () => Launcher.Run(RepoUrl + "/issues/new"), "\uE939", keep: true, chevron: true);
            string newer = Installer.NewerBuild();
            if (newer != null)
                ui.Item("upd", "Install Update", () => Launcher.Run(newer, "--install"), "\uE896", sub: "Newer build from " + File.GetLastWriteTime(newer).ToString("d MMM, HH:mm"), accent: true, circle: true);
            else
                ui.Item("upd", "Check for Updates", () => Launcher.Run(RepoUrl + "/releases"), "\uE895", sub: "Latest releases on GitHub", keep: true, chevron: true);

            Card(ui, "Harbor");
            ui.Item("cfg", "Open Settings File", () => Launcher.Run(Path.Combine(Settings.Dir, "settings.ini")), "\uE8A5", keep: true);
            ui.Item("restart", "Restart Harbor", App.Restart, "\uE72C");
            ui.Item("quit", "Quit Harbor", App.Quit, "\uE7E8");
            ui.Item("uninst", "Uninstall Harbor…", App.Uninstall, "\uE74D");

            ui.Gap(10);
            ui.Text("Free and open source (MIT). Inspired by macOS, WhiteSur and Seelen UI.", new RectangleF(ui.Pad, ui.Y, ui.Right - ui.Pad, 18 * S), ui.Small, ui.DimBrush, 1);
            ui.Y += 22 * S;
        }
    }
}
