using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace Harbor
{
    sealed class TrayIcon
    {
        public IntPtr HWnd;
        public uint UID, CallbackMsg, Version;
        public Guid Guid;
        public string Tip = "";
        public Bitmap Image;
        public bool Hidden;
        public Rectangle ScreenRect; // where the primary menu bar drew it (for Shell_NotifyIconGetRect)
    }

    /// <summary>
    /// Hosts the notification area so tray icons can live in the menu bar.
    /// Apps find the tray with FindWindow("Shell_TrayWnd"); we register that class, receive their icons,
    /// and forward every message to Explorer's real tray so nothing is lost if Harbor quits.
    /// </summary>
    static class TrayService
    {
        public static readonly List<TrayIcon> Icons = new List<TrayIcon>();
        public static event Action Changed;
        public static bool Running { get { return hwnd != IntPtr.Zero; } }
        public static long OwnBroadcastUntil;

        static IntPtr hwnd, notifyHwnd;
        static readonly WndProcDelegate trayProc = TrayWndProc;
        static readonly WndProcDelegate childProc = (h, m, w, l) => N.DefWindowProc(h, m, w, l);
        static System.Windows.Forms.Timer changedTimer;
        static readonly uint selfPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;

        public static void Start()
        {
            if (hwnd != IntPtr.Zero) return;
            try
            {
                IntPtr inst = N.GetModuleHandle(null);
                var wc = new WNDCLASSEX { cbSize = Marshal.SizeOf(typeof(WNDCLASSEX)), lpfnWndProc = trayProc, hInstance = inst, lpszClassName = "Shell_TrayWnd" };
                N.RegisterClassEx(ref wc);
                var wc2 = new WNDCLASSEX { cbSize = Marshal.SizeOf(typeof(WNDCLASSEX)), lpfnWndProc = childProc, hInstance = inst, lpszClassName = "TrayNotifyWnd" };
                N.RegisterClassEx(ref wc2);

                hwnd = N.CreateWindowEx(N.WS_EX_TOPMOST | N.WS_EX_TOOLWINDOW, "Shell_TrayWnd", "", N.WS_POPUP | 0x02000000 | 0x04000000,
                    0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, inst, IntPtr.Zero);
                if (hwnd == IntPtr.Zero) return;
                notifyHwnd = N.CreateWindowEx(0, "TrayNotifyWnd", null, 0x40000000 /*WS_CHILD*/, 0, 0, 0, 0, hwnd, IntPtr.Zero, inst, IntPtr.Zero);
                RaiseAboveExplorer();

                changedTimer = new System.Windows.Forms.Timer { Interval = 60 };
                changedTimer.Tick += delegate { changedTimer.Stop(); if (Changed != null) Changed(); };

                // ask every app to (re-)add its icons; they'll now reach us first
                Broadcast();
                // ask once more: some apps (Wallpaper Engine) ignore a repeat request that arrives right after
                // the previous one (e.g. an old instance quitting), and at sign-in many are still starting up
                var again = new System.Windows.Forms.Timer { Interval = 6000 };
                again.Tick += delegate { again.Stop(); again.Dispose(); if (hwnd != IntPtr.Zero) Broadcast(); };
                again.Start();
            }
            catch (Exception ex) { App.Log(ex); Stop(); }
        }

        public static void Stop()
        {
            if (hwnd == IntPtr.Zero) return;
            N.DestroyWindow(hwnd);
            hwnd = notifyHwnd = IntPtr.Zero;
            IntPtr inst = N.GetModuleHandle(null);
            N.UnregisterClass("TrayNotifyWnd", inst);
            N.UnregisterClass("Shell_TrayWnd", inst);
            foreach (var i in Icons) if (i.Image != null) i.Image.Dispose();
            Icons.Clear();
            Broadcast(); // apps re-add their icons to Explorer's tray
        }

        static void Broadcast()
        {
            OwnBroadcastUntil = Environment.TickCount + 4000;
            N.SendNotifyMessage(N.HWND_BROADCAST, N.RegisterWindowMessage("TaskbarCreated"), IntPtr.Zero, IntPtr.Zero);
        }

        /// <summary>FindWindow returns the highest window in z-order; keep ours above Explorer's.</summary>
        public static void RaiseAboveExplorer()
        {
            if (hwnd == IntPtr.Zero) return;
            N.SetWindowPos(hwnd, N.HWND_TOPMOST, 0, 0, 0, 0, N.SWP_NOMOVE | N.SWP_NOSIZE | N.SWP_NOACTIVATE);
        }

        /// <summary>Called after Explorer restarts: re-claim the tray.</summary>
        public static void Reclaim()
        {
            if (hwnd == IntPtr.Zero) return;
            RaiseAboveExplorer();
            Broadcast();
        }

        /// <summary>Keeps our (invisible) tray window where the menu bar is, for apps that ask where the "taskbar" is.</summary>
        public static void SetBounds(Rectangle r, Rectangle iconArea)
        {
            if (hwnd == IntPtr.Zero) return;
            N.SetWindowPos(hwnd, IntPtr.Zero, r.X, r.Y, r.Width, r.Height, N.SWP_NOACTIVATE | N.SWP_NOZORDER);
            if (notifyHwnd != IntPtr.Zero)
                N.SetWindowPos(notifyHwnd, IntPtr.Zero, iconArea.X - r.X, iconArea.Y - r.Y, Math.Max(1, iconArea.Width), iconArea.Height, N.SWP_NOACTIVATE | N.SWP_NOZORDER);
        }

        public static void Prune()
        {
            int removed = Icons.RemoveAll(i => !N.IsWindow(i.HWnd));
            if (removed > 0) FireChanged();
        }

        static IntPtr ExplorerTray()
        {
            IntPtr h = IntPtr.Zero;
            while ((h = N.FindWindowEx(IntPtr.Zero, h, "Shell_TrayWnd", null)) != IntPtr.Zero)
            {
                uint pid;
                N.GetWindowThreadProcessId(h, out pid);
                if (pid != selfPid) return h;
            }
            return IntPtr.Zero;
        }

        static IntPtr Forward(int msg, IntPtr w, IntPtr l)
        {
            IntPtr tray = ExplorerTray();
            if (tray == IntPtr.Zero) return IntPtr.Zero;
            IntPtr res;
            N.SendMessageTimeout(tray, msg, w, l, 0x2 /*SMTO_ABORTIFHUNG*/, 3000, out res);
            return res;
        }

        static IntPtr TrayWndProc(IntPtr h, int msg, IntPtr w, IntPtr l)
        {
            try
            {
                if (msg == N.WM_COPYDATA && l != IntPtr.Zero)
                {
                    var cds = (COPYDATASTRUCT)Marshal.PtrToStructure(l, typeof(COPYDATASTRUCT));
                    long kind = cds.dwData.ToInt64();
                    if (kind == 1)
                    {
                        bool ok = HandleNotify(cds);
                        Forward(msg, w, l);
                        return new IntPtr(ok ? 1 : 0);
                    }
                    if (kind == 3)
                    {
                        IntPtr r = HandleGetRect(cds);
                        return r != IntPtr.Zero ? r : Forward(msg, w, l);
                    }
                    return Forward(msg, w, l); // app-bar messages and anything else
                }
                if (msg >= N.WM_USER || msg == 0x111 /*WM_COMMAND*/) return Forward(msg, w, l);
            }
            catch (Exception ex) { App.Log(ex); }
            return N.DefWindowProc(h, msg, w, l);
        }

        // NOTIFYICONDATA as the shell sends it (32-bit handles on every platform), offsets from the start of the struct
        const int O_HWND = 4, O_UID = 8, O_FLAGS = 12, O_CALLBACK = 16, O_ICON = 20, O_TIP = 24, O_STATE = 280, O_STATEMASK = 284,
                  O_VERSION = 800, O_GUID = 936;
        const uint NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4, NIF_STATE = 8, NIF_GUID = 0x20;

        static bool HandleNotify(COPYDATASTRUCT cds)
        {
            IntPtr nid = cds.lpData + 8;
            int len = cds.cbData - 8;
            if (len < 24) return false;
            uint message = (uint)Marshal.ReadInt32(cds.lpData, 4);
            IntPtr owner = new IntPtr((uint)Marshal.ReadInt32(nid, O_HWND));
            uint uid = (uint)Marshal.ReadInt32(nid, O_UID);
            uint flags = (uint)Marshal.ReadInt32(nid, O_FLAGS);
            Guid guid = Guid.Empty;
            if ((flags & NIF_GUID) != 0 && len >= O_GUID + 16) guid = (Guid)Marshal.PtrToStructure(nid + O_GUID, typeof(Guid));

            TrayIcon icon = Icons.Find(i => guid != Guid.Empty ? i.Guid == guid : (i.HWnd == owner && i.UID == uid));
            switch (message)
            {
                case 0: // NIM_ADD
                    if (icon == null)
                    {
                        icon = new TrayIcon { HWnd = owner, UID = uid, Guid = guid };
                        Icons.Add(icon);
                    }
                    Apply(icon, nid, len, flags);
                    break;
                case 1: // NIM_MODIFY
                    if (icon == null)
                    {
                        // icon added before we started (app ignored TaskbarCreated): adopt it
                        if ((flags & NIF_ICON) == 0) return false;
                        icon = new TrayIcon { HWnd = owner, UID = uid, Guid = guid };
                        Icons.Add(icon);
                    }
                    icon.HWnd = owner;
                    Apply(icon, nid, len, flags);
                    break;
                case 2: // NIM_DELETE
                    if (icon == null) return false;
                    if (icon.Image != null) icon.Image.Dispose();
                    Icons.Remove(icon);
                    break;
                case 4: // NIM_SETVERSION
                    if (icon == null) return false;
                    if (len >= O_VERSION + 4) icon.Version = (uint)Marshal.ReadInt32(nid, O_VERSION);
                    return true;
                default:
                    return true;
            }
            FireChanged();
            return true;
        }

        static void Apply(TrayIcon icon, IntPtr nid, int len, uint flags)
        {
            if ((flags & NIF_MESSAGE) != 0) icon.CallbackMsg = (uint)Marshal.ReadInt32(nid, O_CALLBACK);
            if ((flags & NIF_TIP) != 0 && len >= O_TIP + 2)
            {
                int max = Math.Min(128, (len - O_TIP) / 2);
                var sb = new StringBuilder();
                for (int i = 0; i < max; i++)
                {
                    char c = (char)Marshal.ReadInt16(nid, O_TIP + i * 2);
                    if (c == '\0') break;
                    sb.Append(c);
                }
                icon.Tip = sb.ToString();
            }
            if ((flags & NIF_STATE) != 0 && len >= O_STATEMASK + 4)
            {
                int state = Marshal.ReadInt32(nid, O_STATE), mask = Marshal.ReadInt32(nid, O_STATEMASK);
                if ((mask & 1) != 0) icon.Hidden = (state & 1) != 0;
            }
            if ((flags & NIF_ICON) != 0)
            {
                IntPtr hicon = new IntPtr((uint)Marshal.ReadInt32(nid, O_ICON));
                Bitmap bmp = null;
                if (hicon != IntPtr.Zero)
                {
                    IntPtr copy = N.CopyIcon(hicon);
                    if (copy != IntPtr.Zero)
                    {
                        try { using (var ic = Icon.FromHandle(copy)) bmp = ic.ToBitmap(); }
                        catch { }
                        N.DestroyIcon(copy);
                    }
                }
                if (icon.Image != null) icon.Image.Dispose();
                icon.Image = bmp;
            }
        }

        static IntPtr HandleGetRect(COPYDATASTRUCT cds)
        {
            if (cds.cbData < 40) return IntPtr.Zero;
            int which = Marshal.ReadInt32(cds.lpData, 4);
            IntPtr owner = new IntPtr((uint)Marshal.ReadInt32(cds.lpData, 16));
            uint uid = (uint)Marshal.ReadInt32(cds.lpData, 20);
            Guid guid = (Guid)Marshal.PtrToStructure(cds.lpData + 24, typeof(Guid));
            var icon = Icons.Find(i => guid != Guid.Empty ? i.Guid == guid : (i.HWnd == owner && i.UID == uid));
            if (icon == null || icon.ScreenRect.Width == 0) return IntPtr.Zero;
            var r = icon.ScreenRect;
            int x = which == 1 ? r.Left : r.Right, y = which == 1 ? r.Top : r.Bottom;
            return new IntPtr((y << 16) | (x & 0xFFFF));
        }

        static void FireChanged()
        {
            if (changedTimer == null) return;
            changedTimer.Stop();
            changedTimer.Start();
        }

        /// <summary>Sends the click to the app exactly like the Windows taskbar would.</summary>
        public static void Click(TrayIcon icon, bool right, bool dbl, Point anchor)
        {
            if (!N.IsWindow(icon.HWnd)) { Prune(); return; }
            uint pid;
            N.GetWindowThreadProcessId(icon.HWnd, out pid);
            N.AllowSetForegroundWindow(pid);
            int msg = (int)icon.CallbackMsg;
            if (msg == 0) return;
            bool v4 = icon.Version >= 4;
            IntPtr wp = v4 ? new IntPtr((anchor.Y << 16) | (anchor.X & 0xFFFF)) : new IntPtr(icon.UID);
            Func<int, IntPtr> lp = m => v4 ? new IntPtr(((int)icon.UID << 16) | (m & 0xFFFF)) : new IntPtr(m);
            N.SendNotifyMessage(icon.HWnd, msg, wp, lp(0x200)); // WM_MOUSEMOVE
            if (dbl)
            {
                N.SendNotifyMessage(icon.HWnd, msg, wp, lp(0x203)); // WM_LBUTTONDBLCLK
                return;
            }
            if (right)
            {
                N.SendNotifyMessage(icon.HWnd, msg, wp, lp(0x204));
                N.SendNotifyMessage(icon.HWnd, msg, wp, lp(0x205));
                if (v4) N.SendNotifyMessage(icon.HWnd, msg, wp, lp(0x7B)); // WM_CONTEXTMENU
            }
            else
            {
                N.SendNotifyMessage(icon.HWnd, msg, wp, lp(0x201));
                N.SendNotifyMessage(icon.HWnd, msg, wp, lp(0x202));
                if (v4) N.SendNotifyMessage(icon.HWnd, msg, wp, lp(0x400)); // NIN_SELECT
            }
        }

        public static List<TrayIcon> Visible()
        {
            var l = new List<TrayIcon>();
            foreach (var i in Icons) if (!i.Hidden && i.Image != null) l.Add(i);
            return l;
        }
    }
}
