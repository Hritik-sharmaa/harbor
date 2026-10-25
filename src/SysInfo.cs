using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net.NetworkInformation;
using System.Text;
using Microsoft.Win32;

namespace Harbor
{
    /// <summary>Cheap system readings for menu-bar widgets. Only sampled while a widget that needs them is switched on.</summary>
    static class SysInfo
    {
        public static int Cpu, Ram;
        public static double RamUsedGB, RamTotalGB;
        public static readonly List<int> CpuHistory = new List<int>();
        public static long DownBps, UpBps;
        public static bool MicInUse, CamInUse;
        public static int DesktopCount = 1, DesktopIndex;
        public static event Action Changed;

        static long lastIdle, lastKernel, lastUser, lastRx, lastTx;
        static DateTime lastNet;
        static System.Windows.Forms.Timer fast, slow;
        static string lastShown;

        public static void Init()
        {
            fast = new System.Windows.Forms.Timer { Interval = 1500 };
            fast.Tick += delegate
            {
                SampleFast();
                // redraw the menu bars only when a number they show actually changed
                string shown = Cpu + "|" + Ram + "|" + TempC + (Look.Bool("BarNet") ? "|" + Speed(DownBps) + "|" + Speed(UpBps) : "");
                if (shown == lastShown) return;
                lastShown = shown;
                if (Changed != null) Changed();
            };
            slow = new System.Windows.Forms.Timer { Interval = 3000 };
            slow.Tick += delegate { SampleSlow(); };
            Apply();
        }

        /// <summary>Starts/stops the samplers depending on which widgets are enabled (zero cost when all are off).</summary>
        public static void Apply()
        {
            if (fast == null) return;
            bool needFast = Look.Bool("BarCpu") || Look.Bool("BarRam") || Look.Bool("BarTemp") || Look.Bool("BarNet") || NotificationCenter.IsOpen || SystemFlyout.IsOpen;
            bool needSlow = Look.Bool("BarPrivacy") || Look.Bool("BarWorkspaces");
            if (needFast && !fast.Enabled) { SampleFast(); fast.Start(); }
            if (!needFast) fast.Stop();
            if (needSlow && !slow.Enabled) { SampleSlow(); slow.Start(); }
            if (!needSlow) slow.Stop();
        }

        public static bool Demo; // --snapshot --demo: keep the sample readings

        public static void SampleFast()
        {
            if (Demo) return;
            long idle, kernel, user;
            if (N.GetSystemTimes(out idle, out kernel, out user))
            {
                long di = idle - lastIdle, dt = (kernel - lastKernel) + (user - lastUser);
                if (lastKernel != 0 && dt > 0)
                {
                    Cpu = Math.Max(0, Math.Min(100, (int)Math.Round(100.0 * (1 - di / (double)dt))));
                    CpuHistory.Add(Cpu);
                    if (CpuHistory.Count > 60) CpuHistory.RemoveAt(0);
                }
                lastIdle = idle; lastKernel = kernel; lastUser = user;
            }
            var m = new MEMORYSTATUSEX();
            m.dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf(m);
            if (N.GlobalMemoryStatusEx(ref m))
            {
                Ram = (int)m.dwMemoryLoad;
                RamTotalGB = m.ullTotalPhys / 1073741824.0;
                RamUsedGB = (m.ullTotalPhys - m.ullAvailPhys) / 1073741824.0;
            }

            if (Look.Bool("BarTemp") || SystemFlyout.IsOpen) SampleTemp();

            if (Look.Bool("BarNet"))
            {
                long rx = 0, tx = 0;
                try
                {
                    foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                    {
                        if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                        var s = ni.GetIPStatistics();
                        rx += s.BytesReceived; tx += s.BytesSent;
                    }
                }
                catch { }
                var now = DateTime.UtcNow;
                double secs = (now - lastNet).TotalSeconds;
                if (lastRx != 0 && secs > 0.2) { DownBps = (long)((rx - lastRx) / secs); UpBps = (long)((tx - lastTx) / secs); }
                lastRx = rx; lastTx = tx; lastNet = now;
            }
        }

        public static int TempC = -1;
        static System.Diagnostics.PerformanceCounter tempCounter;
        static bool tempUnavailable;

        /// <summary>CPU / chassis temperature from the ACPI thermal zone (readable without admin rights).</summary>
        static void SampleTemp()
        {
            if (tempUnavailable || Demo) return;
            try
            {
                if (tempCounter == null)
                {
                    var cat = new System.Diagnostics.PerformanceCounterCategory("Thermal Zone Information");
                    string[] inst = cat.GetInstanceNames();
                    if (inst.Length == 0) { tempUnavailable = true; return; }
                    Array.Sort(inst); // prefer the main CPU zone (\_TZ.THRM / CPUZ...) - first is fine on most laptops
                    tempCounter = new System.Diagnostics.PerformanceCounter("Thermal Zone Information", "High Precision Temperature", inst[0], true);
                }
                float tenthsKelvin = tempCounter.NextValue();
                int c = (int)Math.Round(tenthsKelvin / 10f - 273.15f);
                TempC = c > 0 && c < 120 ? c : -1;
            }
            catch { tempUnavailable = true; TempC = -1; }
        }

