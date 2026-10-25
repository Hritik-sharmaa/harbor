using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace Harbor
{
    /// <summary>System usage panel (click the CPU / memory graphs in the menu bar).</summary>
    sealed class SystemFlyout : Flyout
    {
        static SystemFlyout inst;
        public static bool IsOpen { get { return inst != null && inst.Visible; } }

        public static void Toggle(TopBar bar, RectangleF zone)
        {
            if (inst == null) inst = new SystemFlyout();
            inst.ShowBelow(bar, zone, "sys", 2);
        }

        sealed class Proc { public string Name; public double Cpu; public long Mem; }

        readonly Timer tick = new Timer { Interval = 1500 };
        Dictionary<int, TimeSpan> lastCpu = new Dictionary<int, TimeSpan>();
        DateTime lastSample;
        List<Proc> top = new List<Proc>();

        internal SystemFlyout()
        {
            LogicalWidth = 340;
            tick.Tick += delegate { SampleProcesses(); Refresh2(); };
        }

        protected override void OnOpened()
        {
            SysInfo.Apply();
            SysInfo.SampleFast();
            lastCpu.Clear();
            SampleProcesses();
            tick.Start();
        }

        protected override void OnClosedFlyout()
        {
            tick.Stop();
            lastCpu.Clear();
            top.Clear();
            SysInfo.Apply();
        }

        /// <summary>Top apps by CPU: process CPU-time deltas between samples (only while this panel is open).</summary>
        void SampleProcesses()
        {
            var now = DateTime.UtcNow;
            double secs = (now - lastSample).TotalSeconds;
            var next = new Dictionary<int, TimeSpan>();
            var byName = new Dictionary<string, Proc>(StringComparer.OrdinalIgnoreCase);
            int cores = Environment.ProcessorCount;
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id == 0) continue;
                    var t = p.TotalProcessorTime;
                    next[p.Id] = t;
                    TimeSpan prev;
                    double cpu = 0;
                    if (lastCpu.TryGetValue(p.Id, out prev) && secs > 0.3) cpu = (t - prev).TotalSeconds / secs / cores * 100;
                    Proc agg;
                    if (!byName.TryGetValue(p.ProcessName, out agg)) { agg = new Proc { Name = p.ProcessName }; byName[p.ProcessName] = agg; }
                    agg.Cpu += cpu;
                    agg.Mem += p.WorkingSet64;
                }
                catch { }
                finally { p.Dispose(); }
            }
            lastCpu = next;
            lastSample = now;
            var list = new List<Proc>(byName.Values);
            list.Sort((a, b) => b.Cpu != a.Cpu ? b.Cpu.CompareTo(a.Cpu) : b.Mem.CompareTo(a.Mem));
            top = list.GetRange(0, Math.Min(6, list.Count));
        }

        void Card(UI ui, RectangleF r)
        {
            ui.Fill(r, Theme.Tile, 14 * S);
            using (var pen = new Pen(Theme.TileBorder, 1)) using (var p = Gfx.RoundRect(r, 14 * S)) ui.G.DrawPath(pen, p);
        }

        protected override void Build(UI ui)
        {
            float pad = 12 * S, w = ui.W - pad * 2;
            var up = TimeSpan.FromMilliseconds(Environment.TickCount & int.MaxValue);
            ui.Y = 6 * S;
            ui.Header("System", null, null, Environment.MachineName + "  ·  up " + (up.Days > 0 ? up.Days + "d " : "") + up.Hours + "h " + up.Minutes + "m");

            // CPU with history graph
            var c = new RectangleF(pad, ui.Y, w, 100 * S);
            Card(ui, c);
            ui.Text("CPU", new RectangleF(c.X + 14 * S, c.Y + 10 * S, 100 * S, 18 * S), ui.Bold, ui.TextBrush);
            if (SysInfo.TempC > 0)
                ui.Text(SysInfo.TempC + "°C", new RectangleF(c.X + 52 * S, c.Y + 10 * S, 80 * S, 18 * S), ui.Small, ui.DimBrush);
            using (var big = new Font(Fonts.UiBold, 20f * S, GraphicsUnit.Pixel))
                ui.Text(SysInfo.Cpu + "%", new RectangleF(c.X, c.Y + 6 * S, c.Width - 14 * S, 26 * S), big, ui.TextBrush, 2);
            Graph(ui, new RectangleF(c.X + 14 * S, c.Y + 40 * S, c.Width - 28 * S, 48 * S), SysInfo.CpuHistory, 60);
            ui.Y += c.Height + 8 * S;

            // memory
            var m = new RectangleF(pad, ui.Y, w, 62 * S);
            Card(ui, m);
            ui.Text("Memory", new RectangleF(m.X + 14 * S, m.Y + 10 * S, 120 * S, 18 * S), ui.Bold, ui.TextBrush);
            ui.Text(SysInfo.RamUsedGB.ToString("0.0") + " of " + SysInfo.RamTotalGB.ToString("0.0") + " GB", new RectangleF(m.X, m.Y + 10 * S, m.Width - 14 * S, 18 * S), ui.Small, ui.DimBrush, 2);
            Bar(ui, new RectangleF(m.X + 14 * S, m.Y + 38 * S, m.Width - 28 * S, 8 * S), SysInfo.Ram / 100f, false);
            ui.Y += m.Height + 8 * S;

            // drives
            var drives = new List<DriveInfo>();
            try { foreach (var d in DriveInfo.GetDrives()) if (d.DriveType == DriveType.Fixed && d.IsReady) drives.Add(d); } catch { }
            if (drives.Count > 0)
            {
                var dr = new RectangleF(pad, ui.Y, w, 18 * S + drives.Count * 40 * S);
                Card(ui, dr);
                float y = dr.Y + 10 * S;
                foreach (var d in drives)
                {
                    double total = d.TotalSize / 1073741824.0, free = d.AvailableFreeSpace / 1073741824.0;
                    ui.Text("Disk " + d.Name.TrimEnd('\\'), new RectangleF(dr.X + 14 * S, y, 120 * S, 18 * S), ui.Bold, ui.TextBrush);
                    ui.Text(free.ToString("0") + " GB free of " + total.ToString("0"), new RectangleF(dr.X, y, dr.Width - 14 * S, 18 * S), ui.Small, ui.DimBrush, 2);
                    Bar(ui, new RectangleF(dr.X + 14 * S, y + 23 * S, dr.Width - 28 * S, 6 * S), (float)((total - free) / Math.Max(1, total)), false);
                    y += 40 * S;
                }
                ui.Y += dr.Height + 8 * S;
            }

            // network
            var n = new RectangleF(pad, ui.Y, w, 44 * S);
            Card(ui, n);
            ui.Text("Network", new RectangleF(n.X + 14 * S, n.Y, 120 * S, n.Height), ui.Bold, ui.TextBrush);
            ui.Text("↓ " + SysInfo.Speed(SysInfo.DownBps) + "    ↑ " + SysInfo.Speed(SysInfo.UpBps), new RectangleF(n.X, n.Y, n.Width - 14 * S, n.Height), ui.Small, ui.DimBrush, 2);
            ui.Y += n.Height + 8 * S;

            // top apps
            ui.Section("TOP APPS BY CPU");
            foreach (var p in top)
            {
                var r = new RectangleF(pad, ui.Y, w, 28 * S);
                ui.Text(p.Name, new RectangleF(r.X + 8 * S, r.Y, r.Width - 150 * S, r.Height), ui.Body, ui.TextBrush);
                ui.Text((p.Mem / 1048576) + " MB", new RectangleF(r.X, r.Y, r.Width - 66 * S, r.Height), ui.Small, ui.DimBrush, 2);
                ui.Text(p.Cpu.ToString("0.0") + "%", new RectangleF(r.X, r.Y, r.Width - 8 * S, r.Height), ui.Bold, ui.TextBrush, 2);
                ui.Y += 28 * S;
            }
            ui.Gap(6);
            ui.Button("tm", "Open Task Manager", () => Launcher.Run("taskmgr.exe"));
        }

        void Bar(UI ui, RectangleF t, float v, bool good)
        {
            v = Math.Max(0, Math.Min(1, v));
            ui.Fill(t, Theme.Track, t.Height / 2);
            Color c = v > 0.85f ? Color.FromArgb(255, 255, 69, 58) : (v > 0.65f ? Color.FromArgb(255, 255, 159, 10) : Theme.Accent);
            ui.Fill(new RectangleF(t.X, t.Y, Math.Max(t.Height, t.Width * v), t.Height), c, t.Height / 2);
        }

        /// <summary>Filled area graph of the last samples (newest on the right).</summary>
        public static void Graph(UI ui, RectangleF r, List<int> data, int slots)
        {
            if (data.Count < 2) return;
            var pts = new List<PointF>();
            int start = Math.Max(0, data.Count - slots);
            float step = r.Width / (slots - 1);
            float x = r.Right - (data.Count - start - 1) * step;
            for (int i = start; i < data.Count; i++, x += step)
                pts.Add(new PointF(x, r.Bottom - r.Height * Math.Max(0, Math.Min(100, data[i])) / 100f));
            using (var path = new GraphicsPath())
            {
                path.AddLines(pts.ToArray());
                path.AddLine(pts[pts.Count - 1], new PointF(pts[pts.Count - 1].X, r.Bottom));
                path.AddLine(new PointF(pts[pts.Count - 1].X, r.Bottom), new PointF(pts[0].X, r.Bottom));
                path.CloseFigure();
                using (var br = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2),
                    Color.FromArgb(110, Theme.Accent), Color.FromArgb(10, Theme.Accent), 90f))
                    ui.G.FillPath(br, path);
            }
            using (var pen = new Pen(Theme.Accent, 1.6f * ui.S) { LineJoin = LineJoin.Round })
                ui.G.DrawLines(pen, pts.ToArray());
        }
    }
}
