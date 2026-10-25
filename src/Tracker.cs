using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace Harbor
{
    sealed class WinInfo
    {
        public IntPtr Hwnd;
        public uint Pid;
        public string Key;    // "exe:<path>" or "aumid:<id>" - groups windows into apps
        public string Exe;    // full exe path (may be null)
        public string Aumid;  // packaged app id (may be null)
        public string WebAppName, WebAppIcon; // installed web apps (Netflix in Edge, YouTube in Chrome...)
    }

    /// <summary>
    /// Event-driven tracking of top-level app windows (shell hook + WinEvent foreground hook).
    /// No busy polling: a cheap safety refresh runs every few seconds.
    /// </summary>
    static class Tracker
    {
        sealed class ProcInfo { public string Exe, ExeName, Aumid; }

        public static List<WinInfo> Windows = new List<WinInfo>();
        public static IntPtr Foreground;
        public static event Action WindowsChanged;
        public static event Action ForegroundChanged;
        public static event Action<IntPtr> Flash;
        public static event Action SafetyTick;
        public static event Action<IntPtr> TitleChanged;

        static uint selfPid;
        static int shellMsg;
        static IntPtr host, fgHook, cloakHook, minHook;
        static WinEventDelegate hookProc; // keep alive
        static Timer debounce, safety;
        static string lastSig = "";
        static readonly Dictionary<uint, ProcInfo> procs = new Dictionary<uint, ProcInfo>();
        static readonly EnumWindowsProc enumProc = EnumCb;
        static List<WinInfo> building;
        static HashSet<uint> seenPids;

        public static void Init(IntPtr hostHwnd)
        {
            host = hostHwnd;
            selfPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            shellMsg = N.RegisterWindowMessage("SHELLHOOK");
            N.RegisterShellHookWindow(host);
            hookProc = OnWinEvent;
            fgHook = N.SetWinEventHook(N.EVENT_SYSTEM_FOREGROUND, N.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, hookProc, 0, 0,
                N.WINEVENT_OUTOFCONTEXT | N.WINEVENT_SKIPOWNPROCESS);
            minHook = N.SetWinEventHook(0x16, 0x17, IntPtr.Zero, hookProc, 0, 0, N.WINEVENT_OUTOFCONTEXT | N.WINEVENT_SKIPOWNPROCESS); // minimize start/end
            cloakHook = N.SetWinEventHook(N.EVENT_OBJECT_CLOAKED, N.EVENT_OBJECT_UNCLOAKED, IntPtr.Zero, hookProc, 0, 0,
                N.WINEVENT_OUTOFCONTEXT | N.WINEVENT_SKIPOWNPROCESS);

            debounce = new Timer { Interval = 120 };
            debounce.Tick += delegate { debounce.Stop(); Refresh(); };
            safety = new Timer { Interval = 4000 };
            safety.Tick += delegate { Refresh(); if (SafetyTick != null) SafetyTick(); };
            safety.Start();

            SetForeground(N.GetForegroundWindow());
            Refresh();
        }

        /// <summary>Explorer restarted: the shell hook registration was lost with it.</summary>
        public static void ReRegisterShellHook()
        {
            N.DeregisterShellHookWindow(host);
            N.RegisterShellHookWindow(host);
            ScheduleRefresh();
        }

        public static void Shutdown()
        {
            try
            {
                N.DeregisterShellHookWindow(host);
                if (fgHook != IntPtr.Zero) N.UnhookWinEvent(fgHook);
                if (cloakHook != IntPtr.Zero) N.UnhookWinEvent(cloakHook);
                if (minHook != IntPtr.Zero) N.UnhookWinEvent(minHook);
            }
            catch { }
        }

        public static void ScheduleRefresh()
        {
            if (debounce == null) return;
            debounce.Stop();
            debounce.Start();
        }

        public static bool HandleMessage(ref Message m)
        {
            if (shellMsg == 0 || m.Msg != shellMsg) return false;
            int code = m.WParam.ToInt32();
            switch (code)
            {
                case 1: case 2: case 13: // created, destroyed, replaced
                    ScheduleRefresh();
                    break;
                case 6: // title changed (badge counts like "(3) WhatsApp")
                    if (TitleChanged != null) TitleChanged(m.LParam);
                    break;
                case 4: case 0x8004:     // activated
                    SetForeground(m.LParam);
                    break;
                case 0x8006:             // flash (wants attention)
                    if (Flash != null) Flash(m.LParam);
                    break;
            }
            return true;
        }

        static void OnWinEvent(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (ev == N.EVENT_SYSTEM_FOREGROUND) { SetForeground(hwnd); ScheduleRefresh(); }
            else if (idObject == 0 && hwnd != IntPtr.Zero) ScheduleRefresh(); // cloak / minimize changes
        }

        static void SetForeground(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            uint pid;
            N.GetWindowThreadProcessId(hwnd, out pid);
            if (pid == selfPid) return;
            IntPtr root = N.GetAncestor(hwnd, N.GA_ROOT);
            if (root != IntPtr.Zero) hwnd = root;
            if (hwnd == Foreground) return;
            Foreground = hwnd;
            if (ForegroundChanged != null) ForegroundChanged();
        }

        public static bool IsSelf(IntPtr hwnd)
        {
            uint pid;
            N.GetWindowThreadProcessId(hwnd, out pid);
            return pid == selfPid;
        }

        public static void Refresh()
        {
            building = new List<WinInfo>();
            seenPids = new HashSet<uint>();
            N.EnumWindows(enumProc, IntPtr.Zero);
            var list = building;
            building = null;

            // forget processes that are gone
            var dead = new List<uint>();
            foreach (var k in procs.Keys) if (!seenPids.Contains(k)) dead.Add(k);
            foreach (var k in dead) procs.Remove(k);
            if (webApps.Count > 0)
                foreach (var hw in new List<IntPtr>(webApps.Keys)) if (!N.IsWindow(hw)) webApps.Remove(hw);

            var sb = new StringBuilder();
            foreach (var w in list) sb.Append(w.Hwnd.ToInt64()).Append(w.Key).Append(N.IsIconic(w.Hwnd) ? 'm' : '-').Append('|');
            string sig = sb.ToString();
            Windows = list;
            if (sig != lastSig)
            {
                lastSig = sig;
                if (WindowsChanged != null) WindowsChanged();
            }
        }

        static bool EnumCb(IntPtr h, IntPtr lp)
        {
            if (!IsAppWindow(h)) return true;
            uint pid;
            N.GetWindowThreadProcessId(h, out pid);
            if (pid == selfPid) return true;
            seenPids.Add(pid);
            var pi = GetProc(pid);
            string aumid = pi.Aumid;
            if (pi.ExeName == "applicationframehost.exe")
            {
                aumid = WindowAumid(h);
                if (aumid == null) return true;
            }
            var w = new WinInfo { Hwnd = h, Pid = pid, Exe = pi.Exe, Aumid = aumid };
            // a browser window that is really an installed web app (Netflix, YouTube...) carries its own
            // app id, name and icon — exactly what the Windows taskbar uses to show it separately
            if (aumid == null) aumid = ApplyWebApp(w, WebApp(h));
            if (aumid != null) w.Key = "aumid:" + aumid.ToLowerInvariant();
            else if (w.Exe != null) w.Key = "exe:" + w.Exe.ToLowerInvariant();
            else w.Key = "pid:" + pid;
            building.Add(w);
            return true;
        }

        static readonly Dictionary<IntPtr, string[]> webApps = new Dictionary<IntPtr, string[]>();

        /// <summary>Copies WebApp() info onto the window; returns its app id (null when it stays an ordinary exe window).</summary>
        static string ApplyWebApp(WinInfo w, string[] web)
        {
            if (web == null) return null;
            if (web[0] == null) { w.Exe = web[3]; return null; } // e.g. Steam: its windows belong to steam.exe
            w.Aumid = web[0];
            w.WebAppName = web[1];
            w.WebAppIcon = web[2];
            return web[0];
        }

        /// <summary>
        /// { app id, display name, icon file, relaunch exe } when the window declares relaunch info (PWAs), else null.
        /// Apps whose id isn't registered anywhere and that give no icon (Steam, Wallpaper Engine: Chromium windows
        /// tagged "Valve.Steam.Client") come back as { null, name, null, exe } so they show as the exe they relaunch.
        /// </summary>
        static string[] WebApp(IntPtr h)
        {
            string[] cached;
            if (webApps.TryGetValue(h, out cached)) return cached;
            IPropertyStore ps = null;
            try
            {
                if (N.SHGetPropertyStoreForWindow(h, ref IID_IPropertyStore, out ps) != 0 || ps == null) return null;
                Guid fmt = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
                string id = ReadString(ps, fmt, 5), icon = ReadString(ps, fmt, 3), name = ReadString(ps, fmt, 4);
                if (string.IsNullOrEmpty(id)) return null;
                // Store apps hosted by a browser (Netflix, Instagram... from the Microsoft Store run inside msedge.exe)
                // carry their package app id "Family!App"; installed web apps carry relaunch name / icon instead
                bool storeApp = id.IndexOf('!') > 0;
                if (!storeApp && string.IsNullOrEmpty(icon) && !ShellIcons.Exists(@"shell:AppsFolder\" + id))
                {
                    string exe = CommandExe(ReadString(ps, fmt, 2));
                    var asExe = exe != null ? new[] { null, name, null, exe } : null;
                    webApps[h] = asExe;
                    return asExe;
                }
                if (!storeApp && string.IsNullOrEmpty(icon) && string.IsNullOrEmpty(name)) return null;
                if (storeApp) { name = name ?? ShellIcons.DisplayName(@"shell:AppsFolder\" + id); icon = null; }
                if (icon != null)
                {
                    int comma = icon.LastIndexOf(',');
                    if (comma > 2 && comma > icon.LastIndexOf('\\')) icon = icon.Substring(0, comma);
                    icon = icon.Trim('"');
                }
                var res = new[] { id, name, icon };
                webApps[h] = res;
                return res;
            }
            catch { return null; }
            finally { if (ps != null) Marshal.ReleaseComObject(ps); }
        }

        /// <summary>The .exe a relaunch command starts (quoted or not, spaces allowed), if it exists.</summary>
        static string CommandExe(string cmd)
        {
            if (string.IsNullOrEmpty(cmd)) return null;
            cmd = Environment.ExpandEnvironmentVariables(cmd.Trim());
            string exe;
            if (cmd.StartsWith("\""))
            {
                int end = cmd.IndexOf('"', 1);
                exe = end > 1 ? cmd.Substring(1, end - 1) : null;
            }
            else
            {
                int end = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                exe = end > 0 ? cmd.Substring(0, end + 4) : null;
            }
            return exe != null && exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(exe) ? exe : null;
        }

        static string ReadString(IPropertyStore ps, Guid fmt, uint pid)
        {
            var key = new PROPERTYKEY { fmtid = fmt, pid = pid };
            PROPVARIANT pv;
            if (ps.GetValue(ref key, out pv) != 0) return null;
            string s = pv.vt == 31 ? Marshal.PtrToStringUni(pv.p) : null;
            N.PropVariantClear(ref pv);
            if (s != null && s.StartsWith("@")) s = null; // resource references (e.g. "@shell32.dll,-123") aren't useful here
            return string.IsNullOrEmpty(s) ? null : s;
        }

        public static bool IsAppWindow(IntPtr h)
        {
            if (!N.IsWindowVisible(h)) return false;
            int ex = N.GetWindowLong(h, N.GWL_EXSTYLE);
            bool appWin = (ex & N.WS_EX_APPWINDOW) != 0;
            // launchers / helpers (Raycast, PowerToys...) mark hidden windows APPWINDOW but never activate:
            // real app windows are never NOACTIVATE, and an APPWINDOW+TOOLWINDOW combo is a background helper
            if ((ex & N.WS_EX_NOACTIVATE) != 0) return false;
            if (appWin && (ex & N.WS_EX_TOOLWINDOW) != 0) return false;
            if (!appWin)
            {
                if ((ex & N.WS_EX_TOOLWINDOW) != 0) return false;
                if ((ex & N.WS_EX_NOACTIVATE) != 0) return false;
                if (N.GetWindow(h, N.GW_OWNER) != IntPtr.Zero) return false;
            }
            if (N.GetWindowTextLength(h) == 0) return false;
            if (N.IsCloaked(h)) return false;
            string cls = N.ClassOf(h);
            switch (cls)
            {
                case "Windows.UI.Core.CoreWindow":
                case "Progman":
                case "WorkerW":
                case "Shell_TrayWnd":
                case "Shell_SecondaryTrayWnd":
                    return false;
            }
            return true;
        }

        static ProcInfo GetProc(uint pid)
        {
            ProcInfo pi;
            if (procs.TryGetValue(pid, out pi)) return pi;
            pi = new ProcInfo();
            IntPtr hp = N.OpenProcess(N.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (hp != IntPtr.Zero)
            {
                try
                {
                    var sb = new StringBuilder(1024);
                    int size = sb.Capacity;
                    if (N.QueryFullProcessImageName(hp, 0, sb, ref size))
                    {
                        pi.Exe = sb.ToString();
                        pi.ExeName = Path.GetFileName(pi.Exe).ToLowerInvariant();
                    }
                    uint len = 512;
                    var ab = new StringBuilder((int)len);
                    if (N.GetApplicationUserModelId(hp, ref len, ab) == 0 && ab.Length > 0) pi.Aumid = ab.ToString();
                }
                catch { }
                finally { N.CloseHandle(hp); }
            }
            procs[pid] = pi;
            return pi;
        }

        static Guid IID_IPropertyStore = new Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");

        static string WindowAumid(IntPtr h)
        {
            IPropertyStore ps = null;
            try
            {
                if (N.SHGetPropertyStoreForWindow(h, ref IID_IPropertyStore, out ps) != 0 || ps == null) return null;
                var key = new PROPERTYKEY { fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), pid = 5 };
                PROPVARIANT pv;
                if (ps.GetValue(ref key, out pv) != 0) return null;
                string s = pv.vt == 31 ? Marshal.PtrToStringUni(pv.p) : null;
                N.PropVariantClear(ref pv);
                return string.IsNullOrEmpty(s) ? null : s;
            }
            catch { return null; }
            finally { if (ps != null) Marshal.ReleaseComObject(ps); }
        }

        /// <summary>Process/app identity for any window, even ones the dock doesn't show (dialogs, popups).</summary>
        public static WinInfo Describe(IntPtr h)
        {
            uint pid;
            N.GetWindowThreadProcessId(h, out pid);
            if (pid == 0 || pid == selfPid) return null;
            var pi = GetProc(pid);
            string aumid = pi.ExeName == "applicationframehost.exe" ? WindowAumid(h) : pi.Aumid;
            var w = new WinInfo { Hwnd = h, Pid = pid, Exe = pi.Exe, Aumid = aumid };
            if (aumid == null) aumid = ApplyWebApp(w, WebApp(h));
            if (aumid != null) w.Key = "aumid:" + aumid.ToLowerInvariant();
            else if (w.Exe != null) w.Key = "exe:" + w.Exe.ToLowerInvariant();
            else w.Key = "pid:" + pid;
            return w;
        }

        /// <summary>Key of the app owning a window (for the menu bar), or null.</summary>
        public static WinInfo Find(IntPtr hwnd)
        {
            foreach (var w in Windows) if (w.Hwnd == hwnd) return w;
            return null;
        }

        public static List<WinInfo> WindowsOfKey(string key)
        {
            var l = new List<WinInfo>();
            if (key == null) return l;
            foreach (var w in Windows) if (w.Key == key) l.Add(w);
            return l;
        }

        // ---------- window actions ----------

        public static void Activate(IntPtr h)
        {
            if (N.IsIconic(h)) N.ShowWindowAsync(h, N.SW_RESTORE);
            if (!N.SetForegroundWindow(h))
            {
                // Foreground lock workaround: a synthetic Alt press lets us take foreground
                N.keybd_event(N.VK_MENU, 0, 0, UIntPtr.Zero);
                N.SetForegroundWindow(h);
                N.keybd_event(N.VK_MENU, 0, N.KEYEVENTF_KEYUP, UIntPtr.Zero);
            }
            N.BringWindowToTop(h);
        }

        public static void Minimize(IntPtr h) { N.ShowWindowAsync(h, N.SW_MINIMIZE); }
        public static void Close(IntPtr h) { N.PostMessage(h, N.WM_CLOSE, IntPtr.Zero, IntPtr.Zero); }

        public static void ToggleMaximize(IntPtr h)
        {
            N.ShowWindowAsync(h, N.IsZoomed(h) ? N.SW_RESTORE : N.SW_MAXIMIZE);
        }
    }
}
