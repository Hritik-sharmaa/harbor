using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Harbor
{
    /// <summary>
    /// Live activities: a small dark pill that grows out of the centre of the menu bar for a few seconds, like the
    /// notch on a phone. Charging, headphones, a finished download, a screenshot, a timer, a meeting about to start.
    /// Click it for its action. It never takes focus, stays out of fullscreen apps, and only the important ones
    /// (timers, alarms, meetings) come through during Focus. Nothing runs until something happens.
    /// </summary>
    static class LiveActivity
    {
        public sealed class Item
        {
            public string Glyph, Title, Detail, Action;   // Action: a short label on the right, e.g. "Join"
            public Color Tint = Color.White;
            public Bitmap Image;                           // a thumbnail instead of the glyph (not owned)
            public Action Click;
            public bool Important;
            public float Level = -1;                       // 0..1: drawn as a ring around the icon (battery, headphones)
            public string Big, RightGlyph;                 // a big number on the right ("70%"), or an icon there
            public int Ms = 4000;
        }

        static readonly Queue<Item> queue = new Queue<Item>();
        static Pill pill;
        public static bool On { get { return Look.Bool("LiveActivities"); } }

        public static void Show(Item it)
        {
            if (!On || App.SnapshotMode) return;
            if (Target() == null) return; // a fullscreen game or video where you are
            if (FocusMode.On && !it.Important) return;
            if (queue.Count >= 3) return;
            queue.Enqueue(it);
            if (pill == null || !pill.Visible) Next();
        }

        public static void Show(string glyph, string title, string detail = null, bool important = false)
        {
            Show(new Item { Glyph = glyph, Title = title, Detail = detail, Important = important });
        }

        /// <summary>--pill-demo: a charging pill, headphones and a timer with an action, one after another (made-up values).</summary>
        public static void Demo() { foreach (var it in DemoItems()) Show(it); }

        /// <summary>--snapshot: each demo pill as a picture.</summary>
        public static List<Bitmap> Pictures(float scale)
        {
            var list = new List<Bitmap>();
            using (var p = new Pill()) foreach (var it in DemoItems()) list.Add(p.Picture(it, scale));
            return list;
        }

        static List<Item> DemoItems()
        {
            return new List<Item> {
            new Item { Glyph = "", Title = "Charging", Detail = "Charger connected", Level = 0.7f, Big = "70%", Tint = Color.FromArgb(255, 52, 199, 89), Ms = 2500 },
            new Item { Glyph = "", Title = "AirBuds Pro", Detail = "Connected · 70%", Level = 0.7f, RightGlyph = "", Tint = Color.FromArgb(255, 100, 180, 255), Ms = 2500 },
            new Item { Glyph = "", Title = "Pasta is ready", Detail = "Timer · 12 min", Tint = Color.FromArgb(255, 255, 179, 64), Action = "Again", Important = true, Ms = 2500 } };
        }

        /// <summary>The screen you're working on (where the active window is), unless something there is fullscreen.</summary>
        public static Display Target()
        {
            IntPtr mon = N.MonitorFromWindow(N.GetForegroundWindow(), 2);
            Display primary = null;
            foreach (var d in App.Displays)
            {
                if (d.Bar == null || !d.Bar.IsHandleCreated) continue;
                if (d.HMon == mon) return d.Fullscreen ? null : d;
                if (d.Primary) primary = d;
            }
            return primary != null && !primary.Fullscreen ? primary : null; // the desktop or a panel is active
        }

        static void Next()
        {
            if (queue.Count == 0) return;
            var d = Target();
            if (d == null) { queue.Clear(); return; }
            if (pill == null) pill = new Pill();
            pill.Play(queue.Dequeue(), d);
        }

        /// <summary>
        /// The pill window: black, curving into the menu bar like the notch, drawn into a layered window (per-pixel alpha).
        /// It springs out wide and short, drops down with a small bounce, and its content fades in once it's open.
        /// </summary>
        sealed class Pill : Form
        {
            const float Ear = 10, Side = 14, Bounce = 10;
            readonly Surface surf = new Surface();
            readonly Timer anim = new Timer { Interval = 15 }, hold = new Timer();
            readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
            Item item;
            float S = 1;
            int x, y, w, h, winW, winH;        // w x h: the pill itself; the window has room around it for the bounce
            bool closing, hover;
            // springs: width and height (0..1, a little past 1 while bouncing)
            float sw, swv, sh, shv;
            double last, shownAt;

            public Pill()
            {
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                AutoScaleMode = AutoScaleMode.None;
                anim.Tick += delegate { Step(); };
                hold.Tick += delegate { hold.Stop(); if (hover) { hold.Interval = 800; hold.Start(); } else Close2(); };
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

            /// <summary>--snapshot: the pill fully open, drawn offscreen.</summary>
            public Bitmap Picture(Item it, float scale)
            {
                Measure(it, scale);
                sw = sh = 1; swv = shv = 0;
                shownAt = clock.Elapsed.TotalSeconds - 10; // ring filled, pulse at rest
                surf.Ensure(winW, winH);
                Draw();
                return new Bitmap(surf.Bitmap);
            }

            void Measure(Item it, float scale)
            {
                item = it;
                S = scale;
                using (var f = new Font(Fonts.UiBold, 15f * S, GraphicsUnit.Pixel))
                using (var s = new Font(Fonts.Ui, 12.5f * S, GraphicsUnit.Pixel))
                using (var big = new Font(Fonts.Ui, 26f * S, GraphicsUnit.Pixel))
                using (var g = Graphics.FromHwnd(IntPtr.Zero))
                {
                    float tw = Math.Max(g.MeasureString(it.Title ?? "", f).Width, it.Detail != null ? g.MeasureString(it.Detail, s).Width : 0);
                    float rw = it.Action != null ? g.MeasureString(it.Action, f).Width + 34 * S
                        : it.Big != null ? g.MeasureString(it.Big, big).Width + 16 * S
                        : it.RightGlyph != null ? 52 * S : 0;
                    w = (int)Math.Round(Math.Max(320 * S, Math.Min(620 * S, 74 * S + tw + rw + 28 * S)));
                }
                h = (int)Math.Round(64 * S);
                winW = w + (int)Math.Round(2 * (Ear + Side) * S);
                winH = h + (int)Math.Round(Bounce * S);
            }

            public void Play(Item it, Display d)
            {
                Measure(it, d.Scale);
                var sb = d.Bar.ScreenBounds;
                x = (int)Math.Round(sb.X + sb.Width / 2f - winW / 2f);
                y = sb.Y + sb.Height;
                surf.Ensure(winW, winH);
                closing = false;
                hover = false;
                sw = 0.35f; sh = 0.12f; swv = shv = 0;
                last = shownAt = clock.Elapsed.TotalSeconds;
                Draw();
                N.SetWindowPos(Handle, N.HWND_TOPMOST, x, y, winW, winH, N.SWP_NOACTIVATE);
                surf.Present(Handle, x, y);
                if (!Visible) Show();
                anim.Interval = 15;
                anim.Start();
                hold.Interval = Math.Max(1500, it.Ms);
                hold.Stop();
                hold.Start();
            }

            void Step()
            {
                double now = clock.Elapsed.TotalSeconds;
                float dt = (float)Math.Min(0.05, now - last);
                last = now;
                // opening: bouncy; closing: quick and settled
                float tw = closing ? 0.35f : 1, th = closing ? 0 : 1;
                float kw = closing ? 420 : 200, cw = closing ? 42 : 17, kh = closing ? 380 : 180, ch = closing ? 40 : 16;
                for (float left = dt; left > 0; left -= 0.004f)
                {
                    float st = Math.Min(0.004f, left);
                    swv += (kw * (tw - sw) - cw * swv) * st; sw += swv * st;
                    shv += (kh * (th - sh) - ch * shv) * st; sh += shv * st;
                }
                bool settled = Math.Abs(tw - sw) < 0.002f && Math.Abs(swv) < 0.01f && Math.Abs(th - sh) < 0.002f && Math.Abs(shv) < 0.01f;
                if (!closing && settled && now - shownAt > 0.8)
                {
                    // open and still: only the pulse on a right-hand icon keeps moving, at a gentle frame rate
                    if (item == null || item.RightGlyph == null) anim.Stop(); else anim.Interval = 50;
                }
                if (closing && sh <= 0.03f)
                {
                    anim.Stop();
                    Hide();
                    item = null;
                    Next();
                    if (!Visible) surf.Dispose(); // nothing left to show: free the bitmap
                    return;
                }
                // keep drawing while it moves, and slowly for the pulse on its right-hand icon
                Draw();
                surf.Present(Handle, x, y);
            }

            void Close2()
            {
                if (closing) return;
                closing = true;
                anim.Interval = 15;
                anim.Start();
            }

            void Draw()
            {
                var g = surf.G;
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                float bw = Math.Min(winW - 2 * Ear * S, w * sw), bh = Math.Max(2, Math.Min(winH, h * sh));
                float bx = (winW - bw) / 2, r = Math.Min(22 * S, bh / 2), e = Ear * S * Math.Min(1, sh * 2.5f);
                using (var p = new GraphicsPath())
                {
                    // ears curving into the menu bar, then down to a round bottom (the notch's shape)
                    if (e > 0.5f) p.AddArc(bx - 2 * e, 0, 2 * e, 2 * e, 270, 90); else p.AddLine(bx, 0, bx, 0);
                    p.AddArc(bx, bh - 2 * r, 2 * r, 2 * r, 180, -90);
                    p.AddArc(bx + bw - 2 * r, bh - 2 * r, 2 * r, 2 * r, 90, -90);
                    if (e > 0.5f) p.AddArc(bx + bw, 0, 2 * e, 2 * e, 180, 90); else p.AddLine(bx + bw, 0, bx + bw, 0);
                    p.CloseFigure();
                    using (var br = new SolidBrush(Color.Black)) g.FillPath(br, p);
                    using (var eb = new LinearGradientBrush(new RectangleF(0, -1, winW, bh + 2), Color.FromArgb(0, 255, 255, 255), Color.FromArgb(34, 255, 255, 255), 90f))
                    using (var pen = new Pen(eb, 1f * S)) g.DrawPath(pen, p);
                }
                var it = item;
                float show = closing ? Math.Max(0, (sh - 0.4f) / 0.5f) : Math.Max(0, Math.Min(1, (sh - 0.6f) / 0.35f));
                if (it == null || show <= 0.01f) return;
                Func<int, int> A = a => (int)(a * Math.Min(1, show));

                float left = (winW - w) / 2f, cy = h / 2f + (1 - Math.Min(1, show)) * -6 * S;
                // left: the icon, in a ring when there's a level to show (battery, headphones)
                float isz = 36 * S, ix = left + 16 * S;
                var ir = new RectangleF(ix, cy - isz / 2, isz, isz);
                if (it.Image != null)
                {
                    using (var clip = Gfx.RoundRect(ir, 8 * S)) { var st = g.Save(); g.SetClip(clip); g.DrawImage(it.Image, ir); g.Restore(st); }
                }
                else
                {
                    if (it.Level >= 0)
                    {
                        float th = 3.4f * S, lv = Math.Min(1, it.Level) * Math.Min(1, (float)((clock.Elapsed.TotalSeconds - shownAt) / 0.7)); // the ring fills as it appears
                        var rr = RectangleF.Inflate(ir, -th / 2, -th / 2);
                        using (var track = new Pen(Color.FromArgb(A(60), it.Tint), th)) g.DrawEllipse(track, rr);
                        if (lv * 360 >= 1) using (var arc = new Pen(Color.FromArgb(A(255), it.Tint), th) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(arc, rr, -90, 360 * lv);
                    }
                    else using (var dot = new SolidBrush(Color.FromArgb(A(55), it.Tint))) g.FillEllipse(dot, ir);
                    using (var gf = new Font(Fonts.Icons, 14f * S, GraphicsUnit.Pixel))
                    using (var tb = new SolidBrush(Color.FromArgb(A(255), it.Level >= 0 ? Color.White : it.Tint)))
                    using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        g.DrawString(it.Glyph ?? "", gf, tb, ir, sf);
                }

                float tx = ix + isz + 14 * S, right = left + w - 18 * S;
                using (var f = new Font(Fonts.UiBold, 15f * S, GraphicsUnit.Pixel))
                using (var s = new Font(Fonts.Ui, 12.5f * S, GraphicsUnit.Pixel))
                using (var white = new SolidBrush(Color.FromArgb(A(250), 255, 255, 255)))
                using (var dim = new SolidBrush(Color.FromArgb(A(150), 255, 255, 255)))
                using (var sf = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                using (var cf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    // right: an action button, a big number, or an icon with a slow pulse
                    if (it.Action != null)
                    {
                        float aw = g.MeasureString(it.Action, f).Width + 22 * S;
                        float k = hover ? 1.04f : 1;
                        var ar = new RectangleF(right - aw, cy - 15 * S * k, aw, 30 * S * k);
                        using (var ab = new SolidBrush(Color.FromArgb(A(240), 255, 255, 255))) using (var pp = Gfx.RoundRect(ar, ar.Height / 2)) g.FillPath(ab, pp);
                        using (var dark = new SolidBrush(Color.FromArgb(A(240), 18, 18, 22))) g.DrawString(it.Action, f, dark, ar, cf);
                        right = ar.X - 12 * S;
                    }
                    else if (it.Big != null)
                    {
                        using (var big = new Font(Fonts.Ui, 26f * S, GraphicsUnit.Pixel))
                        {
                            float bw2 = g.MeasureString(it.Big, big).Width;
                            g.DrawString(it.Big, big, white, new RectangleF(right - bw2, cy - 18 * S, bw2 + 4 * S, 36 * S), sf);
                            right -= bw2 + 10 * S;
                        }
                    }
                    else if (it.RightGlyph != null)
                    {
                        float pr = 19 * S, pulse = (float)(0.5 + 0.5 * Math.Sin((clock.Elapsed.TotalSeconds - shownAt) * 3.2));
                        var rc = new RectangleF(right - 2 * pr, cy - pr, 2 * pr, 2 * pr);
                        using (var halo = new Pen(Color.FromArgb(A((int)(40 + 50 * pulse)), it.Tint), (2 + 3 * pulse) * S)) g.DrawEllipse(halo, RectangleF.Inflate(rc, 2 * S, 2 * S));
                        using (var ring = new Pen(Color.FromArgb(A(200), it.Tint), 1.6f * S)) g.DrawEllipse(ring, rc);
                        using (var gf = new Font(Fonts.Icons, 14f * S, GraphicsUnit.Pixel)) g.DrawString(it.RightGlyph, gf, white, rc, cf);
                        right = rc.X - 12 * S;
                    }
                    if (it.Detail != null)
                    {
                        g.DrawString(it.Title ?? "", f, white, new RectangleF(tx, cy - 20 * S, right - tx, 22 * S), sf);
                        g.DrawString(it.Detail, s, dim, new RectangleF(tx, cy + 1 * S, right - tx, 20 * S), sf);
                    }
                    else g.DrawString(it.Title ?? "", f, white, new RectangleF(tx, cy - 12 * S, right - tx, 24 * S), sf);
                }
            }

            protected override void OnMouseEnter(EventArgs e) { hover = true; if (!anim.Enabled) { Draw(); surf.Present(Handle, x, y); } base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hover = false; if (!anim.Enabled) { Draw(); surf.Present(Handle, x, y); } base.OnMouseLeave(e); }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                var it = item;
                Close2();
                if (it != null && it.Click != null) try { it.Click(); } catch (Exception ex) { App.Log(ex); }
            }
        }

        // ------------------------------------------------------------------ what triggers one

        static PowerLineStatus lastPower = PowerLineStatus.Unknown;
        static string lastHeadphones;

        /// <summary>Hooks the system events (power, headphones). The other sources call Show themselves.</summary>
        public static void Init()
        {
            lastPower = SystemInformation.PowerStatus.PowerLineStatus;
            lastHeadphones = BtBattery.ConnectedName;
            Microsoft.Win32.SystemEvents.PowerModeChanged += (s, e) => { if (e.Mode == Microsoft.Win32.PowerModes.StatusChange) App.Post(PowerChanged); };
            BtBattery.Changed += delegate
            {
                string now = BtBattery.ConnectedName;
                if (now != null && now != lastHeadphones)
                    Show(new Item { Glyph = "", Title = now, Detail = BtBattery.ConnectedLevel >= 0 ? "Connected · " + BtBattery.ConnectedLevel + "%" : "Connected", Level = BtBattery.ConnectedLevel >= 0 ? BtBattery.ConnectedLevel / 100f : -1, RightGlyph = "", Tint = Color.FromArgb(255, 100, 180, 255) });
                lastHeadphones = now;
            };
        }

        static void PowerChanged()
        {
            var ps = SystemInformation.PowerStatus;
            if (ps.PowerLineStatus == lastPower || (ps.BatteryChargeStatus & BatteryChargeStatus.NoSystemBattery) != 0) { lastPower = ps.PowerLineStatus; return; }
            lastPower = ps.PowerLineStatus;
            int pct = (int)Math.Round(ps.BatteryLifePercent * 100);
            if (ps.PowerLineStatus == PowerLineStatus.Online)
                Show(new Item { Glyph = "", Title = "Charging", Detail = "Charger connected", Level = pct / 100f, Big = pct + "%", Tint = Color.FromArgb(255, 52, 199, 89) });
            else
                Show(new Item { Glyph = "", Title = "On battery", Detail = ps.BatteryLifeRemaining > 0 ? "About " + (ps.BatteryLifeRemaining / 3600) + " h " + (ps.BatteryLifeRemaining / 60 % 60) + " min left" : "Charger disconnected", Level = pct / 100f, Big = pct + "%", Tint = pct <= 20 ? Color.FromArgb(255, 255, 69, 58) : Color.White });
        }
    }
}
