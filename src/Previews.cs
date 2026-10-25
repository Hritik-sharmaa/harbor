using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace Harbor
{
    /// <summary>Hover previews for running apps: live DWM thumbnails of each window, click to switch, × to close.</summary>
    static class Previews
    {
        static PreviewWindow win;
        static Dock owner, pendingDock;
        static DockItem item, pendingItem;
        static Timer showTimer, hideTimer;

        static void Ensure()
        {
            if (win != null) return;
            win = new PreviewWindow();
            var h = win.Handle;
            showTimer = new Timer { Interval = 450 };
            showTimer.Tick += delegate { showTimer.Stop(); if (pendingItem != null) Show(pendingDock, pendingItem); };
            hideTimer = new Timer { Interval = 300 };
            hideTimer.Tick += delegate { hideTimer.Stop(); Hide(); };
        }

        public static bool ShowingFor(Dock d) { return win != null && win.Visible && owner == d; }

        public static void OnDockHover(Dock d, DockItem it)
        {
            if (!Look.Bool("DockPreviews")) return;
            Ensure();
            bool ok = it != null && it.Kind == ItemKind.App && it.Running && !Flyout.AnyOpen;
            if (ok)
            {
                hideTimer.Stop();
                if (win.Visible) { if (it != item || d != owner) Show(d, it); }
                else { pendingDock = d; pendingItem = it; showTimer.Stop(); showTimer.Start(); }
            }
            else
            {
                showTimer.Stop();
                pendingItem = null;
                if (win.Visible) { hideTimer.Stop(); hideTimer.Start(); }
            }
        }

        internal static void PointerInside(bool inside)
        {
            if (hideTimer == null) return;
            if (inside) hideTimer.Stop();
            else { hideTimer.Stop(); hideTimer.Start(); }
        }

        static void Show(Dock d, DockItem it)
        {
            owner = d;
            item = it;
            win.ShowFor(d, it);
            d.RequestRender(); // hides the name label while previews are up
        }

        public static void Hide()
        {
            if (win == null) return;
            showTimer.Stop();
            hideTimer.Stop();
            pendingItem = null;
            var d = owner;
            owner = null;
            item = null;
            win.HideAll();
            if (d != null) d.RequestRender();
        }
    }

    sealed class PreviewWindow : Form
    {
        sealed class Thumb
        {
            public IntPtr Hwnd, Handle;
            public RectangleF Card, Image, Close;
            public string Title;
            public bool Minimized;
            public Bitmap Icon;
        }

        readonly List<Thumb> thumbs = new List<Thumb>();
        readonly Surface surf = new Surface();
        float S = 1;
        int hover = -1;
        bool hoverClose, blurOk;
        DockItem item;

        public PreviewWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.Style = N.WS_POPUP;
                cp.ExStyle |= N.WS_EX_TOOLWINDOW | N.WS_EX_NOACTIVATE | N.WS_EX_TOPMOST;
                cp.ClassStyle |= 0x20000;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            N.RoundCorners(Handle);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == N.WM_MOUSEACTIVATE) { m.Result = (IntPtr)N.MA_NOACTIVATE; return; }
            if (m.Msg == N.WM_DPICHANGED) return;
            base.WndProc(ref m);
        }

        public void HideAll()
        {
            foreach (var t in thumbs)
            {
                if (t.Handle != IntPtr.Zero) N.DwmUnregisterThumbnail(t.Handle);
                if (t.Icon != null) t.Icon.Dispose();
            }
            thumbs.Clear();
            hover = -1;
            if (Visible) N.ShowWindow(Handle, N.SW_HIDE);
        }

        public void ShowFor(Dock dock, DockItem it)
        {
            HideAll();
            item = it;
            S = dock.Disp.Scale;
            blurOk = Settings.Blur && N.SetAccent(Handle, 4, Theme.PanelTint);
            if (!blurOk) N.SetAccent(Handle, 0, 0);

            float pad = 10 * S, gap = 10 * S, titleH = 26 * S, maxW = 220 * S, maxH = 140 * S;
            float x = pad;
            float rowH = 0;
            int n = 0;
            foreach (var w in it.Wins)
            {
                if (++n > 6) break;
                var t = new Thumb { Hwnd = w.Hwnd, Title = N.TitleOf(w.Hwnd), Minimized = N.IsIconic(w.Hwnd) };
                float tw = maxW, th = maxH;
                if (!t.Minimized && N.DwmRegisterThumbnail(Handle, w.Hwnd, out t.Handle) == 0)
                {
                    SIZE src;
                    if (N.DwmQueryThumbnailSourceSize(t.Handle, out src) == 0 && src.cx > 0 && src.cy > 0)
                    {
                        float k = Math.Min(maxW / src.cx, maxH / src.cy);
                        tw = src.cx * k;
                        th = src.cy * k;
                    }
                }
                else
                {
                    t.Handle = IntPtr.Zero;
                    tw = 160 * S; th = 100 * S;
                }
                tw = Math.Max(tw, 120 * S);
                t.Card = new RectangleF(x, pad, tw + 12 * S, titleH + th + 12 * S);
                t.Image = new RectangleF(x + 6 * S, pad + titleH, tw, th);
                t.Close = new RectangleF(t.Card.Right - 26 * S, t.Card.Y + 4 * S, 20 * S, 20 * S);
                rowH = Math.Max(rowH, t.Card.Height);
                x += t.Card.Width + gap;
                thumbs.Add(t);
            }
            int W = (int)Math.Ceiling(x - gap + pad), H = (int)Math.Ceiling(rowH + pad * 2);

            // place next to the dock icon, on the side facing the screen
            var r = dock.ItemScreenRect(it);
            var wa = Screen.FromRectangle(r).WorkingArea;
            string pos = Look.Get("DockPosition");
            int px, py;
            if (pos == "left") { px = r.Right + (int)(14 * S); py = r.Y + r.Height / 2 - H / 2; }
            else if (pos == "right") { px = r.X - (int)(14 * S) - W; py = r.Y + r.Height / 2 - H / 2; }
            else { px = r.X + r.Width / 2 - W / 2; py = r.Y - (int)(16 * S) - H; }
            px = Math.Max(wa.Left + 6, Math.Min(wa.Right - W - 6, px));
            py = Math.Max(wa.Top + 6, Math.Min(wa.Bottom - H - 6, py));
            N.SetWindowPos(Handle, N.HWND_TOPMOST, px, py, W, H, N.SWP_NOACTIVATE);

            foreach (var t in thumbs)
            {
                if (t.Handle == IntPtr.Zero) continue;
                var p = new DWM_THUMBNAIL_PROPERTIES
                {
                    dwFlags = 0x1 | 0x4 | 0x8 | 0x10,
                    rcDestination = new RECT((int)t.Image.X, (int)t.Image.Y, (int)t.Image.Right, (int)t.Image.Bottom),
                    opacity = 255,
                    fVisible = true,
                    fSourceClientAreaOnly = false,
                };
                N.DwmUpdateThumbnailProperties(t.Handle, ref p);
            }
            Paint2();
            N.ShowWindow(Handle, N.SW_SHOWNOACTIVATE);
        }

        void Paint2()
        {
            surf.Ensure(Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height));
            var g = surf.G;
            g.Clear(blurOk ? Color.Transparent : Theme.PanelSolid);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (var f = new Font(Fonts.Ui, 12f * S, GraphicsUnit.Pixel))
            using (var sf = new StringFormat(StringFormat.GenericTypographic) { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
            using (var tb = new SolidBrush(Theme.Text))
            using (var db = new SolidBrush(Theme.TextDim))
            {
                for (int i = 0; i < thumbs.Count; i++)
                {
                    var t = thumbs[i];
                    bool hot = i == hover;
                    using (var p = Gfx.RoundRect(t.Card, 10 * S))
                    using (var br = new SolidBrush(hot ? Theme.TileHover : Theme.Tile))
                        g.FillPath(br, p);
                    if (hot)
                        using (var p = Gfx.RoundRect(t.Card, 10 * S))
                        using (var pen = new Pen(Theme.Accent, 2 * S)) g.DrawPath(pen, p);
                    g.DrawString(t.Title, f, tb, new RectangleF(t.Card.X + 10 * S, t.Card.Y + 2 * S, t.Card.Width - (hot ? 40 : 20) * S, 24 * S), sf);
                    if (t.Minimized)
                    {
                        if (item != null && item.Big != null)
                        {
                            float isz = 44 * S;
                            g.DrawImage(item.Big, new RectangleF(t.Image.X + t.Image.Width / 2 - isz / 2, t.Image.Y + t.Image.Height / 2 - isz / 2 - 8 * S, isz, isz));
                        }
                        using (var c = new StringFormat(sf) { Alignment = StringAlignment.Center })
                            g.DrawString("Minimized", f, db, new RectangleF(t.Image.X, t.Image.Bottom - 26 * S, t.Image.Width, 22 * S), c);
                    }
                    if (hot)
                    {
                        using (var br = new SolidBrush(hoverClose ? Color.FromArgb(255, 232, 17, 35) : Theme.CircleOff)) g.FillEllipse(br, t.Close);
                        using (var cf = new Font(Fonts.Icons, 8f * S, GraphicsUnit.Pixel))
                        using (var c = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                            g.DrawString("", cf, hoverClose ? Brushes.White : (Brush)tb, t.Close, c);
                    }
                }
            }
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (surf.W <= 0) return;
            IntPtr hdc = e.Graphics.GetHdc();
            surf.BlitTo(hdc);
            e.Graphics.ReleaseHdc(hdc);
        }

        int HitIndex(Point p, out bool onClose)
        {
            onClose = false;
            for (int i = 0; i < thumbs.Count; i++)
                if (thumbs[i].Card.Contains(p)) { onClose = thumbs[i].Close.Contains(p); return i; }
            return -1;
        }

        protected override void OnMouseEnter(EventArgs e) { Previews.PointerInside(true); }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = -1;
            Paint2();
            Previews.PointerInside(false);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool c;
            int i = HitIndex(e.Location, out c);
            if (i != hover || c != hoverClose) { hover = i; hoverClose = c; Paint2(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            bool c;
            int i = HitIndex(e.Location, out c);
            if (i < 0) return;
            var t = thumbs[i];
            if (c || e.Button == MouseButtons.Middle) { Tracker.Close(t.Hwnd); Previews.Hide(); return; }
            Tracker.Activate(t.Hwnd);
            Previews.Hide();
        }
    }
}
