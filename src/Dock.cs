using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Windows.Forms;

namespace Harbor
{
    enum ItemKind { App, Folder, File, Trash, Separator, Window }

    sealed class DockItem
    {
        public string Path;
        public ItemKind Kind;
        public bool Pinned, Fixed;
        public string ExeKey, AumidKey, Exe;
        public string Name;
        public Bitmap Big, Small;
        public string IconKey, IconVariant;
        public string Badge;       // unread count parsed from window titles, e.g. "(3) WhatsApp"
        public bool Attention;     // window flashed for attention and hasn't been looked at yet
        public IntPtr WinHwnd;     // minimized-window items
        public string IconOverride; // web apps: the icon file the browser gives the window
        public int IconPx;
        public List<WinInfo> Wins = new List<WinInfo>();
        // last layout (window coordinates)
        public float X, Y, Size, SlotL, SlotR;
        // bounce animation
        public long BounceStart = -1, BounceUntil;
        public bool LaunchBounce;

        public bool Running { get { return Wins.Count > 0; } }
        public bool IsSep { get { return Kind == ItemKind.Separator; } }

        public bool Matches(WinInfo w)
        {
            return (ExeKey != null && w.Key == ExeKey) || (AumidKey != null && w.Key == AumidKey);
        }
    }

    /// <summary>Blur-behind backing window for the dock panel (sits right under the icon layer).</summary>
    sealed class DockBack : Form
    {
        public bool BlurOk;
        readonly Surface clear = new Surface();
        int lx = int.MinValue, ly, lw, lh, lr;

