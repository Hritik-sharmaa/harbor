using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;

namespace Harbor
{
    /// <summary>
    /// Every user-tweakable appearance value (spacing, sizes, colours, opacity, blur, borders, visible items).
    /// Stored in settings.ini as Key=Value; anything not set falls back to the defaults below.
    /// </summary>
    static class Look
    {
        static readonly Dictionary<string, string> defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // dock
            { "DockSpacing", "6" }, { "DockPadH", "8" }, { "DockPadV", "6" }, { "DockRadius", "20" }, { "DockMargin", "6" },
            { "DockColor", "auto" }, { "DockOpacity", "40" }, { "DockBlur", "1" },
            { "DockBorderColor", "auto" }, { "DockBorderOpacity", "22" }, { "DockBorderWidth", "1" },
            { "DockShadow", "1" }, { "DockDots", "1" },
            { "DockPosition", "bottom" }, { "DockPreviews", "1" }, { "DockBadges", "1" }, { "DockMinimized", "0" },
            { "DockStacks", "1" }, { "DockBounce", "1" }, { "DockSmartHide", "1" },
            // menu bar
            { "BarStyle", "full" }, { "BarHeight", "30" }, { "BarFont", "13" }, { "BarItemWidth", "32" },
            { "BarRadius", "14" }, { "BarMargin", "4" },
            { "BarColor", "auto" }, { "BarOpacity", "50" }, { "BarBlur", "1" }, { "BarBorderOpacity", "12" }, { "BarTextColor", "auto" },
            { "BarShowApp", "1" }, { "BarShowMedia", "1" }, { "BarShowClock", "1" }, { "BarShowDate", "1" }, { "BarClock", "auto" },
            { "BarShowWifi", "1" }, { "BarShowBluetooth", "1" }, { "BarShowVolume", "1" }, { "BarShowBattery", "1" }, { "BarShowBatteryPct", "1" },
            { "BarShowTray", "1" }, { "BarShowCC", "1" },
            { "BarAppMenus", "1" }, { "BarWorkspaces", "1" }, { "BarCpu", "1" }, { "BarRam", "1" }, { "BarTemp", "1" }, { "BarNet", "0" },
            { "BarKeyboard", "0" }, { "BarPrivacy", "1" }, { "BarHeadphones", "1" },
            // general
            { "Accent", "#0A84FF" },
            { "CornerTL", "none" }, { "CornerTR", "none" }, { "Hotkeys", "1" },
            { "NcWidgets", "1" }, { "Banners", "1" }, { "Lyrics", "1" },
            // island (centre of the menu bar)
            { "Island", "1" }, { "IslandTab", "dashboard" }, { "ClipboardHistory", "1" },
            { "IslandTabs", "" }, { "LiveActivities", "1" }, { "BreakEyes", "1" }, { "BreakStand", "1" }, { "BreakWater", "0" }, { "CalendarUrls", "" },
            { "FocusPomodoro", "1" }, { "FocusHideDock", "1" }, { "UpdateCheck", "1" }, { "UpdateChecked", "" }, { "UpdateNotified", "" },
            { "PomoWork", "25" }, { "PomoShort", "5" }, { "PomoLong", "15" }, { "PomoToday", "" },
            { "DayStart", "9" }, { "DayEnd", "18" },
            { "WeatherCity", "" }, { "WeatherPlace", "" }, { "WeatherLat", "" }, { "WeatherLon", "" }, { "WeatherUnit", "C" },
            { "ScreenTime", "1" }, { "PomoFocus", "" }, { "ClaudeBanners", "1" }, { "KeepAwake", "0" },
        };

        static readonly Dictionary<string, string> vals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Back to the shipped look for this run only (README screenshots); nothing is saved.</summary>
        public static void UseDefaults() { vals.Clear(); }

        public static void Load(string key, string value)
        {
            if (defaults.ContainsKey(key)) vals[key] = value;
        }

        public static void Save(List<string> lines)
        {
            lines.Add("");
            lines.Add("; appearance (also editable in the Harbor app)");
            foreach (var kv in defaults)
                lines.Add(kv.Key + "=" + Get(kv.Key));
        }

        public static string Get(string key)
        {
            string v;
            if (vals.TryGetValue(key, out v)) return v;
            return defaults.TryGetValue(key, out v) ? v : "";
        }

        public static int Int(string key)
        {
            int v;
            if (int.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return v;
            int.TryParse(defaults[key], out v);
            return v;
        }

        public static bool Bool(string key) { return Get(key) == "1"; }

        public static void Set(string key, object value)
        {
            if (value is bool) vals[key] = (bool)value ? "1" : "0";
            else vals[key] = Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        public static void Reset(string prefix)
        {
            foreach (var k in new List<string>(vals.Keys))
                if (k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) vals.Remove(k);
        }

        /// <summary>"auto" -> fallback, otherwise #RRGGBB.</summary>
        public static Color Color(string key, Color auto)
        {
            Color c;
            return TryParse(Get(key), out c) ? c : auto;
        }

        public static bool TryParse(string s, out Color c)
        {
            c = System.Drawing.Color.Empty;
            if (string.IsNullOrEmpty(s) || s[0] != '#' || s.Length != 7) return false;
            int rgb;
            if (!int.TryParse(s.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb)) return false;
            c = System.Drawing.Color.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
            return true;
        }

        public static string Hex(Color c) { return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }

        public static Color WithOpacity(Color c, string opacityKey)
        {
            int a = Math.Max(0, Math.Min(100, Int(opacityKey)));
            return System.Drawing.Color.FromArgb(a * 255 / 100, c);
        }

        // ---- resolved colours used by the bar and dock ----

        public static Color DockFill
        {
            get
            {
                var auto = Theme.Dark ? System.Drawing.Color.FromArgb(255, 34, 34, 38) : System.Drawing.Color.FromArgb(255, 246, 246, 249);
                return WithOpacity(Color("DockColor", auto), "DockOpacity");
            }
        }

        public static Color DockBorder
        {
            get
            {
                var auto = Theme.Dark ? System.Drawing.Color.White : System.Drawing.Color.Black;
                return WithOpacity(Color("DockBorderColor", auto), "DockBorderOpacity");
            }
        }

        public static Color BarFill
        {
            get
            {
                var auto = Theme.Dark ? System.Drawing.Color.FromArgb(255, 22, 22, 26) : System.Drawing.Color.FromArgb(255, 246, 246, 250);
                return WithOpacity(Color("BarColor", auto), "BarOpacity");
            }
        }

        public static Color BarText { get { return Color("BarTextColor", Theme.Text); } }
    }
}
