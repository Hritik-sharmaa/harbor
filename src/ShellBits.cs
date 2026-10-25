using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Harbor
{
    /// <summary>Registers a window as a desktop AppBar so maximized windows don't cover it.</summary>
    sealed class AppBar
    {
        readonly IntPtr hwnd;
        readonly int callbackMsg;
        public bool Registered { get; private set; }
        public event Action<bool> FullScreenChanged;
        public event Action PosChanged;

        public AppBar(IntPtr hwnd, string id)
        {
            this.hwnd = hwnd;
            callbackMsg = N.RegisterWindowMessage("HarborAppBar_" + id);
        }

        APPBARDATA Data()
        {
            var d = new APPBARDATA();
            d.cbSize = Marshal.SizeOf(d);
            d.hWnd = hwnd;
            return d;
        }

        public void Register()
        {
            if (Registered) return;
            var d = Data();
            d.uCallbackMessage = (uint)callbackMsg;
            N.SHAppBarMessage(N.ABM_NEW, ref d);
            Registered = true;
        }

        public void Unregister()
        {
            if (!Registered) return;
            var d = Data();
            N.SHAppBarMessage(N.ABM_REMOVE, ref d);
            Registered = false;
        }

        /// <summary>Reserves 'thickness' pixels along the edge of the monitor; returns the granted rect.</summary>
        public RECT SetPos(uint edge, RECT mon, int thickness)
        {
            var d = Data();
            d.uEdge = edge;
            d.rc = mon;
            switch (edge)
            {
                case 1: d.rc.Bottom = mon.Top + thickness; break;      // top
                case 3: d.rc.Top = mon.Bottom - thickness; break;      // bottom
                case 0: d.rc.Right = mon.Left + thickness; break;      // left
                case 2: d.rc.Left = mon.Right - thickness; break;      // right
            }
            N.SHAppBarMessage(N.ABM_QUERYPOS, ref d);
            switch (edge)
            {
                case 1: d.rc.Bottom = d.rc.Top + thickness; break;
                case 3: d.rc.Top = d.rc.Bottom - thickness; break;
                case 0: d.rc.Right = d.rc.Left + thickness; break;
                case 2: d.rc.Left = d.rc.Right - thickness; break;
            }
            N.SHAppBarMessage(N.ABM_SETPOS, ref d);
            return d.rc;
        }

        public bool Handle(ref Message m)
        {
            if (m.Msg != callbackMsg) return false;
            int code = m.WParam.ToInt32();
            if (code == N.ABN_FULLSCREENAPP) { if (FullScreenChanged != null) FullScreenChanged(m.LParam != IntPtr.Zero); }
            else if (code == N.ABN_POSCHANGED) { if (PosChanged != null) PosChanged(); }
            return true;
        }
    }

    /// <summary>Optionally hides the Windows taskbar so the dock replaces it.</summary>
    static class Taskbar
    {
        static bool applied;

        /// <summary>Explorer's taskbars (skipping our own Shell_TrayWnd used for hosting tray icons).</summary>
        static IEnumerable<IntPtr> Bars()
        {
            foreach (string cls in new[] { "Shell_TrayWnd", "Shell_SecondaryTrayWnd" })
            {
                IntPtr h = IntPtr.Zero;
                while ((h = N.FindWindowEx(IntPtr.Zero, h, cls, null)) != IntPtr.Zero)
                    if (!Tracker.IsSelf(h)) yield return h;
            }
        }

        static IntPtr MainBar()
        {
            foreach (var h in Bars()) if (N.ClassOf(h) == "Shell_TrayWnd") return h;
            return IntPtr.Zero;
        }

        static int GetState()
        {
            var d = new APPBARDATA();
            d.cbSize = Marshal.SizeOf(d);
            return N.SHAppBarMessage(N.ABM_GETSTATE, ref d).ToInt32();
        }

        static void SetAutoHide(bool on)
        {
            var d = new APPBARDATA();
            d.cbSize = Marshal.SizeOf(d);
            d.hWnd = MainBar();
            d.lParam = new IntPtr(on ? N.ABS_AUTOHIDE : N.ABS_ALWAYSONTOP);
            N.SHAppBarMessage(N.ABM_SETSTATE, ref d);
        }

        public static void Apply(bool hide)
        {
            if (hide && !applied)
            {
                bool was = (GetState() & N.ABS_AUTOHIDE) != 0;
                Settings.TaskbarWasAutoHide = was;
                Settings.Save();
                if (!was) SetAutoHide(true);
                foreach (var h in Bars()) N.ShowWindow(h, N.SW_HIDE);
                applied = true;
            }
            else if (!hide && applied)
            {
                foreach (var h in Bars()) N.ShowWindow(h, N.SW_SHOWNA);
                if (!Settings.TaskbarWasAutoHide) SetAutoHide(false);
                applied = false;
            }
        }

        /// <summary>Explorer likes to re-show the taskbar (e.g. after Start opens); hide it again.</summary>
        public static void Enforce()
        {
            if (!applied) return;
            foreach (var h in Bars())
                if (N.IsWindowVisible(h)) N.ShowWindow(h, N.SW_HIDE);
        }

        public static void OnExplorerRestart()
        {
            if (!applied) return;
            applied = false;
            Apply(true);
        }
    }

    static class Autostart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static void Sync()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return;
                    if (Settings.LaunchAtLogin)
                    {
                        string want = "\"" + Application.ExecutablePath + "\" --startup";
                        if ((k.GetValue("Harbor") as string) != want) k.SetValue("Harbor", want);
                    }
                    else if (k.GetValue("Harbor") != null) k.DeleteValue("Harbor", false);
                    if (k.GetValue("MacDock") != null) k.DeleteValue("MacDock", false); // the app's old name
                }
            }
            catch { }
        }
    }

    static class Launcher
    {
        public static void Open(string path, string args = null)
        {
            try
            {
                string p = ShellIcons.ParsingName(path);
                if (p.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) || p.StartsWith("::{"))
                {
                    Process.Start("explorer.exe", p);
                    return;
                }
                var psi = new ProcessStartInfo(p) { UseShellExecute = true };
                if (args != null) psi.Arguments = args;
                try
                {
                    string dir = System.IO.Path.GetDirectoryName(p);
                    if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir)) psi.WorkingDirectory = dir;
                }
                catch { }
                Process.Start(psi).Dispose();
            }
            catch (Exception ex) { App.Log(ex); }
        }

        public static void Run(string file, string args = null)
        {
            try
            {
                var psi = new ProcessStartInfo(file) { UseShellExecute = true };
                if (args != null) psi.Arguments = args;
                var p = Process.Start(psi);
                if (p != null) p.Dispose();
            }
            catch (Exception ex) { App.Log(ex); }
        }

        /// <summary>
        /// "New Window" for a running app. Re-launching a single-instance app (Dia, Store apps) just focuses it,
        /// so: browsers that accept a flag get it; everything else is brought forward and sent Ctrl+N.
        /// </summary>
        public static void NewWindow(string path, string exe, IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !N.IsWindow(hwnd)) { if (path != null) Open(path); return; }
            string name = exe != null ? System.IO.Path.GetFileNameWithoutExtension(exe).ToLowerInvariant() : "";
            bool canRun = exe != null && System.IO.File.Exists(exe) && exe.IndexOf(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase) < 0;
            if (canRun && (name == "chrome" || name == "msedge" || name == "brave" || name == "vivaldi" || name == "opera" || name == "launcher"))
            { Run(exe, "--new-window"); return; }
            if (canRun && name == "firefox") { Run(exe, "-new-window"); return; }

            bool shift = name == "code" || name == "cursor" || name == "windsurf"; // editors use Ctrl+Shift+N for a new window
            Tracker.Activate(hwnd);
            int tries = 0;
            var t = new System.Windows.Forms.Timer { Interval = 120 };
            t.Tick += delegate
            {
                if (N.GetForegroundWindow() != hwnd && ++tries < 6) { Tracker.Activate(hwnd); return; }
                t.Stop();
                t.Dispose();
                if (N.GetForegroundWindow() != hwnd) return; // never type into the wrong app
                N.keybd_event(0x11, 0, 0, UIntPtr.Zero);                 // Ctrl
                if (shift) N.keybd_event(0x10, 0, 0, UIntPtr.Zero);      // Shift
                N.keybd_event((byte)'N', 0, 0, UIntPtr.Zero);
                N.keybd_event((byte)'N', 0, N.KEYEVENTF_KEYUP, UIntPtr.Zero);
                if (shift) N.keybd_event(0x10, 0, N.KEYEVENTF_KEYUP, UIntPtr.Zero);
                N.keybd_event(0x11, 0, N.KEYEVENTF_KEYUP, UIntPtr.Zero);
            };
            t.Start();
        }

        public static void Reveal(string path)
        {
            Run("explorer.exe", "/select,\"" + path + "\"");
        }
    }
}