        public DockBack()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            Bounds = new Rectangle(-10, -10, 1, 1);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.Style = N.WS_POPUP;
                cp.ExStyle |= N.WS_EX_TOOLWINDOW | N.WS_EX_NOACTIVATE | N.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            BlurOk = N.EnableBlur(Handle, true);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            // fully transparent pixels -> DWM shows the blur
            clear.Ensure(Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height));
            clear.G.Clear(Color.Transparent);
            IntPtr hdc = e.Graphics.GetHdc();
            clear.BlitTo(hdc);
            e.Graphics.ReleaseHdc(hdc);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == N.WM_MOUSEACTIVATE) { m.Result = (IntPtr)N.MA_NOACTIVATE; return; }
            base.WndProc(ref m);
        }

        public void Place(int x, int y, int w, int h, int r)
        {
            if (w < 1 || h < 1) { Conceal(); return; }
            if (x == lx && y == ly && w == lw && h == lh && r == lr) { Reveal(); return; }
            bool resized = w != lw || h != lh || r != lr;
            lx = x; ly = y; lw = w; lh = h; lr = r;
            N.SetWindowPos(Handle, Below, x, y, w, h, N.SWP_NOACTIVATE | N.SWP_NOOWNERZORDER | (Below == IntPtr.Zero ? N.SWP_NOZORDER : 0));
            if (resized) N.SetWindowRgn(Handle, N.CreateRoundRectRgn(0, 0, w + 1, h + 1, r * 2, r * 2), true);
            Reveal();
        }

        /// <summary>When set, this blur layer is kept directly under that window (menu-bar islands).</summary>
        public IntPtr Below;

        bool shown;
        public void Reveal()
        {
            if (shown) return;
            N.ShowWindow(Handle, N.SW_SHOWNOACTIVATE);
            if (Below != IntPtr.Zero) N.SetWindowPos(Handle, Below, 0, 0, 0, 0, N.SWP_NOACTIVATE | N.SWP_NOMOVE | N.SWP_NOSIZE);
            shown = true;
        }
        public void Conceal() { if (shown) { N.ShowWindow(Handle, N.SW_HIDE); shown = false; } }
    }

    /// <summary>The dock: a per-pixel-alpha layered window rendered with GDI+ into a DIB (no GPU/browser runtime).</summary>
    sealed class Dock : Form
    {
        const int BouncePeriod = 620;

        readonly DockBack back;
        readonly Surface surf = new Surface();
        readonly ImageAttributes wrapAttr = new ImageAttributes();
        AppBar bar;

        List<DockItem> items = new List<DockItem>();
        readonly Dictionary<string, DockItem> pinnedCache = new Dictionary<string, DockItem>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, DockItem> runCache = new Dictionary<string, DockItem>();
        readonly List<string> runOrder = new List<string>();
        DockItem downloads, trash;
        readonly DockItem sep1 = new DockItem { Kind = ItemKind.Separator }, sep2 = new DockItem { Kind = ItemKind.Separator };
        bool trashEmpty = true;

        // metrics (pixels)
        float S = 1, B, M, gap, sepW, padX, padTop, padBottom, panelH, margin, radius, falloff;
        int winW, winH, winX, winY;
        int bottomEdge, monLeft, monWidth, monTop, monHeight;
        int AW, AH;                // actual window size (virtual layout is always horizontal)
        string pos = "bottom";
        bool ruleHidden;
        public readonly Display Disp;

        // interaction state
        bool hovering, dragging, dragRemove, hidden;
        float mx = -1, my = -1;
        float mag;          // 0..1 magnification amount (animated)
        float shown = 1;    // 0..1 autohide visibility (animated)
        int hoverIdx = -1, pressIdx = -1;
        Point pressPt;
        DockItem dragItem;
        long lastKeep;

        // last layout
        float panelL, panelR, panelTop, off;

        // animation
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly Timer anim = new Timer { Interval = 15 };
        bool animOn, dirty;
        long lastTick, lastRender;

        public Dock(DockBack back, Display disp)
        {
            this.back = back;
            Disp = disp;
            Flyout.AnyClosed += () => { if (!IsDisposed) Kick(); }; // our menu closed: hide again if we should
            Owner = back; // owned => always drawn above the blur layer
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            AllowDrop = true;
            wrapAttr.SetWrapMode(WrapMode.TileFlipXY);
            anim.Tick += delegate { Tick(); };

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

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            bar = new AppBar(Handle, "dock" + Disp.Index);
            bar.PosChanged += delegate { if (!App.SnapshotMode) Reposition(); };
            bar.FullScreenChanged += delegate { App.CheckFullscreenSoon(); };
        }

        protected override void WndProc(ref Message m)
        {
            if (bar != null && bar.Handle(ref m)) return;
            if (m.Msg == N.WM_MOUSEACTIVATE) { m.Result = (IntPtr)N.MA_NOACTIVATE; return; }
            if (m.Msg == N.WM_DPICHANGED) { App.ScheduleRelayout(); return; }
            base.WndProc(ref m);
        }

        public void Unregister() { if (bar != null) bar.Unregister(); }
        public void ResetAppBar() { if (bar != null) bar.Unregister(); }

        // ------------------------------------------------------------------ model

        DockItem MakeItem(string path, bool pinned)
        {
            var it = new DockItem { Path = path, Pinned = pinned };
            string p = ShellIcons.ParsingName(path);
            if (p.StartsWith(@"shell:AppsFolder\", StringComparison.OrdinalIgnoreCase))
            {
                it.Kind = ItemKind.App;
                it.AumidKey = "aumid:" + p.Substring(17).ToLowerInvariant();
            }
            else if (p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                it.Kind = ItemKind.App;
                it.Exe = p;
                it.ExeKey = "exe:" + p.ToLowerInvariant();
            }
            else if (p.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                string t = ShellIcons.ResolveLnk(p);
                if (t != null && t.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    it.Kind = ItemKind.App;
                    it.Exe = t;
                    it.ExeKey = "exe:" + t.ToLowerInvariant();
                }
                else it.Kind = t != null && Directory.Exists(t) ? ItemKind.Folder : ItemKind.App;
            }
            else if (Directory.Exists(p) || p.StartsWith("::{") || p.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                it.Kind = ItemKind.Folder;
            else
                it.Kind = ItemKind.File;
            it.Name = ShellIcons.DisplayName(path);
            return it;
        }

        DockItem MakeRunning(WinInfo w)
        {
            string path = w.Aumid != null ? @"shell:AppsFolder\" + w.Aumid : w.Exe;
            DockItem it;
            if (path != null) it = MakeItem(path, false);
            else it = new DockItem { Kind = ItemKind.App, Name = N.TitleOf(w.Hwnd) };
            if (w.Key.StartsWith("pid:")) it.Path = null;
            // make sure this item matches the window that created it
            if (w.Aumid != null) it.AumidKey = w.Key; else it.ExeKey = w.Key;
            if (w.WebAppName != null) it.Name = w.WebAppName;
            if (w.WebAppIcon != null && System.IO.File.Exists(w.WebAppIcon)) it.IconOverride = w.WebAppIcon;
            return it;
        }

        void EnsureIcon(DockItem it)
        {
            int big = (int)Math.Ceiling(M), small = (int)Math.Round(B);
            string variant = (it.Kind == ItemKind.Trash ? (trashEmpty ? "empty" : "full") : "")
                + (Settings.IconStyle == "tiles" ? (Theme.Dark ? "tile-dark" : "tile-light") : (Settings.IconStyle == "whitesur" ? "whitesur" + LookFor(it) : ""));
            if (it.Big != null && it.IconPx == big && it.Small != null && it.Small.Width == small && it.IconVariant == variant) return;
            DisposeIcons(it);
            string key;
            Bitmap[] icons = IconCache.Acquire(IconSource(it), it.Name, big, small, variant, out key);
            it.IconKey = key;
            it.IconVariant = variant;
            it.Big = icons[0];
            it.Small = icons[1];
            it.IconPx = big;
        }

        static string LookFor(DockItem it)
        {
            string l;
            return it.Path != null && Settings.IconLooks.TryGetValue(it.Path, out l) ? "|look=" + l : "";
        }

        static string IconSource(DockItem it)
        {
            string custom;
            if (it.Path != null && Settings.CustomIcons.TryGetValue(it.Path, out custom) && System.IO.File.Exists(custom)) return custom;
            if (it.IconOverride != null) return it.IconOverride;
            return it.Path;
        }

        readonly Dictionary<IntPtr, DockItem> minCache = new Dictionary<IntPtr, DockItem>();

        public void Rebuild()
        {
            var list = new List<DockItem>();
            var pinnedNow = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string p in Settings.Pinned)
            {
                if (!pinnedNow.Add(p)) continue;
                DockItem it;
                if (!pinnedCache.TryGetValue(p, out it)) { it = MakeItem(p, true); pinnedCache[p] = it; }
                it.Pinned = true;
                it.Wins.Clear();
                list.Add(it);
            }
            // drop cache entries for unpinned paths
            var stale = new List<string>();
            foreach (var k in pinnedCache.Keys) if (!pinnedNow.Contains(k)) stale.Add(k);
            foreach (var k in stale) { DisposeIcons(pinnedCache[k]); pinnedCache.Remove(k); }

            // assign windows
            var groups = new Dictionary<string, List<WinInfo>>();
            var groupFirst = new List<string>();
            foreach (var w in Tracker.Windows)
            {
                DockItem owner = null;
                foreach (var it in list) if (it.Matches(w)) { owner = it; break; }
                if (owner != null) { owner.Wins.Add(w); continue; }
                if (Settings.HiddenApps.Contains(w.Key)) continue;
                List<WinInfo> g;
                if (!groups.TryGetValue(w.Key, out g)) { g = new List<WinInfo>(); groups[w.Key] = g; groupFirst.Add(w.Key); }
                g.Add(w);
            }

            runOrder.RemoveAll(k => !groups.ContainsKey(k));
            foreach (var k in groupFirst) if (!runOrder.Contains(k)) runOrder.Add(k);
            var runItems = new List<DockItem>();
            foreach (var k in runOrder)
            {
                DockItem it;
                if (!runCache.TryGetValue(k, out it)) { it = MakeRunning(groups[k][0]); runCache[k] = it; }
                it.Pinned = false;
                it.Wins.Clear();
                it.Wins.AddRange(groups[k]);
                runItems.Add(it);
            }
            var deadRun = new List<string>();
            foreach (var k in runCache.Keys) if (!groups.ContainsKey(k)) deadRun.Add(k);
            foreach (var k in deadRun) { DisposeIcons(runCache[k]); runCache.Remove(k); }

            if (Settings.ShowRunning && runItems.Count > 0)
            {
                list.Add(sep1);
                list.AddRange(runItems);
            }

            if (downloads == null)
            {
                downloads = MakeItem(ShellIcons.Downloads, false);
                downloads.Fixed = true;
                trash = new DockItem { Path = ShellIcons.RecycleBin, Kind = ItemKind.Trash, Name = "Recycle Bin", Fixed = true };
                trashEmpty = ShellIcons.RecycleBinEmpty();
            }
            // minimized windows sit on the right, like macOS "minimize windows into the dock"
            var minItems = new List<DockItem>();
            var seenMin = new HashSet<IntPtr>();
            if (Look.Bool("DockMinimized"))
                foreach (var w in Tracker.Windows)
                {
                    if (!N.IsIconic(w.Hwnd)) continue;
                    DockItem mi;
                    if (!minCache.TryGetValue(w.Hwnd, out mi))
                    {
                        mi = MakeRunning(w);
                        mi.Kind = ItemKind.Window;
                        mi.WinHwnd = w.Hwnd;
                        minCache[w.Hwnd] = mi;
                    }
                    mi.Pinned = false;
                    mi.Name = N.TitleOf(w.Hwnd);
                    seenMin.Add(w.Hwnd);
                    minItems.Add(mi);
                }
            foreach (var k in new List<IntPtr>(minCache.Keys))
                if (!seenMin.Contains(k)) { DisposeIcons(minCache[k]); minCache.Remove(k); }

            list.Add(sep2);
            list.Add(downloads);
            list.AddRange(minItems);
            list.Add(trash);
            foreach (var it in list) if (it.Wins.Count > 0) UpdateBadge(it);

            long now = clock.ElapsedMilliseconds;
            foreach (var it in list)
            {
                if (it.IsSep) continue;
                EnsureIcon(it);
                if (it.LaunchBounce && it.Running) { it.LaunchBounce = false; it.BounceUntil = now; }
            }

            items = list;
            if (dragging && !items.Contains(dragItem)) EndDrag(false);
            hoverIdx = -1;
            UpdateSize();
            RequestRender();
        }

        // only real unread-count patterns: "(3) WhatsApp", "Inbox (12) - Gmail", "Chats (5)" — never years like "Movie (2013)"
        static readonly System.Text.RegularExpressions.Regex countRx = new System.Text.RegularExpressions.Regex(@"^\s*\((\d{1,3})\+?\)|\b(?:inbox|chats?|messages?|unread|notifications?)\s*\((\d{1,3})\+?\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        /// <summary>Unread counts most apps put in their window titles: "(3) WhatsApp", "Inbox (12) - Gmail".</summary>
        static void UpdateBadge(DockItem it)
        {
            int best = 0;
            foreach (var w in it.Wins)
            {
                var m = countRx.Match(N.TitleOf(w.Hwnd));
                string g1 = m.Success ? (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value) : null;
                int n;
                if (g1 != null && int.TryParse(g1, out n)) best = Math.Max(best, n);
            }
            it.Badge = best <= 0 ? null : (best > 99 ? "99+" : best.ToString());
        }

        public void UpdateBadges()
        {
            foreach (var it in items) if (it.Wins.Count > 0) UpdateBadge(it);
            RequestRender();
        }

        public void ClearAttention()
        {
            IntPtr fg = Tracker.Foreground;
            bool changed = false;
            foreach (var it in items)
                if (it.Attention && it.Wins.Exists(w => w.Hwnd == fg)) { it.Attention = false; changed = true; }
            if (changed) RequestRender();
        }

        static void DisposeIcons(DockItem it)
        {
            IconCache.Release(it.IconKey);
            it.IconKey = null;
            it.Big = it.Small = null;
        }

        public void CheckTrash()
        {
            if (trash == null) return;
            bool e = ShellIcons.RecycleBinEmpty();
            if (e == trashEmpty) return;
            trashEmpty = e;
            DisposeIcons(trash);
            EnsureIcon(trash);
            RequestRender();
        }

        // ------------------------------------------------------------------ geometry

        void Metrics()
        {
            S = Disp.Scale;
            B = (float)Math.Round(Settings.IconSize * S);
            M = Settings.Magnify ? (float)Math.Round(B * Settings.MagnifyPercent / 100f) : B;
            // every value below comes from the Harbor app (Dock page)
            gap = (float)Math.Round(Look.Int("DockSpacing") * S);
            sepW = (float)Math.Round(Math.Max(9, Look.Int("DockSpacing") + 9) * S);
            padX = (float)Math.Round(Look.Int("DockPadH") * S);
            padTop = (float)Math.Round(Look.Int("DockPadV") * S);
            padBottom = (float)Math.Round((Look.Int("DockPadV") + (Look.Bool("DockDots") ? 4 : 0)) * S);
            panelH = padTop + B + padBottom;
            margin = (float)Math.Round(Look.Int("DockMargin") * S);
            radius = (float)Math.Round(Math.Min(panelH / 2f, Look.Int("DockRadius") * S));
            falloff = (B + gap) * 3.1f;
        }

        float BaseWidth()
        {
            float w = 2 * padX;
            foreach (var it in items) w += it.IsSep ? sepW : B + gap;
            return w;
        }

        void UpdateSize()
        {
            if (B <= 0) return;
            bool vert = Vertical;
            int mainLen = vert ? monHeight : monWidth;
            // too many icons for the screen? shrink to fit
            float avail = mainLen - 40 * S;
            float bw = BaseWidth();
            if (bw > avail && items.Count > 0)
            {
                float k = Math.Max(0.4f, avail / bw);
                B = (float)Math.Floor(B * k);
                M = Settings.Magnify ? (float)Math.Round(B * Settings.MagnifyPercent / 100f) : B;
                panelH = padTop + B + padBottom;
                falloff = (B + gap) * 3.1f;
                bw = BaseWidth();
            }
            // virtual (horizontal) layout size; side docks need extra room for the name labels
            float extra = (M - B) * 3.4f * 2 + (vert ? 60 : 260) * S;
            winW = (int)Math.Min(mainLen, Math.Ceiling(bw + extra));
            winH = (int)Math.Ceiling(margin + panelH + Math.Max(M - B, B * 0.62f) + (vert ? 200 : 40) * S);
            AW = vert ? winH : winW;
            AH = vert ? winW : winH;
            if (pos == "left") { winX = bottomEdge; winY = monTop + (monHeight - AH) / 2; }
            else if (pos == "right") { winX = bottomEdge - AW; winY = monTop + (monHeight - AH) / 2; }
            else { winX = monLeft + (monWidth - AW) / 2; winY = bottomEdge - AH; }
        }

        bool Vertical { get { return pos != "bottom"; } }

        /// <summary>Virtual layout rect -> real window rect (the virtual "screen edge" is the bottom).</summary>
        RectangleF MapR(RectangleF r)
        {
            if (pos == "left") return new RectangleF(winH - r.Bottom, r.X, r.Height, r.Width);
            if (pos == "right") return new RectangleF(r.Y, r.X, r.Height, r.Width);
            return r;
        }

        PointF MapP(float x, float y)
        {
            if (pos == "left") return new PointF(winH - y, x);
            if (pos == "right") return new PointF(y, x);
            return new PointF(x, y);
        }

        /// <summary>Real window point -> virtual layout point.</summary>
        PointF ToVirtual(float ax, float ay)
        {
            if (pos == "left") return new PointF(ay, winH - ax);
            if (pos == "right") return new PointF(ay, ax);
            return new PointF(ax, ay);
        }

        /// <summary>Screen rect of an item plus where popups should open (above / beside the dock).</summary>
        public Rectangle ItemScreenRect(DockItem it)
        {
            var r = MapR(new RectangleF(it.X, it.Y, it.Size, it.Size));
            return new Rectangle(winX + (int)r.X, winY + (int)r.Y, (int)r.Width, (int)r.Height);
        }

        /// <summary>Opens a flyout next to an item, on the side facing the screen.</summary>
        // the menu or stack this dock opened: only that keeps an auto-hidden dock up (not the island or the bar's panels)
        Flyout ownPanel;
        bool MenuOpen { get { return ownPanel != null && ownPanel.Visible; } }

        public void OpenBeside(Flyout f, DockItem it)
        {
            ownPanel = f;
            var r = ItemScreenRect(it);
            if (pos == "left") f.Open(new Rectangle(r.Right + (int)(8 * S), r.Y, 0, 0), false, 0, S, null);
            else if (pos == "right") f.Open(new Rectangle(r.X - (int)(8 * S), r.Y, 0, 0), false, 2, S, null);
            else f.Open(new Rectangle(r.X, winY + (int)(panelTop + off) - (int)(4 * S), r.Width, 0), true, 1, S, null);
        }

        public void Reposition()
        {
            Metrics();
            pos = Look.Get("DockPosition");
            if (pos != "left" && pos != "right") pos = "bottom";
            RECT mon = N.MonitorInfo(Disp.HMon).rcMonitor;
            // screen still waking up (or a stale handle): keep the current layout and look again shortly
            if (mon.Width <= 0 || mon.Height <= 0) { App.RetryRelayoutSoon(); return; }
            monLeft = mon.Left;
            monWidth = mon.Width;
            monTop = mon.Top;
            monHeight = mon.Height;
            uint edge = pos == "left" ? 0u : (pos == "right" ? 2u : N.ABE_BOTTOM);
            if (App.SnapshotMode) bottomEdge = pos == "left" ? mon.Left : (pos == "right" ? mon.Right : mon.Bottom);
            else if (!Settings.AutoHide)
            {
                bar.Register();
                int reserve = (int)Math.Ceiling(margin + panelH + 3 * S);
                RECT r = bar.SetPos(edge, mon, reserve);
                bottomEdge = pos == "left" ? r.Left : (pos == "right" ? r.Right : r.Bottom);
            }
            else
            {
                bar.Unregister();
                RECT wk = N.MonitorInfo(Disp.HMon).rcWork;
                bottomEdge = pos == "left" ? wk.Left : (pos == "right" ? wk.Right : wk.Bottom);
            }
            foreach (var it in items) if (!it.IsSep) EnsureIcon(it);
            UpdateSize();
            Render();
        }

        public void Reconfigure()
        {
            Metrics();
            Rebuild();
            Reposition();
        }

        /// <summary>Lays out items for the current mouse position and magnification.</summary>
        void LayoutItems()
        {
            int n = items.Count;
            float baseTotal = BaseWidth() - 2 * padX;
            float baseLeft = (winW - baseTotal) / 2f;
            float k = Smooth(mag);
            float[] slot = new float[n], size = new float[n];
            float cum = 0, magTotal = 0;
            for (int i = 0; i < n; i++)
            {
                var it = items[i];
                float s0 = it.IsSep ? sepW : B + gap;
                float c = baseLeft + cum + s0 / 2f;
                cum += s0;
                if (it.IsSep) { slot[i] = sepW; size[i] = 0; }
                else
                {
                    float f = 0;
                    if (k > 0)
                    {
                        float d = Math.Abs(mx - c);
                        if (d < falloff) f = (float)(Math.Cos(Math.PI * d / falloff) + 1) / 2f;
                    }
                    size[i] = B + (M - B) * f * k;
                    slot[i] = size[i] + gap;
                }
                magTotal += slot[i];
            }

            // anchor: the point under the cursor stays under the cursor while icons grow around it
            float magLeft = baseLeft;
            if (k > 0 && n > 0)
            {
                float pb = Math.Max(0, Math.Min(baseTotal, mx - baseLeft));
                float acc = 0, accM = 0, pm = magTotal;
                for (int i = 0; i < n; i++)
                {
                    float s0 = items[i].IsSep ? sepW : B + gap;
                    if (pb <= acc + s0) { pm = accM + (pb - acc) / s0 * slot[i]; break; }
                    acc += s0;
                    accM += slot[i];
                }
                magLeft = baseLeft + pb - pm;
            }

            float panelBottom = winH - margin;
            panelTop = panelBottom - panelH;
            off = (1 - Smooth(shown)) * (panelH + margin + 2);
            float iconBottom = panelTop + padTop + B + off;
            long now = clock.ElapsedMilliseconds;
            float x = magLeft;
            for (int i = 0; i < n; i++)
            {
                var it = items[i];
                it.SlotL = x;
                it.SlotR = x + slot[i];
                if (!it.IsSep)
                {
                    it.Size = size[i];
                    it.X = x + gap / 2f;
                    it.Y = iconBottom - size[i] - BounceOffset(it, now);
                }
                x += slot[i];
            }
            panelL = magLeft - padX;
            panelR = magLeft + magTotal + padX;
        }

        static float Smooth(float t) { return t * t * (3 - 2 * t); }

        float BounceOffset(DockItem it, long now)
        {
            if (it.BounceStart < 0) return 0;
            long t = now - it.BounceStart;
            long span = Math.Max(1, (it.LaunchBounce ? Math.Max(it.BounceUntil, now) : it.BounceUntil) - it.BounceStart);
            long bounces = Math.Max(1, (span + BouncePeriod - 1) / BouncePeriod);
            if (t >= bounces * BouncePeriod) { it.BounceStart = -1; it.LaunchBounce = false; return 0; }
            double ph = (t % BouncePeriod) / (double)BouncePeriod;
            return (float)(4 * ph * (1 - ph) * B * 0.5);
        }

        bool AnyBouncing()
        {
            foreach (var it in items) if (it.BounceStart >= 0) return true;
            return false;
        }

        int HitTest(float x, float y)
        {
            float bandTop = panelTop + off - (M - B) * Smooth(mag) - 6 * S;
            if (y < bandTop || y > winH) return -1;
            for (int i = 0; i < items.Count; i++)
                if (!items[i].IsSep && x >= items[i].SlotL && x < items[i].SlotR) return i;
            return -1;
        }

        // ------------------------------------------------------------------ rendering

        public void RequestRender()
        {
            long now = clock.ElapsedMilliseconds;
            if (now - lastRender >= 8) Render();
            else { dirty = true; Kick(); }
        }

        public void Render()
        {
            if (!IsHandleCreated || winW <= 0 || winH <= 0 || hidden) return;
            dirty = false;
            lastRender = clock.ElapsedMilliseconds;
            surf.Ensure(AW, AH);
            var g = surf.G;
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.InterpolationMode = (animOn || mag > 0) ? InterpolationMode.HighQualityBilinear : InterpolationMode.HighQualityBicubic;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            LayoutItems();
            bool blur = Look.Bool("DockBlur") && back.BlurOk && !App.SnapshotMode;
            bool visible = shown > 0.001f;

            if (visible)
            {
                var vr = new RectangleF(panelL, panelTop + off, panelR - panelL, panelH);
                var pr = MapR(vr);
                if (blur)
                {
                    // clip the blur layer to the window so a sliding (auto-hide) dock doesn't blur past the edge
                    var clipR = RectangleF.Intersect(pr, new RectangleF(0, 0, AW, AH));
                    if (clipR.Width > 1 && clipR.Height > 1)
                        back.Place(winX + (int)Math.Round(clipR.X), winY + (int)Math.Round(clipR.Y),
                            (int)Math.Round(clipR.Width), (int)Math.Round(clipR.Height), (int)radius);
                    else back.Conceal();
                }
                else back.Conceal();

                using (var panelPath = Gfx.RoundRect(pr, Math.Max(0.5f, radius)))
                {
                    if (Look.Bool("DockShadow"))
                        using (var br = new SolidBrush(Color.FromArgb(Theme.Dark ? 12 : 8, 0, 0, 0)))
                            for (int i = 4; i >= 1; i--)
                            {
                                float sp = i * 2.2f * S;
                                var sr = MapR(new RectangleF(vr.X - sp, vr.Y - sp * 0.6f + 2.5f * S, vr.Width + sp * 2, vr.Height + sp * 1.6f));
                                using (var sp2 = Gfx.RoundRect(sr, radius + sp)) g.FillPath(br, sp2);
                            }
                    var fill = Look.DockFill;
                    if (fill.A < 2) fill = Color.FromArgb(2, fill); // fully transparent pixels would let clicks fall through
                    using (var br = new SolidBrush(fill)) g.FillPath(br, panelPath);
                }
                float bw = Look.Int("DockBorderWidth") * S;
                if (bw > 0 && Look.DockBorder.A > 0)
                {
                    var br2 = RectangleF.Inflate(pr, -bw / 2, -bw / 2);
                    using (var path = Gfx.RoundRect(br2, Math.Max(0.5f, radius - bw / 2)))
                    using (var pen = new Pen(Look.DockBorder, bw))
                        g.DrawPath(pen, path);
                }

                // hover-capture band so the cursor isn't "lost" between magnified icons
                if (hovering || mag > 0.01f)
                {
                    float bandTop = panelTop + off - (M - B) * Smooth(mag) - 6 * S;
                    using (var br = new SolidBrush(Color.FromArgb(1, 0, 0, 0)))
                        g.FillRectangle(br, MapR(new RectangleF(panelL, bandTop, panelR - panelL, panelTop + off - bandTop)));
                }

                for (int i = 0; i < items.Count; i++)
                {
                    var it = items[i];
                    if (it.IsSep)
                    {
                        float cx = (float)Math.Round((it.SlotL + it.SlotR) / 2f) + 0.5f;
                        var a = MapP(cx, panelTop + off + padTop + 2 * S);
                        var b = MapP(cx, panelTop + off + panelH - padBottom + 1 * S);
                        using (var pen = new Pen(Theme.Separator, Math.Max(1f, S))) g.DrawLine(pen, a, b);
                        continue;
                    }
                    if (dragging && it == dragItem) continue;
                    var ir = MapR(new RectangleF(it.X, it.Y, it.Size, it.Size));
                    DrawIcon(g, it, ir.X, ir.Y, it.Size, i == pressIdx && hoverIdx == i, it.Kind == ItemKind.Window ? 0.85f : 1f);
                    if (it.Kind == ItemKind.Window) DrawWindowMark(g, ir);
                    if (it.Running && Look.Bool("DockDots"))
                    {
                        float r = Math.Max(1.5f, 2f * S);
                        var c = MapP(it.X + it.Size / 2f, panelTop + off + panelH - padBottom / 2f + 0.5f * S);
                        using (var br = new SolidBrush(Theme.Dot))
                            g.FillEllipse(br, c.X - r, c.Y - r, r * 2, r * 2);
                    }
                    if (Look.Bool("DockBadges")) DrawBadge(g, it, ir);
                }

                if (dragging && dragItem != null)
                {
                    float sz = B * 1.08f;
                    var dr = MapR(new RectangleF(mx - sz / 2f, my - sz / 2f, sz, sz));
                    DrawIcon(g, dragItem, dr.X, dr.Y, sz, false, dragRemove ? 0.45f : 0.9f);
                    if (dragRemove) DrawLabel(g, "Remove", dr);
                }
                else if (hoverIdx >= 0 && hoverIdx < items.Count && !MenuOpen && !Previews.ShowingFor(this) && (mag > 0.3f || !Settings.Magnify))
                {
                    var it = items[hoverIdx];
                    DrawLabel(g, it.Name, MapR(new RectangleF(it.X, it.Y, it.Size, it.Size)));
                }
            }
            else back.Conceal();

            if (Settings.AutoHide)
            {
                // 2px trigger strip at the screen edge to reveal the dock
                float bw2 = BaseWidth();
                using (var br = new SolidBrush(Color.FromArgb(1, 0, 0, 0)))
                    g.FillRectangle(br, MapR(new RectangleF((winW - bw2) / 2f, winH - 2 * S, bw2, 2 * S)));
            }

            if (!App.SnapshotMode) surf.Present(Handle, winX, winY);
        }

        /// <summary>Red count bubble (from window titles) or an attention dot.</summary>
        void DrawBadge(Graphics g, DockItem it, RectangleF ir)
        {
            string text = it.Badge;
            if (text == null && !it.Attention) return;
            using (var f = new Font(Fonts.UiBold, Math.Max(9f, it.Size * 0.24f), GraphicsUnit.Pixel))
            {
                float h = Math.Max(14 * S, it.Size * 0.36f);
                float w = text == null ? h * 0.62f : Math.Max(h, g.MeasureString(text, f, 200, StringFormat.GenericTypographic).Width + h * 0.6f);
                if (text == null) h = w;
                var r = new RectangleF(ir.Right - w * 0.8f, ir.Y - h * 0.18f, w, h);
                using (var p = Gfx.RoundRect(r, h / 2))
                {
                    using (var br = new SolidBrush(Color.FromArgb(255, 255, 59, 48))) g.FillPath(br, p);
                    using (var pen = new Pen(Color.FromArgb(60, 0, 0, 0), 1)) g.DrawPath(pen, p);
                }
                if (text != null)
                    using (var sf = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        g.DrawString(text, f, Brushes.White, new RectangleF(r.X, r.Y + 0.5f, r.Width, r.Height), sf);
            }
        }

        /// <summary>Minimized windows: small "window" mark in the corner of the app icon.</summary>
        void DrawWindowMark(Graphics g, RectangleF ir)
        {
            float d = ir.Width * 0.38f;
            var r = new RectangleF(ir.Right - d, ir.Bottom - d * 0.85f, d, d * 0.8f);
            using (var p = Gfx.RoundRect(r, 3 * S))
            {
                using (var br = new SolidBrush(Theme.Dark ? Color.FromArgb(235, 50, 50, 56) : Color.FromArgb(240, 250, 250, 252))) g.FillPath(br, p);
                using (var pen = new Pen(Theme.TextDim, 1)) g.DrawPath(pen, p);
            }
            using (var pen = new Pen(Theme.TextDim, 1))
                g.DrawLine(pen, r.X + 2, r.Y + r.Height * 0.32f, r.Right - 2, r.Y + r.Height * 0.32f);
        }

        void DrawIcon(Graphics g, DockItem it, float x, float y, float size, bool pressed, float alpha)
        {
            Bitmap img = it.Small != null && Math.Abs(size - it.Small.Width) < 0.5f ? it.Small : it.Big;
            if (img == null) return;
            ImageAttributes ia = wrapAttr;
            ImageAttributes tmp = null;
            if (pressed || alpha < 1f)
            {
                float k = pressed ? 0.62f : 1f;
                var cm = new ColorMatrix(new[] {
                    new float[] { k, 0, 0, 0, 0 }, new float[] { 0, k, 0, 0, 0 }, new float[] { 0, 0, k, 0, 0 },
                    new float[] { 0, 0, 0, alpha, 0 }, new float[] { 0, 0, 0, 0, 1 } });
                tmp = new ImageAttributes();
                tmp.SetWrapMode(WrapMode.TileFlipXY);
                tmp.SetColorMatrix(cm);
                ia = tmp;
            }
            if (img == it.Small && tmp == null)
                g.DrawImage(img, (float)Math.Round(x), (float)Math.Round(y), img.Width, img.Height);
            else
                g.DrawImage(img, new[] { new PointF(x, y), new PointF(x + size, y), new PointF(x, y + size) },
                    new RectangleF(0, 0, img.Width, img.Height), GraphicsUnit.Pixel, ia);
            if (tmp != null) tmp.Dispose();
        }

        /// <summary>Name pill: above the icon (bottom dock) or beside it (side docks).</summary>
        void DrawLabel(Graphics g, string text, RectangleF icon)
        {
            if (string.IsNullOrEmpty(text)) return;
            using (var f = new Font(Fonts.UiBold, 11.5f * S, GraphicsUnit.Pixel))
            {
                var sz = g.MeasureString(text, f, 1000, StringFormat.GenericTypographic);
                float w = Math.Min(sz.Width + 18 * S, 260 * S), h = 22 * S;
                float x, y;
                if (pos == "left") { x = icon.Right + 10 * S; y = icon.Y + icon.Height / 2 - h / 2; }
                else if (pos == "right") { x = icon.X - 10 * S - w; y = icon.Y + icon.Height / 2 - h / 2; }
                else { x = icon.X + icon.Width / 2 - w / 2; y = icon.Y - 9 * S - h; }
                x = Math.Max(2, Math.Min(AW - w - 2, x));
                y = Math.Max(1, Math.Min(AH - h - 1, y));
                var r = new RectangleF(x, y, w, h);
                using (var path = Gfx.RoundRect(r, h / 2))
                {
                    using (var sh = Gfx.RoundRect(new RectangleF(r.X, r.Y + 1.5f * S, r.Width, r.Height), h / 2))
                    using (var sb = new SolidBrush(Color.FromArgb(40, 0, 0, 0))) g.FillPath(sb, sh);
                    using (var br = new SolidBrush(Theme.LabelBg)) g.FillPath(br, path);
                    using (var pen = new Pen(Theme.LabelBorder, 1f)) g.DrawPath(pen, path);
                }
                using (var br = new SolidBrush(Theme.Text))
                using (var sf = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                    g.DrawString(text, f, br, new RectangleF(r.X + 6 * S, r.Y - 0.5f * S, r.Width - 12 * S, r.Height), sf);
            }
        }

        // ------------------------------------------------------------------ animation loop

        void Kick()
        {
            if (animOn) return;
            animOn = true;
            lastTick = clock.ElapsedMilliseconds;
            N.timeBeginPeriod(1);
            anim.Start();
        }

        void StopAnim()
        {
            if (!animOn) return;
            animOn = false;
            anim.Stop();
            N.timeEndPeriod(1);
            Render(); // final crisp (bicubic) frame
        }

        void Tick()
        {
            long now = clock.ElapsedMilliseconds;
            float dt = Math.Min(50, Math.Max(1, now - lastTick));
            lastTick = now;
            bool active = false;

            float bandTop = panelTop + off - (M - B) * Smooth(mag) - 8 * S;
            bool overDock = hovering && mx >= panelL - 2 && mx <= panelR + 2 && my >= bandTop;
            float magTarget = (Settings.Magnify && (overDock || dragging) && !MenuOpen && shown > 0.6f) ? 1 : 0;
            if (mag != magTarget)
            {
                mag += (magTarget - mag) * (float)(1 - Math.Exp(-dt / 55.0));
                if (Math.Abs(magTarget - mag) < 0.004f) mag = magTarget;
                active = true;
            }

            float showTarget = 1;
            if (Settings.AutoHide && !App.SnapshotMode)
            {
                bool keep = hovering || dragging || MenuOpen || pressIdx >= 0 || (Look.Bool("DockSmartHide") && !overlapped);
                if (keep) lastKeep = now;
                else if (now - lastKeep < 450) active = true; // waiting out the hide delay
                showTarget = (keep || now - lastKeep < 450) ? 1 : 0;
            }
            if (shown != showTarget)
            {
                float step = dt / (showTarget > shown ? 170f : 230f);
                shown = showTarget > shown ? Math.Min(showTarget, shown + step) : Math.Max(showTarget, shown - step);
                active = true;
            }

            if (AnyBouncing()) active = true;

            if (active || dirty) Render();
            if (!active) StopAnim();
        }

        // ------------------------------------------------------------------ input

        protected override void OnMouseMove(MouseEventArgs e)
        {
            hovering = true;
            var vp = ToVirtual(e.X, e.Y);
            mx = vp.X;
            my = vp.Y;
            if (pressIdx >= 0 && pressIdx < items.Count && !dragging && e.Button == MouseButtons.Left)
            {
                var it = items[pressIdx];
                if (it.Pinned && !it.Fixed && (Math.Abs(mx - pressPt.X) > 6 * S || Math.Abs(my - pressPt.Y) > 6 * S))
                {
                    dragging = true;
                    dragItem = it;
                }
            }
            if (dragging) UpdateDrag();
            int prevHover = hoverIdx;
            hoverIdx = dragging ? -1 : HitTest(mx, my);
            if (hoverIdx != prevHover) Previews.OnDockHover(this, hoverIdx >= 0 ? items[hoverIdx] : null);
            Kick();
            RequestRender();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (dragging) return;
            hovering = false;
            hoverIdx = -1;
            Previews.OnDockHover(this, null);
            Kick();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            var vp = ToVirtual(e.X, e.Y);
            pressIdx = HitTest(vp.X, vp.Y);
            pressPt = new Point((int)vp.X, (int)vp.Y);
            Previews.Hide();
            RequestRender();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            var vp0 = ToVirtual(e.X, e.Y);
            e = new MouseEventArgs(e.Button, e.Clicks, (int)vp0.X, (int)vp0.Y, e.Delta);
            if (e.Button == MouseButtons.Left)
            {
                int idx = pressIdx;
                pressIdx = -1;
                if (dragging) EndDrag(true);
                else if (idx >= 0 && idx < items.Count && HitTest(e.X, e.Y) == idx) ClickItem(items[idx]);
                RequestRender();
            }
            else if (e.Button == MouseButtons.Right)
            {
                int idx = HitTest(e.X, e.Y);
                if (idx >= 0) ItemMenu(items[idx]);
                else if (e.X >= panelL && e.X <= panelR) DockMenu();
            }
        }

        void UpdateDrag()
        {
            dragRemove = my < panelTop + off - B * 1.25f;
            // reorder among pinned apps
            int from = Settings.Pinned.FindIndex(p => string.Equals(p, dragItem.Path, StringComparison.OrdinalIgnoreCase));
            if (from < 0) return;
            int to = 0;
            foreach (var it in items)
            {
                if (!it.Pinned || it == dragItem) continue;
                if ((it.SlotL + it.SlotR) / 2f < mx) to++;
            }
            if (to != from)
            {
                string p = Settings.Pinned[from];
                Settings.Pinned.RemoveAt(from);
                Settings.Pinned.Insert(Math.Min(to, Settings.Pinned.Count), p);
                Rebuild();
            }
        }

        void EndDrag(bool commit)
        {
            var it = dragItem;
            bool remove = dragRemove;
            dragging = false;
            dragRemove = false;
            dragItem = null;
            pressIdx = -1;
            if (commit && remove && it != null) Unpin(it);
            else Settings.Save();
        }

        protected override void OnDragEnter(DragEventArgs e)
        {
            e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None;
        }

        protected override void OnDragDrop(DragEventArgs e)
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null) return;
            foreach (var f in files)
                if (!Settings.Pinned.Exists(p => string.Equals(p, f, StringComparison.OrdinalIgnoreCase))) Settings.Pinned.Add(f);
            Settings.Save();
            Rebuild();
        }

        // ------------------------------------------------------------------ actions

        void ClickItem(DockItem it)
        {
            switch (it.Kind)
            {
                case ItemKind.Trash: Launcher.Open(ShellIcons.RecycleBin); return;
                case ItemKind.Folder:
                    if (Look.Bool("DockStacks")) { StackFlyout.ShowFor(this, it); return; }
                    Launcher.Open(it.Path);
                    return;
                case ItemKind.File: Launcher.Open(it.Path); return;
                case ItemKind.Window: Tracker.Activate(it.WinHwnd); return;
            }
            if (!it.Running)
            {
                if (it.Path == null) return;
                Launcher.Open(it.Path);
                if (!Look.Bool("DockBounce")) return;
                it.LaunchBounce = true;
                it.BounceStart = clock.ElapsedMilliseconds;
                it.BounceUntil = it.BounceStart + 3500;
                Kick();
                return;
            }
            var wins = it.Wins;
            IntPtr fg = Tracker.Foreground;
            int active = wins.FindIndex(w => w.Hwnd == fg);
            if (active < 0)
            {
                Tracker.Activate(wins[0].Hwnd);
            }
            else if (wins.Count == 1)
            {
                Tracker.Minimize(fg);
            }
            else
            {
                Tracker.Activate(wins[wins.Count - 1].Hwnd); // cycle through the app's windows
            }
        }

        public void OnFlash(IntPtr hwnd)
        {
            if (hwnd == Tracker.Foreground) return;
            foreach (var it in items)
            {
                if (!it.Wins.Exists(w => w.Hwnd == hwnd)) continue;
                it.Attention = true;
                if (!Look.Bool("DockBounce")) { RequestRender(); return; }
                long now = clock.ElapsedMilliseconds;
                if (it.BounceStart < 0) it.BounceStart = now;
                it.BounceUntil = now + BouncePeriod * 2;
                Kick();
                return;
            }
        }

        void Pin(DockItem it)
        {
            if (it.Path == null) return;
            Settings.Pinned.Add(it.Path);
            Settings.Save();
            Rebuild();
        }

        void Unpin(DockItem it)
        {
            Settings.Pinned.RemoveAll(p => string.Equals(p, it.Path, StringComparison.OrdinalIgnoreCase));
            Settings.Save();
            Rebuild();
        }

        /// <summary>Menus open above a bottom dock, or beside a side dock.</summary>
        void ShowMenu(PopMenu m, DockItem it)
        {
            Rectangle r;
            if (it != null) r = ItemScreenRect(it);
            else { var c = Cursor.Position; r = new Rectangle(c.X, c.Y, 0, 0); }
            if (pos == "left") m.Show(r.Right + (int)(8 * S), r.Y, N.TPM_LEFTALIGN);
            else if (pos == "right") m.Show(r.X - (int)(8 * S), r.Y, N.TPM_RIGHTALIGN);
            else m.Show(r.X + r.Width / 2, winY + (int)(panelTop + off - 6 * S), N.TPM_CENTERALIGN | N.TPM_BOTTOMALIGN);
            ownPanel = Flyout.Current;
        }

        void ItemMenu(DockItem it)
        {
            var m = new PopMenu();
            IntPtr fg = Tracker.Foreground;
            if (it.Running)
            {
                int n = 0;
                foreach (var w in it.Wins)
                {
                    if (++n > 15) break;
                    IntPtr h = w.Hwnd;
                    m.Add(PopMenu.Escape(N.TitleOf(h)), () => Tracker.Activate(h), h == fg);
                }
                m.Sep();
            }

            if (it.Kind == ItemKind.Trash)
            {
                m.Add("Open", () => Launcher.Open(ShellIcons.RecycleBin));
                m.Add("Empty Recycle Bin", () => { N.SHEmptyRecycleBin(App.Host.Handle, null, 0); CheckTrash(); }, false, !trashEmpty);
            }
            else if (it.Fixed)
            {
                m.Add("Open", () => Launcher.Open(it.Path));
            }
            else
            {
                if (it.Path != null)
                    m.Add(it.Running && it.Kind == ItemKind.App ? "New Window" : "Open", () =>
                    {
                        if (it.Running && it.Kind == ItemKind.App) Launcher.NewWindow(it.Path, it.Wins[0].Exe ?? it.Exe, it.Wins[0].Hwnd);
                        else Launcher.Open(it.Path);
                    });
                var opt = m.Sub("Options");
                opt.Add("Keep in Dock", () => { if (it.Pinned) Unpin(it); else Pin(it); }, it.Pinned, it.Path != null);
                string reveal = it.Exe ?? (it.Kind != ItemKind.App && it.Path != null && !it.Path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) ? it.Path : null);
                if (reveal != null) opt.Add("Show in File Explorer", () => Launcher.Reveal(reveal));
                if (it.Pinned) opt.Add("Remove from Dock", () => Unpin(it));
                string runKey = it.Wins.Count > 0 ? it.Wins[0].Key : null;
                if (!it.Pinned && runKey != null) opt.Add("Don't Show in Dock", () => App.HideFromDock(runKey));
                if (it.Path != null)
                {
                    opt.Add("Change Icon…", () => ChangeIcon(it));
                    if (Settings.IconStyle == "whitesur")
                    {
                        string cur;
                        Settings.IconLooks.TryGetValue(it.Path, out cur);
                        var lookMenu = opt.Sub("Icon Look");
                        string[] looks = { "auto", "brand", "white", "dark", "original" };
                        string[] names = { "Automatic", "Brand Colour", "White Tile", "Dark Tile", "Original Icon" };
                        for (int li = 0; li < looks.Length; li++)
                        {
                            string lv = looks[li];
                            lookMenu.Add(names[li], () =>
                            {
                                if (lv == "auto") Settings.IconLooks.Remove(it.Path); else Settings.IconLooks[it.Path] = lv;
                                Settings.Save();
                                DisposeIcons(it);
                                App.RebuildDocks();
                            }, (cur ?? "auto") == lv);
                        }
                    }
                    if (Settings.CustomIcons.ContainsKey(it.Path))
                        opt.Add("Reset Icon", () => { Settings.CustomIcons.Remove(it.Path); Settings.Save(); DisposeIcons(it); App.RebuildDocks(); });
                }
                string ruleKey = runKey ?? it.ExeKey ?? it.AumidKey;
                if (ruleKey != null)
                {
                    bool on = Settings.HideDockFor.Contains(ruleKey);
                    opt.Add("Hide Dock While Using This App", () =>
                    {
                        if (on) Settings.HideDockFor.Remove(ruleKey); else Settings.HideDockFor.Add(ruleKey);
                        Settings.Save();
                        App.ApplyDockRules();
                    }, on);
                }
            }

            if (it.Running)
            {
                m.Sep();
                m.Add("Hide", () => { foreach (var w in it.Wins) Tracker.Minimize(w.Hwnd); });
                m.Add("Quit", () => { foreach (var w in it.Wins) Tracker.Close(w.Hwnd); });
            }
            m.Sep();
            m.Add("Dock Settings…", SettingsWindow.ShowWindow);
            hoverIdx = -1;
            Previews.Hide();
            ShowMenu(m, it);
            hovering = false;
            Kick();
        }

        void DockMenu()
        {
            var m = new PopMenu();
            App.BuildSettingsMenu(m);
            ShowMenu(m, null);
            hovering = false;
            Kick();
        }

        // ------------------------------------------------------------------ visibility

        // ---- Smart Hide: with auto-hide on, stay visible unless a window actually covers the dock
        bool overlapped = true;

        public void UpdateOverlap()
        {
            if (!Settings.AutoHide || !Look.Bool("DockSmartHide") || B <= 0) return;
            bool ov = ComputeOverlap();
            if (ov == overlapped) return;
            overlapped = ov;
            Kick();
        }

        bool ComputeOverlap()
        {
            IntPtr fg = N.GetForegroundWindow();
            if (fg != IntPtr.Zero && App.IsDesktop(fg)) return false; // on the desktop the dock always shows
            float bw = BaseWidth();
            var vr = new RectangleF((winW - bw) / 2f, winH - margin - panelH, bw, panelH + margin);
            var r = MapR(vr);
            var dock = new Rectangle(winX + (int)r.X, winY + (int)r.Y, (int)r.Width, (int)r.Height);
            foreach (var w in Tracker.Windows)
            {
                if (N.IsIconic(w.Hwnd) || !N.IsWindowVisible(w.Hwnd)) continue;
                if (N.MonitorFromWindow(w.Hwnd, 2) != Disp.HMon) continue;
                RECT wr;
                if (!N.VisibleBounds(w.Hwnd, out wr)) continue;
                if (new Rectangle(wr.Left, wr.Top, wr.Width, wr.Height).IntersectsWith(dock)) return true;
            }
            return false;
        }

        bool fsHidden;

        /// <summary>Hidden while a fullscreen app runs on this monitor.</summary>
        public void SetHidden(bool hide) { fsHidden = hide; ApplyHidden(); }

        /// <summary>Hidden while an app from the "Hide Dock While Using" list is in front.</summary>
        public void SetRuleHidden(bool hide) { ruleHidden = hide; ApplyHidden(); }

        void ApplyHidden()
        {
            bool hide = fsHidden || ruleHidden;
            if (hide == hidden) return;
            hidden = hide;
            if (hide) { N.ShowWindow(Handle, N.SW_HIDE); back.Conceal(); Previews.Hide(); }
            else { N.ShowWindow(Handle, N.SW_SHOWNOACTIVATE); Render(); }
        }

        void ChangeIcon(DockItem it)
        {
            using (var dlg = new OpenFileDialog
            {
                Title = "Choose an icon for " + it.Name,
                Filter = "Images and icons (*.png;*.jpg;*.ico;*.exe)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico;*.exe|All files (*.*)|*.*",
            })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                Settings.CustomIcons[it.Path] = dlg.FileName;
                Settings.Save();
                DisposeIcons(it);
                App.RebuildDocks();
            }
        }

        /// <summary>For --snapshot: render a frame with the cursor over item idx.</summary>
        public Bitmap Snapshot(int idx)
        {
            LayoutItems();
            if (idx >= 0 && idx < items.Count)
            {
                hovering = true;
                mag = 1;
                mx = (items[idx].SlotL + items[idx].SlotR) / 2f;
                my = panelTop + 10;
                LayoutItems();
                hoverIdx = HitTest(mx, my);
            }
            Render();
            return new Bitmap(surf.Bitmap); // a real copy: Clone() still points at the window's DIB
        }

        public int ItemCount { get { return items.Count; } }
    }
}