        static void SampleSlow()
        {
            bool mic = MicInUse, cam = CamInUse, ws = false;
            if (Look.Bool("BarPrivacy"))
            {
                MicInUse = InUse("microphone");
                CamInUse = InUse("webcam");
            }
            if (Look.Bool("BarWorkspaces")) ws = ReadDesktops();
            if (mic != MicInUse || cam != CamInUse || ws) { if (Changed != null) Changed(); }
        }

        /// <summary>Windows records "LastUsedTimeStop = 0" while an app is using the mic / camera.</summary>
        static bool InUse(string cap)
        {
            try
            {
                using (var root = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\" + cap))
                {
                    if (root == null) return false;
                    foreach (var name in root.GetSubKeyNames())
                        using (var k = root.OpenSubKey(name))
                        {
                            if (k == null) continue;
                            if (name == "NonPackaged")
                            {
                                foreach (var n2 in k.GetSubKeyNames())
                                    using (var k2 = k.OpenSubKey(n2)) if (Active(k2)) return true;
                            }
                            else if (Active(k)) return true;
                        }
                }
            }
            catch { }
            return false;
        }

        static bool Active(RegistryKey k)
        {
            if (k == null) return false;
            object start = k.GetValue("LastUsedTimeStart"), stop = k.GetValue("LastUsedTimeStop");
            return start is long && (long)start != 0 && stop is long && (long)stop == 0;
        }

        /// <summary>Virtual desktops (Task View) from the registry. Returns true when something changed.</summary>
        public static bool ReadDesktops()
        {
            int count = DesktopCount, index = DesktopIndex;
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops"))
                {
                    var ids = k == null ? null : k.GetValue("VirtualDesktopIDs") as byte[];
                    var cur = k == null ? null : k.GetValue("CurrentVirtualDesktop") as byte[];
                    if (ids != null && ids.Length >= 16)
                    {
                        count = ids.Length / 16;
                        index = 0;
                        if (cur != null && cur.Length == 16)
                            for (int i = 0; i < count; i++)
                            {
                                bool eq = true;
                                for (int b = 0; b < 16 && eq; b++) eq = ids[i * 16 + b] == cur[b];
                                if (eq) { index = i; break; }
                            }
                    }
                    else { count = 1; index = 0; }
                }
            }
            catch { }
            bool changed = count != DesktopCount || index != DesktopIndex;
            DesktopCount = count;
            DesktopIndex = index;
            return changed;
        }

        /// <summary>Switches desktops the same way the keyboard does (Ctrl+Win+Left/Right).</summary>
        public static void SwitchDesktop(int target)
        {
            ReadDesktops();
            int d = target - DesktopIndex;
            byte arrow = (byte)(d > 0 ? 0x27 : 0x25);
            for (int i = 0; i < Math.Abs(d); i++)
            {
                N.keybd_event(0x11, 0, 0, UIntPtr.Zero);
                N.keybd_event(N.VK_LWIN, 0, 0, UIntPtr.Zero);
                N.keybd_event(arrow, 0, 1, UIntPtr.Zero);
                N.keybd_event(arrow, 0, 1 | N.KEYEVENTF_KEYUP, UIntPtr.Zero);
                N.keybd_event(N.VK_LWIN, 0, N.KEYEVENTF_KEYUP, UIntPtr.Zero);
                N.keybd_event(0x11, 0, N.KEYEVENTF_KEYUP, UIntPtr.Zero);
                System.Threading.Thread.Sleep(60);
            }
            DesktopIndex = Math.Max(0, Math.Min(DesktopCount - 1, target));
        }

        public static string KeyboardLayout()
        {
            try
            {
                uint pid;
                uint tid = N.GetWindowThreadProcessId(Tracker.Foreground, out pid);
                int lang = N.GetKeyboardLayout(tid).ToInt32() & 0xFFFF;
                var ci = new CultureInfo(lang);
                return ci.ThreeLetterWindowsLanguageName.ToUpperInvariant();
            }
            catch { return "ENG"; }
        }

        public static string Speed(long bps)
        {
            if (bps < 1024) return bps + " B/s";
            if (bps < 1024 * 1024) return (bps / 1024) + " KB/s";
            return (bps / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB/s";
        }

        /// <summary>Current wallpaper's characteristic colour (for "Match wallpaper").</summary>
        public static Color? WallpaperColor()
        {
            try
            {
                var sb = new StringBuilder(512);
                N.SystemParametersInfo(0x73, (uint)sb.Capacity, sb, 0);
                string path = sb.ToString();
                if (!File.Exists(path))
                {
                    string cached = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Themes\TranscodedWallpaper");
                    if (File.Exists(cached)) path = cached; else return null;
                }
                using (var fs = File.OpenRead(path))
                using (var img = Image.FromStream(fs, false, false))
                using (var small = new Bitmap(img, new Size(48, 27)))
                    return Gfx.Dominant(small);
            }
            catch { return null; }
        }
    }
}
