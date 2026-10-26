using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace Harbor
{
    /// <summary>
    /// Base for every popup (menus, Wi-Fi, sound, calendar, Control Center...): acrylic, rounded, closes when it loses focus.
    /// Content is drawn immediate-mode by Build(UI) and redrawn on any change.
    /// </summary>
    abstract class Flyout : Form
    {
        public static Flyout Current;
        public static bool AnyOpen { get { return Current != null && Current.Visible; } }
        static int lastCloseTick;
        static string lastCloseKey;

        protected float S = 1;
        protected int LogicalWidth = 300;
        protected int MaxLogicalHeight = 620;
        /// <summary>Settings window: normal app window (taskbar/Alt+Tab, stays open, solid, draggable header).</summary>
        protected bool WindowMode;
        protected int FixedLogicalHeight;
        public readonly Dictionary<string, string> Fields = new Dictionary<string, string>();
        public string FocusField;
        public string Hover, Pressed, Dragging;
        public string Key = "";

        readonly Surface surf = new Surface();
        readonly List<UI.Hit> hits = new List<UI.Hit>();
        bool acrylic;
        int W, H, contentH;
        float scroll;
        protected void ResetScroll() { scroll = 0; }
        Rectangle anchor;
        bool above;
        int align; // 0 left, 1 center, 2 right
        TopBar ownerBar;
        Action closed;

        protected Flyout()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            KeyPreview = true;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.Style = N.WS_POPUP | (WindowMode ? 0x00020000 /*WS_MINIMIZEBOX*/ : 0);
                cp.ExStyle |= WindowMode ? N.WS_EX_APPWINDOW : (N.WS_EX_TOOLWINDOW | N.WS_EX_TOPMOST);
                cp.ClassStyle |= 0x20000; // CS_DROPSHADOW
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
            if (m.Msg == N.WM_DPICHANGED) return;
            if (WindowMode && m.Msg == 0x84 /*WM_NCHITTEST*/)
            {
                base.WndProc(ref m);
                long lp = m.LParam.ToInt64();
                var p = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                if (m.Result.ToInt32() == 1 && IsDragArea(p)) m.Result = (IntPtr)2; // HTCAPTION: drag the window
                return;
            }
            base.WndProc(ref m);
        }

        protected abstract void Build(UI ui);
        protected virtual void OnOpened() { }
        protected virtual void OnClosedFlyout() { }
        protected virtual bool IsDragArea(Point p) { return false; }

        /// <summary>True if this click is the one that just closed the same flyout (so it acts as a toggle).</summary>
        public static bool JustClosed(string key)
        {
            return key == lastCloseKey && unchecked(Environment.TickCount - lastCloseTick) < 350;
        }

        /// <summary>Opens under a menu-bar item.</summary>
        public void ShowBelow(TopBar bar, RectangleF zone, string zoneId, int alignMode)
        {
            var sb = bar.ScreenBounds;
            ownerBar = bar;
            Key = zoneId + "@" + bar.Disp.Index;
            if (JustClosed(Key)) return;
            bar.MarkOpen(zoneId);
            Open(new Rectangle(sb.X + (int)zone.X, sb.Y, (int)zone.Width, sb.Height), false, alignMode, bar.Disp.Scale, null);
        }

        public void Open(Rectangle anchorScreen, bool showAbove, int alignMode, float scale, Action onClosed)
        {
            if (!WindowMode)
            {
                if (Current != null && Current != this && Current.Visible) Current.CloseFlyout();
                Current = this;
            }
            anchor = anchorScreen;
            above = showAbove;
            align = alignMode;
            S = scale;
            closed = onClosed;
            scroll = 0;
            Hover = Pressed = Dragging = null;
            var h = Handle;
            acrylic = !WindowMode && Settings.Blur && N.SetAccent(Handle, 4, Theme.PanelTint);
            if (!acrylic) N.SetAccent(Handle, 0, 0);
            OnOpened();
            Render(true);
            Show();
            Activate();
            N.SetForegroundWindow(Handle);
        }

        public void CloseFlyout()
        {
            if (!Visible) return;
            Hide();
            lastCloseTick = Environment.TickCount;
            lastCloseKey = Key;
            if (ownerBar != null) { ownerBar.MarkOpen(null); ownerBar = null; }
            if (Current == this) Current = null;
            OnClosedFlyout();
            var c = closed;
            closed = null;
            if (c != null) c();
            App.ScheduleTrim();
        }

        /// <summary>Set while something (a file drag) needs the panel to survive losing focus.</summary>
        protected bool HoldOpen;

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (!WindowMode && !HoldOpen) CloseFlyout();
        }

        public void Refresh2() { if (Visible) Render(false); }

        /// <summary>--snapshot: renders the content offscreen (no blur) for visual checks.</summary>
        public Bitmap Snap(float scale)
        {
            S = scale;
            var h = Handle;
            acrylic = false;
            OnOpened();
            Render(false);
            Render(false); // second pass: layouts that measure themselves (settings cards)
            var b = new Bitmap(W, H, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(b)) g.DrawImage(surf.Bitmap, new Rectangle(0, 0, W, H), new Rectangle(0, 0, W, H), GraphicsUnit.Pixel);
            return b;
        }

        void Render(bool place)
        {
            W = (int)Math.Round(LogicalWidth * S);
            int maxH = (int)Math.Round(Math.Max(MaxLogicalHeight, FixedLogicalHeight) * S);
            surf.Ensure(W, Math.Max(maxH, contentH) + 2);
            var g = surf.G;
            g.ResetTransform();
            g.Clear(acrylic ? Color.Transparent : Theme.PanelSolid);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.TranslateTransform(0, -scroll);
            hits.Clear();
            var ui = new UI(this, g, S, W, hits);
            try { Build(ui); }
            finally { ui.Dispose(); }
            g.ResetTransform();
            int newContent = (int)Math.Ceiling(ui.Y + 8 * S);
            int newH = FixedLogicalHeight > 0 ? (int)Math.Round(FixedLogicalHeight * S) : Math.Min(maxH, newContent);
            if (newContent > surf.H - 2) { contentH = newContent; Render(place); return; }
            contentH = newContent;
            scroll = Math.Max(0, Math.Min(scroll, contentH - newH));
            if (place || newH != H)
            {
                H = newH;
                Place();
            }
            Invalidate();
        }

        void Place()
        {
            if (WindowMode)
            {
                var wa = Screen.FromRectangle(anchor).WorkingArea;
                if (!Visible) N.SetWindowPos(Handle, IntPtr.Zero, wa.X + (wa.Width - W) / 2, wa.Y + (wa.Height - H) / 2, W, H, N.SWP_NOACTIVATE | N.SWP_NOZORDER);
                else N.SetWindowPos(Handle, IntPtr.Zero, 0, 0, W, H, N.SWP_NOACTIVATE | N.SWP_NOZORDER | N.SWP_NOMOVE);
                return;
            }
            var scr = Screen.FromRectangle(anchor).WorkingArea;
            int x = align == 0 ? anchor.X : (align == 1 ? anchor.X + anchor.Width / 2 - W / 2 : anchor.Right - W);
            int gapPx = (int)(6 * S);
            int y = above ? anchor.Y - H - gapPx : anchor.Bottom + gapPx;
            x = Math.Max(scr.Left + gapPx, Math.Min(scr.Right - W - gapPx, x));
            y = Math.Max(scr.Top + (above ? gapPx : 0), Math.Min(scr.Bottom - H - gapPx, y));
            N.SetWindowPos(Handle, N.HWND_TOPMOST, x, y, W, H, N.SWP_NOACTIVATE);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (surf.W <= 0) return;
            IntPtr hdc = e.Graphics.GetHdc();
            surf.BlitTo(hdc);
            e.Graphics.ReleaseHdc(hdc);
        }

        // ------------------------------------------------------------------ input

        UI.Hit HitAt(Point p)
        {
            var q = new PointF(p.X, p.Y + scroll);
            for (int i = hits.Count - 1; i >= 0; i--) if (hits[i].R.Contains(hits[i].Fixed ? new PointF(p.X, p.Y) : q)) return hits[i];
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (Dragging != null)
            {
                var d = hits.Find(h => h.Id == Dragging);
                if (d != null) SlideTo(d, e.X);
                return;
            }
            var hit = HitAt(e.Location);
            string id = hit == null ? null : hit.Id;
            if (id != Hover) { Hover = id; Render(false); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (Hover != null && Dragging == null) { Hover = null; Render(false); }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (contentH <= H) return;
            scroll = Math.Max(0, Math.Min(contentH - H, scroll - e.Delta / 120f * 40 * S));
            Render(false);
        }

        public MouseButtons LastButton;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left && e.Button != MouseButtons.Right) return;
            LastButton = e.Button;
            var hit = HitAt(e.Location);
            if (hit == null) { Pressed = null; return; }
            if (e.Button == MouseButtons.Left && hit.Slide != null) { Dragging = hit.Id; SlideTo(hit, e.X); return; }
            if (e.Button == MouseButtons.Left && hit.Field != null) { FocusField = hit.Field; Render(false); return; }
            Pressed = hit.Id;
            Render(false);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (Dragging != null) { Dragging = null; Render(false); return; }
            var hit = HitAt(e.Location);
            if (hit == null || hit.Id != Pressed || hit.Act == null) { Pressed = null; return; }
            Pressed = null;
            if (!hit.Keep) CloseFlyout();
            try { hit.Act(); } catch (Exception ex) { App.Log(ex); }
            if (hit.Keep && Visible) Render(false);
        }

        void SlideTo(UI.Hit h, int x)
        {
            var t = h.Track;
            float v = (x - t.X - t.Height / 2) / Math.Max(1, t.Width - t.Height);
            h.Slide(Math.Max(0, Math.Min(1, v)));
            Render(false);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { CloseFlyout(); return; }
            if (FocusField == null) return;
            string v;
            Fields.TryGetValue(FocusField, out v);
            v = v ?? "";
            if (e.KeyCode == Keys.Back && v.Length > 0) { Fields[FocusField] = v.Substring(0, v.Length - 1); Render(false); }
            else if (e.Control && e.KeyCode == Keys.V)
            {
                try { if (Clipboard.ContainsText()) Fields[FocusField] = v + Clipboard.GetText().Replace("\r", "").Replace("\n", ""); } catch { }
                Render(false);
            }
            else if (e.KeyCode == Keys.Enter)
            {
                var sub = hits.Find(h => h.Id == FocusField + ":submit");
                if (sub != null && sub.Act != null) { try { sub.Act(); } catch (Exception ex) { App.Log(ex); } Render(false); }
            }
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            if (FocusField == null || char.IsControl(e.KeyChar)) return;
            string v;
            Fields.TryGetValue(FocusField, out v);
            Fields[FocusField] = (v ?? "") + e.KeyChar;
            Render(false);
        }

        public string Field(string id)
        {
            string v;
            return Fields.TryGetValue(id, out v) ? v ?? "" : "";
        }
    }

    /// <summary>Immediate-mode drawing kit for flyouts. Every call draws a row and advances Y.</summary>
    sealed class UI : IDisposable
    {
        public sealed class Hit
        {
            public string Id;
            public RectangleF R;
            public Action Act;
            public bool Keep;
            public Action<float> Slide;
            public RectangleF Track;
            public string Field;
            public bool Fixed; // drawn without scrolling (settings sidebar)
        }

        public readonly Graphics G;
        public readonly float S, W;
        public float Y;
        public float Pad, Right; // content spans Pad..Right (settings window shifts it right of the sidebar)
        readonly Flyout f;
        readonly List<Hit> hits;
        public readonly Font Body, Bold, Small, Icon, BigIcon, Title;
        public readonly SolidBrush TextBrush, DimBrush;
        readonly StringFormat left, center, right;

        public UI(Flyout f, Graphics g, float s, float w, List<Hit> hits)
        {
            this.f = f; G = g; S = s; W = w; this.hits = hits;
            Pad = 6 * s;
            Right = w - Pad;
            Y = Pad;
            Body = new Font(Fonts.Ui, 13f * s, GraphicsUnit.Pixel);
            Bold = new Font(Fonts.UiBold, 13f * s, GraphicsUnit.Pixel);
            Title = new Font(Fonts.UiBold, 15f * s, GraphicsUnit.Pixel);
            Small = new Font(Fonts.Ui, 11.5f * s, GraphicsUnit.Pixel);
            Icon = new Font(Fonts.Icons, 15f * s, GraphicsUnit.Pixel);
            BigIcon = new Font(Fonts.Icons, 18f * s, GraphicsUnit.Pixel);
            TextBrush = new SolidBrush(Theme.Text);
            DimBrush = new SolidBrush(Theme.TextDim);
            left = new StringFormat(StringFormat.GenericTypographic) { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            center = new StringFormat(left) { Alignment = StringAlignment.Center };
            right = new StringFormat(left) { Alignment = StringAlignment.Far };
        }

        public void Dispose()
        {
            Body.Dispose(); Bold.Dispose(); Title.Dispose(); Small.Dispose(); Icon.Dispose(); BigIcon.Dispose();
            TextBrush.Dispose(); DimBrush.Dispose(); left.Dispose(); center.Dispose(); right.Dispose();
        }

        public Hit AddHit(string id, RectangleF r, Action act, bool keep = false)
        {
            var h = new Hit { Id = id, R = r, Act = act, Keep = keep };
            hits.Add(h);
            return h;
        }

        public bool IsHover(string id) { return f.Hover == id; }

        public void Gap(float h) { Y += h * S; }

        public RectangleF Custom(float h)
        {
            var r = new RectangleF(Pad, Y, (Right - Pad), h * S);
            Y += h * S;
            return r;
        }

        public void Text(string s, RectangleF r, Font font, Brush b, int alignMode = 0)
        {
            G.DrawString(s ?? "", font, b, new RectangleF(r.X, r.Y + 0.5f * S, r.Width, r.Height), alignMode == 0 ? left : (alignMode == 1 ? center : right));
        }

        public void Glyph(string glyph, RectangleF r, Font font, Brush b) { Text(glyph, r, font, b, 1); }

        public void Fill(RectangleF r, Color c, float radius)
        {
            using (var br = new SolidBrush(c))
            using (var p = Gfx.RoundRect(r, radius)) G.FillPath(br, p);
        }

        /// <summary>Bold title with an optional switch on the right.</summary>
        public void Header(string title, bool? on = null, Action<bool> set = null, string sub = null)
        {
            var r = new RectangleF(Pad, Y, (Right - Pad), (sub != null ? 44 : 34) * S);
            Text(title, new RectangleF(r.X + 8 * S, r.Y + (sub != null ? 4 * S : 0), r.Width - 70 * S, sub != null ? 22 * S : r.Height), Title, TextBrush);
            if (sub != null) Text(sub, new RectangleF(r.X + 8 * S, r.Y + 24 * S, r.Width - 70 * S, 16 * S), Small, DimBrush);
            if (on.HasValue)
            {
                var sw = new RectangleF(r.Right - 46 * S, r.Y + r.Height / 2 - 11 * S, 38 * S, 22 * S);
                Switch(sw, on.Value);
                bool v = on.Value;
                AddHit("hdr:" + title, sw, () => { if (set != null) set(!v); }, true);
            }
            Y += r.Height;
        }

        public void Switch(RectangleF r, bool on)
        {
            Fill(r, on ? Theme.Accent : Theme.Track, r.Height / 2);
            float k = r.Height - 4 * S;
            var knob = new RectangleF(on ? r.Right - k - 2 * S : r.X + 2 * S, r.Y + 2 * S, k, k);
            using (var br = new SolidBrush(Color.White)) G.FillEllipse(br, knob);
        }

        public void Section(string text)
        {
            var r = new RectangleF(Pad + 8 * S, Y + 4 * S, (Right - Pad) - 16 * S, 20 * S);
            Text(text, r, Small, DimBrush);
            Y += 26 * S;
        }

        public void Separator()
        {
            using (var pen = new Pen(Theme.Separator, 1))
                G.DrawLine(pen, Pad + 8 * S, Y + 4.5f * S, Right - 8 * S, Y + 4.5f * S);
            Y += 9 * S;
        }

        public void Info(string text)
        {
            var r = new RectangleF(Pad + 8 * S, Y, (Right - Pad) - 16 * S, 22 * S);
            Text(text, r, Small, DimBrush);
            Y += 22 * S;
        }

        /// <summary>A clickable row: optional glyph (or image), title, subtitle, right text, checkmark, chevron.</summary>
        public void Item(string id, string title, Action act = null, string glyph = null, Bitmap img = null, string sub = null,
            string rightText = null, bool check = false, bool enabled = true, bool keep = false, bool circle = false,
            bool accent = false, bool chevron = false)
        {
            float h = (sub != null ? 46 : 30) * S;
            var r = new RectangleF(Pad, Y, (Right - Pad), h);
            bool hot = enabled && act != null && f.Hover == id;
            if (hot) Fill(r, f.Pressed == id ? Theme.Accent : Theme.TileHover, 7 * S);
            float x = r.X + 10 * S;
            if (glyph != null || img != null)
            {
                float d = circle ? 30 * S : 22 * S;
                var ir = new RectangleF(x, r.Y + (h - d) / 2, d, d);
                if (circle)
                {
                    using (var br = new SolidBrush(accent ? Theme.Accent : Theme.CircleOff)) G.FillEllipse(br, ir);
                }
                if (img != null) G.DrawImage(img, circle ? RectangleF.Inflate(ir, -6 * S, -6 * S) : ir);
                else using (var gb = new SolidBrush(accent && circle ? Color.White : (enabled ? Theme.Text : Theme.TextDim))) Glyph(glyph, ir, Icon, gb);
                x += d + 10 * S;
            }
            float rightW = 0;
            if (check || chevron) rightW = 22 * S;
            if (rightText != null) rightW += G.MeasureString(rightText, Small, 400, StringFormat.GenericTypographic).Width + 8 * S;
            var tb = enabled ? TextBrush : DimBrush;
            if (sub != null)
            {
                Text(title, new RectangleF(x, r.Y + 6 * S, r.Right - x - rightW - 6 * S, 18 * S), Body, tb);
                Text(sub, new RectangleF(x, r.Y + 24 * S, r.Right - x - rightW - 6 * S, 16 * S), Small, DimBrush);
            }
            else Text(title, new RectangleF(x, r.Y, r.Right - x - rightW - 6 * S, h), Body, tb);
            float rx = r.Right - 8 * S;
            if (check || chevron)
            {
                var cr = new RectangleF(rx - 18 * S, r.Y, 18 * S, h);
                using (var f2 = new Font(Fonts.Icons, 11f * S, GraphicsUnit.Pixel))
                    Glyph(check ? "" : "", cr, f2, check ? (Brush)TextBrush : DimBrush);
                rx -= 22 * S;
            }
            if (rightText != null) Text(rightText, new RectangleF(r.X, r.Y, rx - r.X, h), Small, DimBrush, 2);
            if (enabled && act != null) AddHit(id, r, act, keep);
            Y += h;
        }

        public void Slider(string id, float v, Action<float> set, string glyph, Action glyphClick = null, string label = null, Bitmap img = null, float inset = 0)
        {
            float rowH = (label != null ? 52 : 34) * S;
            var row = new RectangleF(Pad + inset * S, Y, (Right - Pad) - inset * 2 * S, rowH);
            float ty = row.Y + (label != null ? 26 * S : 5 * S);
            if (label != null)
            {
                float lx = row.X + 10 * S;
                if (img != null) { G.DrawImage(img, new RectangleF(lx, row.Y + 5 * S, 16 * S, 16 * S)); lx += 22 * S; }
                Text(label, new RectangleF(lx, row.Y + 4 * S, row.Width - (lx - row.X) - 50 * S, 18 * S), Body, TextBrush);
                Text((int)Math.Round(v * 100) + "%", new RectangleF(row.X, row.Y + 4 * S, row.Width - 10 * S, 18 * S), Small, DimBrush, 2);
            }
            var t = new RectangleF(row.X + 8 * S, ty, row.Width - 16 * S, 22 * S);
            v = Math.Max(0, Math.Min(1, v));
            float rad = t.Height / 2;
            Fill(t, Theme.Track, rad);
            float fw = t.Height + (t.Width - t.Height) * v;
            Fill(new RectangleF(t.X, t.Y, fw, t.Height), Theme.TrackFill, rad);
            var kr = new RectangleF(t.X + fw - t.Height, t.Y, t.Height, t.Height);
            using (var br = new SolidBrush(Color.White)) G.FillEllipse(br, kr);
            using (var pen = new Pen(Color.FromArgb(40, 0, 0, 0), 1)) G.DrawEllipse(pen, kr);
            if (glyph != null)
                using (var gb = new SolidBrush(Color.FromArgb(200, 40, 40, 44)))
                using (var f2 = new Font(Fonts.Icons, 12f * S, GraphicsUnit.Pixel))
                    Glyph(glyph, new RectangleF(t.X, t.Y, t.Height, t.Height), f2, gb);
            var hit = AddHit(id, RectangleF.Inflate(t, 0, 5 * S), null);
            hit.Slide = set;
            hit.Track = t;
            if (glyphClick != null) AddHit(id + ":glyph", new RectangleF(t.X, t.Y, t.Height, t.Height), glyphClick, true);
            Y += rowH;
        }

        public void Choice(string id, string[] options, int selected, Action<int> set)
        {
            var r = new RectangleF(Pad + 6 * S, Y + 3 * S, (Right - Pad) - 12 * S, 28 * S);
            Fill(r, Theme.Track, 8 * S);
            float sw = r.Width / options.Length;
            for (int i = 0; i < options.Length; i++)
            {
                var sr = new RectangleF(r.X + sw * i + 2 * S, r.Y + 2 * S, sw - 4 * S, r.Height - 4 * S);
                if (i == selected) Fill(sr, Theme.Dark ? Color.FromArgb(70, 255, 255, 255) : Color.White, 6 * S);
                else if (f.Hover == id + i) Fill(sr, Theme.TileHover, 6 * S);
                Text(options[i], sr, Small, i == selected ? TextBrush : DimBrush, 1);
                int k = i;
                AddHit(id + i, sr, () => set(k), true);
            }
            Y += 34 * S;
        }

        public void ToggleRow(string id, string title, bool on, Action<bool> set, string sub = null)
        {
            float h = (sub != null ? 44 : 32) * S;
            var r = new RectangleF(Pad, Y, (Right - Pad), h);
            if (f.Hover == id) Fill(r, Theme.TileHover, 7 * S);
            if (sub != null)
            {
                Text(title, new RectangleF(r.X + 10 * S, r.Y + 5 * S, r.Width - 70 * S, 18 * S), Body, TextBrush);
                Text(sub, new RectangleF(r.X + 10 * S, r.Y + 23 * S, r.Width - 70 * S, 16 * S), Small, DimBrush);
            }
            else Text(title, new RectangleF(r.X + 10 * S, r.Y, r.Width - 70 * S, h), Body, TextBrush);
            Switch(new RectangleF(r.Right - 46 * S, r.Y + h / 2 - 10 * S, 36 * S, 20 * S), on);
            AddHit(id, r, () => set(!on), true);
            Y += h;
        }

        public void TextField(string id, string placeholder, bool password, Action submit)
        {
            var r = new RectangleF(Pad + 8 * S, Y + 4 * S, (Right - Pad) - 16 * S, 30 * S);
            bool focus = f.FocusField == id;
            Fill(r, Theme.Dark ? Color.FromArgb(60, 0, 0, 0) : Color.FromArgb(220, 255, 255, 255), 7 * S);
            using (var pen = new Pen(focus ? Theme.Accent : Theme.TileBorder, focus ? 2 * S : 1))
            using (var p = Gfx.RoundRect(r, 7 * S)) G.DrawPath(pen, p);
            string v = f.Field(id);
            string shown = password ? new string('•', v.Length) : v;
            var tr = new RectangleF(r.X + 10 * S, r.Y, r.Width - 20 * S, r.Height);
            if (v.Length == 0) Text(placeholder, tr, Body, DimBrush);
            else Text(shown, tr, Body, TextBrush);
            if (focus)
            {
                float tw = v.Length == 0 ? 0 : G.MeasureString(shown, Body, 1000, StringFormat.GenericTypographic).Width;
                using (var pen = new Pen(Theme.Text, 1.2f * S))
                    G.DrawLine(pen, tr.X + tw + 1, r.Y + 7 * S, tr.X + tw + 1, r.Bottom - 7 * S);
            }
            var hit = AddHit(id, r, null);
            hit.Field = id;
            AddHit(id + ":submit", RectangleF.Empty, submit, true);
            Y += 38 * S;
        }

        /// <summary>Labelled slider for a whole-number setting, with the live value on the right ("48 px").</summary>
        public void ValueSlider(string id, string label, int value, int min, int max, string unit, Action<int> set)
        {
            var row = new RectangleF(Pad, Y, Right - Pad, 50 * S);
            Text(label, new RectangleF(row.X + 10 * S, row.Y + 4 * S, row.Width - 90 * S, 18 * S), Body, TextBrush);
            Text(value + unit, new RectangleF(row.X, row.Y + 4 * S, row.Width - 10 * S, 18 * S), Small, DimBrush, 2);
            var t = new RectangleF(row.X + 10 * S, row.Y + 30 * S, row.Width - 20 * S, 4 * S);
            float v = max > min ? (value - min) / (float)(max - min) : 0;
            v = Math.Max(0, Math.Min(1, v));
            Fill(t, Theme.Track, 2 * S);
            Fill(new RectangleF(t.X, t.Y, Math.Max(4 * S, t.Width * v), t.Height), Theme.Accent, 2 * S);
            float k = 18 * S;
            var kr = new RectangleF(t.X + t.Width * v - k / 2, t.Y + t.Height / 2 - k / 2, k, k);
            using (var sh = new SolidBrush(Color.FromArgb(50, 0, 0, 0))) G.FillEllipse(sh, kr.X, kr.Y + 1 * S, k, k);
            using (var br = new SolidBrush(Color.White)) G.FillEllipse(br, kr);
            var track = new RectangleF(t.X - k / 2, t.Y - k / 2, t.Width + k, k);
            var hit = AddHit(id, new RectangleF(row.X, row.Y + 18 * S, row.Width, 30 * S), null);
            hit.Slide = f2 => set(min + (int)Math.Round(f2 * (max - min)));
            hit.Track = new RectangleF(t.X - k / 2, t.Y, t.Width + k, k); // knob centre maps to 0..1
            Y += 50 * S;
        }

        public static readonly string[] Swatches =
        {
            "auto", "#000000", "#1C1C1E", "#3A3A3C", "#FFFFFF", "#F2F2F7",
            "#0A84FF", "#5E5CE6", "#BF5AF2", "#FF375F", "#FF9F0A", "#30D158", "#64D2FF",
        };

        /// <summary>Row of colour swatches ("auto" follows light/dark) plus a "+" for any custom colour.</summary>
        public void ColorPicker(string id, string label, string value, Action<string> set, bool allowAuto = true)
        {
            var row = new RectangleF(Pad, Y, Right - Pad, 58 * S);
            Text(label, new RectangleF(row.X + 10 * S, row.Y + 4 * S, row.Width - 20 * S, 18 * S), Body, TextBrush);
            float d = 22 * S, gap = 8 * S, x = row.X + 10 * S, y = row.Y + 28 * S;
            bool matched = false;
            foreach (var sw in Swatches)
            {
                if (sw == "auto" && !allowAuto) continue;
                var r = new RectangleF(x, y, d, d);
                bool sel = string.Equals(sw, value, StringComparison.OrdinalIgnoreCase);
                matched |= sel;
                DrawSwatch(r, sw, sel, f.Hover == id + sw);
                string s2 = sw;
                AddHit(id + sw, RectangleF.Inflate(r, 3 * S, 3 * S), () => set(s2), true);
                x += d + gap;
                if (x + d > row.Right) break;
            }
            // custom colour
            if (x + d <= row.Right)
            {
                var r = new RectangleF(x, y, d, d);
                Color cur;
                bool custom = !matched && Look.TryParse(value, out cur);
                if (custom) DrawSwatch(r, value, true, f.Hover == id + "+");
                else
                {
                    using (var pen = new Pen(f.Hover == id + "+" ? Theme.Text : Theme.TextDim, 1.4f * S)) G.DrawEllipse(pen, r);
                    using (var pf = new Font(Fonts.Icons, 10f * S, GraphicsUnit.Pixel)) Glyph("", r, pf, TextBrush);
                }
                AddHit(id + "+", RectangleF.Inflate(r, 3 * S, 3 * S), () =>
                {
                    using (var dlg = new ColorDialog { FullOpen = true, AnyColor = true })
                    {
                        Color c0;
                        if (Look.TryParse(value, out c0)) dlg.Color = c0;
                        if (dlg.ShowDialog(f) == DialogResult.OK) set(Look.Hex(dlg.Color));
                    }
                }, true);
            }
            Y += 58 * S;
        }

        void DrawSwatch(RectangleF r, string sw, bool selected, bool hot)
        {
            if (sw == "auto")
            {
                // half dark / half light = follows the theme
                using (var p = new System.Drawing.Drawing2D.GraphicsPath())
                {
                    p.AddEllipse(r);
                    var clip = G.Clip;
                    G.SetClip(p);
                    using (var b1 = new SolidBrush(Color.FromArgb(255, 30, 30, 34))) G.FillRectangle(b1, r);
                    using (var b2 = new SolidBrush(Color.FromArgb(255, 245, 245, 248)))
                        G.FillPolygon(b2, new[] { new PointF(r.Right, r.Y), new PointF(r.Right, r.Bottom), new PointF(r.X, r.Bottom) });
                    G.Clip = clip;
                }
            }
            else
            {
                Color c;
                if (Look.TryParse(sw, out c)) using (var b = new SolidBrush(c)) G.FillEllipse(b, r);
            }
            using (var pen = new Pen(Color.FromArgb(60, 128, 128, 128), 1)) G.DrawEllipse(pen, r);
            if (selected || hot)
                using (var pen = new Pen(selected ? Theme.Accent : Theme.TextDim, 2 * S))
                    G.DrawEllipse(pen, RectangleF.Inflate(r, 3.5f * S, 3.5f * S));
        }

        public void Button(string id, string text, Action act, bool primary = false, bool keep = false)
        {
            var r = new RectangleF(Pad + 8 * S, Y + 3 * S, (Right - Pad) - 16 * S, 30 * S);
            Color bg = primary ? Theme.Accent : (f.Hover == id ? Theme.TileHover : Theme.Tile);
            if (primary && f.Hover == id) bg = Gfx.Lighten(Theme.Accent, 0.12f);
            Fill(r, bg, 8 * S);
            using (var b = new SolidBrush(primary ? Color.White : Theme.Text)) Text(text, r, Bold, b, 1);
            AddHit(id, r, act, keep);
            Y += 36 * S;
        }
    }
}
