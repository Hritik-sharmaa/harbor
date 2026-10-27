using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Microsoft.Win32;

namespace Harbor
{
    static class Settings
    {
        public static int IconSize = 40;
        public static bool Magnify = true;
        public static int MagnifyPercent = 150;
        public static bool AutoHide = false;
        public static bool ShowRunning = true;
        public static bool ShowTopBar = true;
        public static bool HideTaskbar = true;
        public static bool DockAllDisplays = true;
        public static bool BarAllDisplays = true;
        public static bool TrayIcons = true;
        public static string IconStyle = "whitesur";
        public static bool Blur = true;
        public static bool LaunchAtLogin = true;
        public static string ThemeMode = "auto";
        public static bool TaskbarWasAutoHide = false;
        public static List<string> Pinned = new List<string>();
        public static List<string> HiddenApps = new List<string>(); // app keys never shown as open apps
        public static List<string> HideDockFor = new List<string>(); // app keys: dock hides while they're focused
        public static Dictionary<string, string> CustomIcons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // dock path -> image
        public static Dictionary<string, string> IconLooks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);  // dock path -> brand|white|dark|original

        public static string Dir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Harbor"); }
        }

        static string FilePath { get { return Path.Combine(Dir, "settings.ini"); } }

        public static void Load()
        {
            // coming from the old name (MacDock)? bring its settings, pins and themes along
            try
            {
                string legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MacDock", "settings.ini");
                if (!File.Exists(FilePath) && File.Exists(legacy))
                {
                    Directory.CreateDirectory(Dir);
                    File.Copy(legacy, FilePath);
                }
            }
            catch { }
            if (!File.Exists(FilePath))
            {
                Pinned = DefaultPins();
                Save();
                return;
            }
            bool inPins = false, inHidden = false, inIcons = false, inRules = false;
            Pinned.Clear();
            HiddenApps.Clear();
            HideDockFor.Clear();
            CustomIcons.Clear();
            IconLooks.Clear();
            bool inLooks = false;
            foreach (string raw in File.ReadAllLines(FilePath))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(";")) continue;
                if (line.Equals("[pinned]", StringComparison.OrdinalIgnoreCase)) { inPins = true; inHidden = inIcons = inRules = false; continue; }
                if (line.Equals("[icons]", StringComparison.OrdinalIgnoreCase)) { inIcons = true; inPins = inHidden = inRules = inLooks = false; continue; }
                if (line.Equals("[iconlook]", StringComparison.OrdinalIgnoreCase)) { inLooks = true; inPins = inHidden = inRules = inIcons = false; continue; }
                if (line.Equals("[hidedock]", StringComparison.OrdinalIgnoreCase)) { inRules = true; inPins = inHidden = inIcons = false; continue; }
                if (line.Equals("[hidden]", StringComparison.OrdinalIgnoreCase)) { inHidden = true; inPins = inIcons = inRules = false; continue; }
                if (line.StartsWith("[")) { inPins = inHidden = inIcons = inRules = inLooks = false; continue; }
                if (inLooks) { int bar2 = line.LastIndexOf('|'); if (bar2 > 0) IconLooks[line.Substring(0, bar2)] = line.Substring(bar2 + 1); continue; }
                if (inIcons) { int bar = line.IndexOf('|'); if (bar > 0) CustomIcons[line.Substring(0, bar)] = line.Substring(bar + 1); continue; }
                if (inRules) { HideDockFor.Add(line); continue; }
                if (inHidden) { HiddenApps.Add(line); continue; }
                if (inPins) { Pinned.Add(line); continue; }
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string k = line.Substring(0, eq).Trim().ToLowerInvariant(), v = line.Substring(eq + 1).Trim();
                switch (k)
                {
                    case "iconsize": IconSize = Clamp(ToInt(v, 40), 28, 96); break;
                    case "magnify": Magnify = v == "1"; break;
                    case "magnifypercent": MagnifyPercent = Clamp(ToInt(v, 150), 110, 250); break;
                    case "autohide": AutoHide = v == "1"; break;
                    case "showrunning": ShowRunning = v == "1"; break;
                    case "showtopbar": ShowTopBar = v == "1"; break;
                    case "hidetaskbar": HideTaskbar = v == "1"; break;
                    case "dockalldisplays": DockAllDisplays = v == "1"; break;
                    case "baralldisplays": BarAllDisplays = v == "1"; break;
                    case "trayicons": TrayIcons = v == "1"; break;
                    case "iconstyle": IconStyle = (v == "tiles" || v == "original" || v == "whitesur") ? v : "whitesur"; break;
                    case "blur": Blur = v == "1"; break;
                    case "launchatlogin": LaunchAtLogin = v == "1"; break;
                    case "theme": ThemeMode = v.ToLowerInvariant(); break;
                    case "taskbarwasautohide": TaskbarWasAutoHide = v == "1"; break;
                    default: Look.Load(line.Substring(0, eq).Trim(), v); break;
                }
            }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var l = new List<string>();
                l.Add("; Harbor settings - edit while Harbor is closed, or use the menus");
                l.Add("[settings]");
                l.Add("IconSize=" + IconSize);
                l.Add("Magnify=" + B(Magnify));
                l.Add("MagnifyPercent=" + MagnifyPercent);
                l.Add("AutoHide=" + B(AutoHide));
                l.Add("ShowRunning=" + B(ShowRunning));
                l.Add("ShowTopBar=" + B(ShowTopBar));
                l.Add("HideTaskbar=" + B(HideTaskbar));
                l.Add("DockAllDisplays=" + B(DockAllDisplays));
                l.Add("BarAllDisplays=" + B(BarAllDisplays));
                l.Add("TrayIcons=" + B(TrayIcons));
                l.Add("IconStyle=" + IconStyle);
                l.Add("Blur=" + B(Blur));
                l.Add("LaunchAtLogin=" + B(LaunchAtLogin));
                l.Add("Theme=" + ThemeMode);
                l.Add("TaskbarWasAutoHide=" + B(TaskbarWasAutoHide));
                Look.Save(l);
                l.Add("");
                l.Add("; one app / file / folder per line (exe, lnk, folder, or shell:AppsFolder\\<AppUserModelID>)");
                l.Add("[pinned]");
                l.AddRange(Pinned);
                l.Add("");
                l.Add("; open apps that should never appear in the dock");
                l.Add("[hidden]");
                l.AddRange(HiddenApps);
                l.Add("");
                l.Add("; custom dock icons: <dock item>|<image file>");
                l.Add("[icons]");
                foreach (var kv in CustomIcons) l.Add(kv.Key + "|" + kv.Value);
                l.Add("");
                l.Add("; apps that hide the dock while they're in front");
                l.Add("[hidedock]");
                l.AddRange(HideDockFor);
                l.Add("");
                l.Add("; per-app icon look: <dock item>|brand / white / dark / original");
                l.Add("[iconlook]");
                foreach (var kv in IconLooks) l.Add(kv.Key + "|" + kv.Value);
                File.WriteAllLines(FilePath, l.ToArray());
            }
            catch { }
        }

        static string B(bool b) { return b ? "1" : "0"; }
        static int ToInt(string s, int d) { int v; return int.TryParse(s, out v) ? v : d; }
        static int Clamp(int v, int a, int b) { return v < a ? a : (v > b ? b : v); }

        static List<string> DefaultPins()
        {
            var pins = new List<string>();
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string winApps = Path.Combine(local, @"Microsoft\WindowsApps");

            pins.Add(Path.Combine(win, "explorer.exe"));
            AddFirst(pins, Path.Combine(pf86, @"Microsoft\Edge\Application\msedge.exe"),
                           Path.Combine(pf, @"Microsoft\Edge\Application\msedge.exe"));
            AddFirst(pins, Path.Combine(pf, @"Google\Chrome\Application\chrome.exe"),
                           Path.Combine(pf86, @"Google\Chrome\Application\chrome.exe"),
                           Path.Combine(local, @"Google\Chrome\Application\chrome.exe"));
            AddFirst(pins, Path.Combine(pf, @"Mozilla Firefox\firefox.exe"));
            AddFirst(pins, Path.Combine(local, @"Programs\Microsoft VS Code\Code.exe"),
                           Path.Combine(pf, @"Microsoft VS Code\Code.exe"));
            if (File.Exists(Path.Combine(winApps, "wt.exe")))
                pins.Add(@"shell:AppsFolder\Microsoft.WindowsTerminal_8wekyb3d8bbwe!App");
            AddFirst(pins, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Spotify\Spotify.exe"));
            if (Directory.Exists(Path.Combine(winApps, "Microsoft.WindowsNotepad_8wekyb3d8bbwe")))
                pins.Add(@"shell:AppsFolder\Microsoft.WindowsNotepad_8wekyb3d8bbwe!App");
            else
                pins.Add(Path.Combine(win, "notepad.exe"));
            pins.Add(@"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App");
            pins.Add(@"shell:AppsFolder\windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel");

            // drop AppsFolder entries that don't exist on this PC
            pins.RemoveAll(p => p.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) && !ShellIcons.Exists(p));
            return pins;
        }

        static void AddFirst(List<string> pins, params string[] candidates)
        {
            foreach (var c in candidates) if (File.Exists(c)) { pins.Add(c); return; }
        }
    }

    static class Theme
    {
        public static bool Dark = true;

        public static void Refresh()
        {
            string m = Settings.ThemeMode;
            if (m == "dark") Dark = true;
            else if (m == "light") Dark = false;
            else
            {
                Dark = true;
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    {
                        object v = k == null ? null : k.GetValue("SystemUsesLightTheme");
                        if (v is int) Dark = (int)v == 0;
                    }
                }
                catch { }
            }
            UserDark = Dark;
            N.SetMenuTheme(Dark);
        }

        /// <summary>
        /// Your real light/dark choice. Panels always draw dark glass (Dark is forced on while they render),
        /// so anything that shows the setting itself - the Dark Mode toggles - reads this instead.
        /// </summary>
        public static bool UserDark = true;

        public static Color Text { get { return Dark ? Color.FromArgb(242, 255, 255, 255) : Color.FromArgb(230, 0, 0, 0); } }
        public static Color TextDim { get { return Dark ? Color.FromArgb(150, 255, 255, 255) : Color.FromArgb(120, 0, 0, 0); } }

        public static Color BarBg(bool blur)
        {
            if (Dark) return blur ? Color.FromArgb(120, 22, 22, 26) : Color.FromArgb(255, 30, 30, 34);
            return blur ? Color.FromArgb(150, 246, 246, 250) : Color.FromArgb(255, 238, 238, 242);
        }

        public static Color BarHighlight { get { return Dark ? Color.FromArgb(46, 255, 255, 255) : Color.FromArgb(28, 0, 0, 0); } }

        public static Color DockBg(bool blur)
        {
            if (Dark) return blur ? Color.FromArgb(95, 38, 38, 42) : Color.FromArgb(215, 40, 40, 44);
            return blur ? Color.FromArgb(110, 255, 255, 255) : Color.FromArgb(215, 242, 242, 246);
        }

        public static Color DockBorderInner { get { return Dark ? Color.FromArgb(55, 255, 255, 255) : Color.FromArgb(120, 255, 255, 255); } }
        public static Color DockBorderOuter { get { return Dark ? Color.FromArgb(110, 0, 0, 0) : Color.FromArgb(45, 0, 0, 0); } }
        public static Color Separator { get { return Dark ? Color.FromArgb(70, 255, 255, 255) : Color.FromArgb(60, 0, 0, 0); } }
        public static Color Dot { get { return Dark ? Color.FromArgb(220, 235, 235, 235) : Color.FromArgb(200, 30, 30, 30); } }
        public static Color LabelBg { get { return Dark ? Color.FromArgb(235, 44, 44, 48) : Color.FromArgb(240, 242, 242, 245); } }
        public static Color LabelBorder { get { return Dark ? Color.FromArgb(60, 255, 255, 255) : Color.FromArgb(40, 0, 0, 0); } }

        // control center
        public static Color Accent { get { return Look.Color("Accent", Color.FromArgb(255, 10, 132, 255)); } }
        public static Color PanelSolid { get { return Dark ? Color.FromArgb(255, 36, 36, 40) : Color.FromArgb(255, 236, 236, 240); } }
        public static int PanelTint { get { return Dark ? unchecked((int)0xA0201E1E) : unchecked((int)0x90F2F0F0); } } // AABBGGRR
        public static Color Tile { get { return Dark ? Color.FromArgb(34, 255, 255, 255) : Color.FromArgb(150, 255, 255, 255); } }
        public static Color TileHover { get { return Dark ? Color.FromArgb(52, 255, 255, 255) : Color.FromArgb(205, 255, 255, 255); } }
        public static Color TileBorder { get { return Dark ? Color.FromArgb(28, 255, 255, 255) : Color.FromArgb(30, 0, 0, 0); } }
        public static Color CircleOff { get { return Dark ? Color.FromArgb(48, 255, 255, 255) : Color.FromArgb(28, 0, 0, 0); } }
        public static Color Track { get { return Dark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(30, 0, 0, 0); } }
        public static Color TrackFill { get { return Dark ? Color.FromArgb(235, 255, 255, 255) : Color.FromArgb(255, 255, 255, 255); } }
    }
}
