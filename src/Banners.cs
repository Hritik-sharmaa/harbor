using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace Harbor
{
    /// <summary>
    /// macOS-style notification banners, top right under the menu bar.
    /// Windows can't move its own banners, so while this is on Harbor switches them off per app
    /// ("Show notification banners" in Windows Settings) and draws its own. Notifications still land in the
    /// Notification Center. Every app Harbor switched off is remembered in HKCU\Software\Harbor and switched back on
    /// when Harbor quits, crashes, is uninstalled, or the option is turned off.
    /// </summary>
    static class Banners
    {
        const string NotifRoot = @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings";
        const string OwnKey = @"Software\Harbor", OwnValue = "BannersHidden";
        const int PollMs = 1500, LifeMs = 5000, MaxShown = 3;

        static bool running, polling, primed, checkedLeftovers;
        static Timer poll;
        static readonly HashSet<uint> seen = new HashSet<uint>();

        // ------------------------------------------------------------------ on / off

        /// <summary>Follows the "Banners" setting; call at startup and whenever settings change.</summary>
        public static void Apply()
        {
            bool want = Look.Bool("Banners");
            if (want && !running) Start();
            else if (!want && running) Stop();
            else if (!want && !checkedLeftovers) RestoreWindows(); // switched off after a crash: give Windows its banners back
            checkedLeftovers = true;
        }

        static void Start()
        {
            running = true;
            try
            {
                Async.Then(UserNotificationListener.Current.RequestAccessAsync(), access =>
                {
                    if (!running || access != UserNotificationListenerAccessStatus.Allowed) return;
                    HideAllKnown();
                    if (poll == null) { poll = new Timer { Interval = PollMs }; poll.Tick += delegate { Poll(); }; }
                    primed = false;
                    seen.Clear();
                    Poll();
                    poll.Start();
                });
            }
            catch (Exception ex) { App.Log(ex); }
        }

        static void Stop()
        {
            running = false;
            if (poll != null) poll.Stop();
            foreach (var b in live.ToArray()) b.Dispose();
            shown.Clear();
            live.Clear();
            RestoreWindows();
        }

        // ------------------------------------------------------------------ Windows' own banners

        /// <summary>Banners Windows should keep: alarms, calls and system prompts need their buttons.</summary>
        static bool KeepWindowsBanner(string aumid)
        {
            return aumid.StartsWith("Windows.SystemToast.", StringComparison.OrdinalIgnoreCase)
                || aumid.StartsWith("Windows.ActionCenter.", StringComparison.OrdinalIgnoreCase)
                || aumid.IndexOf("WindowsAlarms", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static List<string> HiddenList()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(OwnKey))
                {
                    var v = k == null ? null : k.GetValue(OwnValue) as string[];
                    return v != null ? new List<string>(v) : new List<string>();
                }
            }
            catch { return new List<string>(); }
        }

        static void SaveHiddenList(List<string> list)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(OwnKey))
                {
                    if (list.Count == 0) k.DeleteValue(OwnValue, false);
                    else k.SetValue(OwnValue, list.ToArray(), RegistryValueKind.MultiString);
                }
            }
            catch (Exception ex) { App.Log(ex); }
        }

        /// <summary>Turns off Windows' banner for every app that has ever sent a notification.</summary>
        static void HideAllKnown()
        {
            try
            {
                using (var root = Registry.CurrentUser.OpenSubKey(NotifRoot))
                    if (root != null)
                    {
                        var list = HiddenList();
                        foreach (var aumid in root.GetSubKeyNames()) HideWindowsBanner(aumid, list);
                        SaveHiddenList(list);
                    }
            }
            catch (Exception ex) { App.Log(ex); }
        }

        static void HideWindowsBanner(string aumid, List<string> list)
        {
            if (string.IsNullOrEmpty(aumid) || KeepWindowsBanner(aumid) || list.Contains(aumid)) return;
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(NotifRoot + "\\" + aumid))
                {
                    if (k.GetValue("ShowBanner") != null) return; // the user's own choice: leave it alone
                    k.SetValue("ShowBanner", 0, RegistryValueKind.DWord);
                }
                list.Add(aumid);
            }
            catch (Exception ex) { App.Log(ex); }
        }

        /// <summary>Switches Windows' banners back on for every app Harbor switched off. Safe to call from any exit path.</summary>
        public static void RestoreWindows()
        {
            var list = HiddenList();
            if (list.Count == 0) return;
            foreach (var aumid in list)
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(NotifRoot + "\\" + aumid, true))
                        if (k != null) k.DeleteValue("ShowBanner", false);
                }
                catch { }
            SaveHiddenList(new List<string>());
        }

        // ------------------------------------------------------------------ new notifications

        static void Poll()
        {
            if (polling || !running) return;
            polling = true;
            try
            {
                Async.Then(UserNotificationListener.Current.GetNotificationsAsync(NotificationKinds.Toast), list =>
                {
                    polling = false;
                    if (list == null || !running) return;
                    var present = new HashSet<uint>();
                    var fresh = new List<UserNotification>();
                    foreach (var n in list)
                    {
                        present.Add(n.Id);
                        if (seen.Add(n.Id) && primed && (DateTimeOffset.Now - n.CreationTime).TotalSeconds < 30) fresh.Add(n);
                    }
                    seen.IntersectWith(present); // forget dismissed ones so the set stays small
                    primed = true;
                    if (fresh.Count == 0) return;
                    fresh.Sort((a, b) => a.CreationTime.CompareTo(b.CreationTime));
                    var hidden = HiddenList();
                    int before = hidden.Count;
                    foreach (var n in fresh)
                    {
                        string aumid = null;
                        try { aumid = n.AppInfo.AppUserModelId; } catch { }
                        HideWindowsBanner(aumid, hidden); // a new app: Windows showed this one, Harbor shows the next
                        if (!Quiet() && (aumid == null || !KeepWindowsBanner(aumid))) Show(n, aumid);
                    }
                    if (hidden.Count != before) SaveHiddenList(hidden);
                });
            }
            catch (Exception ex) { polling = false; App.Log(ex); }
        }

        [DllImport("ntdll.dll")] static extern int NtQueryWnfStateData(ref ulong name, IntPtr type, IntPtr scope, out uint stamp, out int buf, ref uint size);
        [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);

        /// <summary>Do Not Disturb, a fullscreen game/video or a presentation: Windows wouldn't pop up either.</summary>
        static bool Quiet()
        {
            try
            {
                ulong name = 0x0D83063EA3BF1C75UL; // WNF_SHEL_QUIETHOURS_ACTIVE_PROFILE_CHANGED: 0 = off
                uint stamp, size = 4;
                int profile;
                if (NtQueryWnfStateData(ref name, IntPtr.Zero, IntPtr.Zero, out stamp, out profile, ref size) == 0 && profile != 0) return true;
            }
            catch { }
            int state;
            if (SHQueryUserNotificationState(out state) == 0 && (state == 2 || state == 3 || state == 4)) return true;
            var p = App.PrimaryDisplay;
            return p != null && p.Fullscreen;
        }

        // ------------------------------------------------------------------ showing

        static readonly List<BannerForm> shown = new List<BannerForm>(); // stacked on screen, newest first
        static readonly List<BannerForm> live = new List<BannerForm>();  // everything still animating, incl. fading out
        static Timer anim;

        static void Show(UserNotification n, string aumid)
        {
            var d = App.PrimaryDisplay;
            if (d == null) return;
            string app = "App", title = "", body = "";
            try { app = n.AppInfo.DisplayInfo.DisplayName; } catch { }
            try
            {
                var b = n.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric);
                if (b != null)
                {
                    var parts = new List<string>();
                    foreach (var t in b.GetTextElements()) if (!string.IsNullOrWhiteSpace(t.Text)) parts.Add(t.Text.Trim());
                    if (parts.Count > 0) title = parts[0];
                    if (parts.Count > 1) body = string.Join(" ", parts.GetRange(1, parts.Count - 1).ToArray());
                }
            }
            catch { }
            if (title == "" && body == "") return;

            while (shown.Count >= MaxShown) shown[shown.Count - 1].Dismiss(); // oldest at the bottom
            var f = new BannerForm(d, n.Id, aumid, app, title, body);
            shown.Insert(0, f);
            live.Add(f);
            Layout(true);
            f.Appear();
            if (anim == null) { anim = new Timer { Interval = 15 }; anim.Tick += delegate { Animate(); }; }
            anim.Start();
        }

        /// <summary>Stacks banners top-down, newest first.</summary>
        static void Layout(bool snapNew)
        {
            float y = 0;
            foreach (var b in shown)
            {
                b.TargetY = y;
                if (snapNew && b.IsNew) b.Y = y;
                y += b.LogicalHeight + 8;
            }
        }

        internal static void Removed(BannerForm b)
        {
            shown.Remove(b);
            Layout(false);
        }

        static void Animate()
        {
            foreach (var b in live.ToArray()) b.Step();
            if (live.Count == 0) anim.Stop();
        }

        internal static void Open(BannerForm b)
        {
            if (!string.IsNullOrEmpty(b.Aumid) && ShellIcons.Exists(@"shell:AppsFolder\" + b.Aumid))
                Launcher.Open(@"shell:AppsFolder\" + b.Aumid);
            try { UserNotificationListener.Current.RemoveNotification(b.Id); } catch { }
            b.Dismiss();
        }

        // ------------------------------------------------------------------ the banner window

        internal sealed class BannerForm : Form
        {
            const float W = 356, M = 14; // logical width, shadow margin

            public readonly uint Id;
            public readonly string Aumid;
            readonly string app, title, body;
            readonly Display disp;
            readonly Surface surf = new Surface();
            Bitmap logo;
            public float Y, TargetY, LogicalHeight;
            public bool IsNew = true;
            float alpha, slide = 1; // slide: 1 = off to the right, 0 = in place
            bool hover, hoverClose, leaving;
            int born, hoverLeft;
            float S { get { return disp.Scale; } }

            public BannerForm(Display d, uint id, string aumid, string app, string title, string body)
            {
                disp = d; Id = id; Aumid = aumid; this.app = app; this.title = title; this.body = body;
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                AutoScaleMode = AutoScaleMode.None;
                if (!string.IsNullOrEmpty(aumid))
                {
                    var arr = ShellIcons.LoadIcons(@"shell:AppsFolder\" + aumid, (int)Math.Round(64 * S));
                    if (arr != null) logo = arr[0];
                }
                if (logo == null) logo = Gfx.LetterIcon(app, (int)Math.Round(64 * S));
                Measure();
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

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == N.WM_MOUSEACTIVATE) { m.Result = (IntPtr)N.MA_NOACTIVATE; return; }
                base.WndProc(ref m);
            }

            Font TitleFont() { return new Font(Fonts.UiBold, 13f * S, GraphicsUnit.Pixel); }
            Font BodyFont() { return new Font(Fonts.Ui, 12.5f * S, GraphicsUnit.Pixel); }
            Font SmallFont() { return new Font(Fonts.Ui, 11.5f * S, GraphicsUnit.Pixel); }

            float bodyH;
            void Measure()
            {
                bodyH = 0;
                if (body != "")
                    using (var tmp = new Bitmap(1, 1))
                    using (var g = Graphics.FromImage(tmp))
                    using (var f = BodyFont())
                    {
                        float line = f.GetHeight(g);
                        float h = g.MeasureString(body, f, (int)((W - 82) * S)).Height;
                        bodyH = Math.Min(h, line * 2 + 1); // at most two lines, like macOS
                    }
                LogicalHeight = (54 * S + bodyH) / S + 6;
            }

            public void Appear()
            {
                born = Environment.TickCount;
                var h = Handle;
                Render();
                Place();
                N.ShowWindow(Handle, N.SW_SHOWNOACTIVATE);
            }

            public void Dismiss()
            {
                if (leaving) return;
                leaving = true;
                Banners.Removed(this);
            }

            /// <summary>One animation frame: slide in, follow the stack, time out, fade out.</summary>
            public void Step()
            {
                if (leaving)
                {
                    alpha -= 15f / 160;
                    slide = Math.Min(1, slide + 15f / 260);
                    if (alpha <= 0) { Dispose(); return; }
                }
                else
                {
                    if (alpha < 1) alpha = Math.Min(1, alpha + 15f / 180);
                    if (slide > 0) slide = Math.Max(0, slide - 15f / 280);
                    Y = Math.Abs(Y - TargetY) > 0.3f ? Y + (TargetY - Y) * 0.22f : TargetY;
                    int now = Environment.TickCount;
                    if (hover || now - hoverLeft < 1200) born = Math.Max(born, now - LifeMs + 1200); // reading it: wait
                    else if (now - born > LifeMs) Dismiss();
                }
                IsNew = false;
                Place();
            }

            void Place()
            {
                var bar = disp.Bar.ScreenBounds;
                var scr = Screen.FromHandle(disp.Bar.Handle).Bounds;
                bool barShown = N.IsWindowVisible(disp.Bar.Handle) && bar.Height > 0;
                float k = 1 - slide; k = 1 - (1 - k) * (1 - k) * (1 - k); // ease-out
                int x = scr.Right - surf.W + (int)(4 * S) + (int)Math.Round((1 - k) * 60 * S);
                int y = (barShown ? bar.Bottom : scr.Top) + (int)Math.Round(Y * S) - (int)(4 * S);
                float a = alpha * alpha * (3 - 2 * alpha);
                surf.Present(Handle, x, y, (byte)Math.Round(Math.Max(0, Math.Min(1, a)) * 255));
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                bool c = CloseRect().Contains(e.Location);
                if (!hover || c != hoverClose) { hover = true; hoverClose = c; Render(); Place(); }
                Cursor = Cursors.Default;
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                hover = false; hoverClose = false; hoverLeft = Environment.TickCount;
                Render(); Place();
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                if (leaving) return;
                if (e.Button == MouseButtons.Right || CloseRect().Contains(e.Location)) { Dismiss(); return; }
                if (e.Button == MouseButtons.Left) Banners.Open(this);
            }

            RectangleF Card() { return new RectangleF(M * S, M * S, W * S, LogicalHeight * S - 6 * S); }
            RectangleF CloseRect() { var r = Card(); return new RectangleF(r.X - 7 * S, r.Y - 7 * S, 22 * S, 22 * S); }

            void Render()
            {
                var r = Card();
                int w = (int)Math.Ceiling(r.Width + 2 * M * S), h = (int)Math.Ceiling(r.Height + 2 * M * S);
                surf.Ensure(w, h);
                var g = surf.G;
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                bool dark = Theme.Dark;
                float rad = 18 * S;

                for (int i = 1; i <= 7; i++)
                    using (var sp = Gfx.RoundRect(new RectangleF(r.X - i * S, r.Y - i * S + 4 * S, r.Width + 2 * i * S, r.Height + 2 * i * S), rad + i * S))
                    using (var sb = new SolidBrush(Color.FromArgb(dark ? 10 : 6, 0, 0, 0)))
                        g.FillPath(sb, sp);
                using (var p = Gfx.RoundRect(r, rad))
                {
                    var bg = dark ? Color.FromArgb(244, hover ? 44 : 36, hover ? 44 : 36, hover ? 48 : 40) : Color.FromArgb(246, hover ? 236 : 244, hover ? 236 : 244, hover ? 240 : 246);
                    using (var br = new SolidBrush(bg)) g.FillPath(br, p);
                    using (var hl = new LinearGradientBrush(r, Color.FromArgb(dark ? 16 : 70, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f)) g.FillPath(hl, p);
                    using (var pen = new Pen(dark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(24, 0, 0, 0), 1)) g.DrawPath(pen, p);
                }

                var text = dark ? Color.FromArgb(242, 255, 255, 255) : Color.FromArgb(230, 0, 0, 0);
                var dim = dark ? Color.FromArgb(150, 255, 255, 255) : Color.FromArgb(130, 0, 0, 0);
                float pad = 14 * S;
                var lr = new RectangleF(r.X + pad, r.Y + (r.Height - 38 * S) / 2, 38 * S, 38 * S);
                if (r.Height > 80 * S) lr.Y = r.Y + 14 * S;
                g.DrawImage(logo, lr);

                float tx = lr.Right + 12 * S, tw = r.Right - pad - tx;
                using (var tf = TitleFont())
                using (var bf = BodyFont())
                using (var sf = SmallFont())
                using (var tb = new SolidBrush(text))
                using (var db = new SolidBrush(dim))
                using (var one = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                using (var right = new StringFormat { Alignment = StringAlignment.Far, FormatFlags = StringFormatFlags.NoWrap })
                using (var wrap = new StringFormat { Trimming = StringTrimming.EllipsisWord })
                {
                    float y = r.Y + 11 * S;
                    g.DrawString("now", sf, db, new RectangleF(tx, y, tw, 16 * S), right);
                    g.DrawString(app, sf, db, new RectangleF(tx, y, tw - 40 * S, 16 * S), one);
                    y += 17 * S;
                    g.DrawString(title, tf, tb, new RectangleF(tx, y, tw, 19 * S), one);
                    y += 19 * S;
                    if (bodyH > 0) g.DrawString(body, bf, db, new RectangleF(tx, y, tw, bodyH), wrap);
                }

                if (hover)
                {
                    var c = CloseRect();
                    using (var br = new SolidBrush(dark ? Color.FromArgb(255, hoverClose ? 70 : 54, hoverClose ? 70 : 54, hoverClose ? 74 : 58) : Color.FromArgb(255, hoverClose ? 220 : 250, hoverClose ? 220 : 250, hoverClose ? 224 : 252)))
                        g.FillEllipse(br, c);
                    using (var pen = new Pen(dark ? Color.FromArgb(50, 255, 255, 255) : Color.FromArgb(40, 0, 0, 0), 1)) g.DrawEllipse(pen, c);
                    using (var f = new Font(Fonts.Icons, 7.5f * S, GraphicsUnit.Pixel))
                    using (var b = new SolidBrush(text))
                    using (var cf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        g.DrawString("", f, b, c, cf);
                }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    surf.Dispose();
                    if (logo != null) { logo.Dispose(); logo = null; }
                    if (!leaving) Banners.Removed(this);
                    live.Remove(this);
                }
                base.Dispose(disposing);
            }

            /// <summary>--snapshot: render without showing.</summary>
            public Bitmap Snapshot()
            {
                hover = false;
                Render();
                surf.G.Flush(FlushIntention.Sync);
                return new Bitmap(surf.Bitmap); // a real copy: Clone() still points at the window's DIB
            }
        }

        /// <summary>--snapshot: a sample banner as a bitmap.</summary>
        public static Bitmap Snapshot(Display d)
        {
            using (var f = new BannerForm(d, 0, null, "Messages", "Alex", "Are we still on for dinner tonight? I booked a table for 8."))
                return f.Snapshot();
        }
    }
}
