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

        /// <summary>The pill window: drawn into a layered window (per-pixel alpha), shape and all.</summary>
        sealed class Pill : Form
        {
            readonly Surface surf = new Surface();
            readonly Timer anim = new Timer { Interval = 15 }, hold = new Timer();
            Item item;
            float S = 1, reveal;
            int x, y, w, h;
            bool closing, hover;

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

            public void Play(Item it, Display d)
            {
                item = it;
                S = d.Scale;
                using (var f = new Font(Fonts.UiBold, 13f * S, GraphicsUnit.Pixel))
                using (var s = new Font(Fonts.Ui, 12f * S, GraphicsUnit.Pixel))
                using (var g = Graphics.FromHwnd(IntPtr.Zero))
                {
                    float tw = g.MeasureString(it.Title ?? "", f).Width + (it.Detail != null ? g.MeasureString(it.Detail, s).Width + 10 * S : 0);
                    float aw = it.Action != null ? g.MeasureString(it.Action, f).Width + 26 * S : 0;
                    w = (int)Math.Round(Math.Max(240 * S, Math.Min(560 * S, 58 * S + tw + aw + 22 * S)));
                }
                h = (int)Math.Round(46 * S);
                var sb = d.Bar.ScreenBounds;
                x = (int)Math.Round(sb.X + sb.Width / 2f - w / 2f);
                y = sb.Y + sb.Height;
                surf.Ensure(w, h);
                closing = false;
                reveal = 0;
                hover = false;
                Draw();
                N.SetWindowPos(Handle, N.HWND_TOPMOST, x, y, w, h, N.SWP_NOACTIVATE);
                Present();
                if (!Visible) Show();
                anim.Start();
                hold.Interval = Math.Max(1500, it.Ms);
                hold.Stop();
            }

            void Step()
            {
                reveal += closing ? -15f / 200 : 15f / 180;
                if (!closing && reveal >= 1) { reveal = 1; anim.Stop(); hold.Start(); }
                if (closing && reveal <= 0)
                {
                    reveal = 0;
                    anim.Stop();
                    Hide();
                    item = null;
                    Next();
                    if (!Visible) surf.Dispose(); // nothing left to show: free the bitmap
                    return;
                }
                Present();
            }

            void Close2()
            {
                if (closing) return;
                closing = true;
                anim.Start();
            }

            void Present()
            {
                float e = 1 - (1 - reveal) * (1 - reveal) * (1 - reveal); // ease-out
                // drops a little out of the menu bar while fading in (never over the bar itself)
                surf.Present(Handle, x, y - (int)Math.Round((1 - e) * 10 * S), (byte)Math.Round(255 * Math.Min(1, e * 1.3f)));
            }

            void Draw()
            {
                var g = surf.G;
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                float r = 20 * S;
                using (var p = new GraphicsPath())
                {
                    // square where it meets the menu bar, round at the bottom (a notch)
                    p.AddLine(0, 0, w, 0);
                    p.AddArc(w - 2 * r, h - 2 * r, 2 * r, 2 * r, 0, 90);
                    p.AddArc(0, h - 2 * r, 2 * r, 2 * r, 90, 90);
                    p.CloseFigure();
                    using (var br = new LinearGradientBrush(new RectangleF(0, -2, w, h + 4), Color.FromArgb(250, 30, 30, 36), Color.FromArgb(250, 10, 10, 13), 90f))
                        g.FillPath(br, p);
                    using (var pen = new Pen(Color.FromArgb(60, 255, 255, 255), 1.1f * S)) g.DrawPath(pen, p);
                }
                var it = item;
                float ix = 14 * S, isz = 26 * S, cy = h / 2f;
                if (it.Image != null)
                {
                    var ir = new RectangleF(ix, cy - isz / 2, isz, isz);
                    using (var clip = Gfx.RoundRect(ir, 6 * S)) { var st = g.Save(); g.SetClip(clip); g.DrawImage(it.Image, ir); g.Restore(st); }
                }
                else
                {
                    using (var dot = new SolidBrush(Color.FromArgb(50, it.Tint))) g.FillEllipse(dot, ix, cy - isz / 2, isz, isz);
                    using (var gf = new Font(Fonts.Icons, 13f * S, GraphicsUnit.Pixel))
                    using (var tb = new SolidBrush(it.Tint))
                    using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        g.DrawString(it.Glyph ?? "", gf, tb, new RectangleF(ix, cy - isz / 2, isz, isz), sf);
                }
                float tx = ix + isz + 12 * S, right = w - 16 * S;
                using (var f = new Font(Fonts.UiBold, 13f * S, GraphicsUnit.Pixel))
                using (var s = new Font(Fonts.Ui, 12f * S, GraphicsUnit.Pixel))
                using (var white = new SolidBrush(Color.FromArgb(245, 255, 255, 255)))
                using (var dim = new SolidBrush(Color.FromArgb(160, 255, 255, 255)))
                using (var sf = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                {
                    if (it.Action != null)
                    {
                        float aw = g.MeasureString(it.Action, f).Width + 18 * S;
                        var ar = new RectangleF(right - aw, cy - 13 * S, aw, 26 * S);
                        using (var ab = new SolidBrush(Color.FromArgb(235, 255, 255, 255))) using (var pp = Gfx.RoundRect(ar, 13 * S)) g.FillPath(ab, pp);
                        using (var dark = new SolidBrush(Color.FromArgb(240, 18, 18, 22)))
                        using (var cf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                            g.DrawString(it.Action, f, dark, ar, cf);
                        right = ar.X - 10 * S;
                    }
                    float titleW = Math.Min(g.MeasureString(it.Title ?? "", f).Width + 2, right - tx);
                    g.DrawString(it.Title ?? "", f, white, new RectangleF(tx, 0, titleW, h), sf);
                    if (it.Detail != null && right - tx - titleW > 30 * S)
                        g.DrawString(it.Detail, s, dim, new RectangleF(tx + titleW + 8 * S, 0, right - tx - titleW - 8 * S, h), sf);
                }
            }

            protected override void OnMouseEnter(EventArgs e) { hover = true; base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hover = false; base.OnMouseLeave(e); }

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
                    Show(new Item { Glyph = "", Title = now + " connected", Detail = BtBattery.ConnectedLevel >= 0 ? "Battery " + BtBattery.ConnectedLevel + "%" : null, Tint = Color.FromArgb(255, 100, 180, 255) });
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
                Show(new Item { Glyph = "", Title = "Charging", Detail = pct + "%", Tint = Color.FromArgb(255, 52, 199, 89) });
            else
                Show(new Item { Glyph = "", Title = "On battery", Detail = pct + "%" + (ps.BatteryLifeRemaining > 0 ? " · about " + (ps.BatteryLifeRemaining / 3600) + " h " + (ps.BatteryLifeRemaining / 60 % 60) + " min left" : ""), Tint = pct <= 20 ? Color.FromArgb(255, 255, 69, 58) : Color.White });
        }
    }
}
