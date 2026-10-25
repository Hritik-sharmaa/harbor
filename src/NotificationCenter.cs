using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace Harbor
{
    /// <summary>
    /// macOS-style Notification Center (click the clock / top-right hot corner):
    /// your real Windows notifications grouped by app, plus calendar, now playing and system widgets.
    /// </summary>
    sealed class NotificationCenter : Flyout
    {
        sealed class Note
        {
            public uint Id;
            public string App, Aumid, Title, Body;
            public DateTimeOffset Time;
        }

        static NotificationCenter inst;
        public static bool IsOpen { get { return inst != null && inst.Visible; } }

        public static void Toggle(TopBar bar, RectangleF zone)
        {
            if (inst == null) inst = new NotificationCenter();
            var wa = Screen.FromHandle(bar.Handle).WorkingArea;
            inst.MaxLogicalHeight = (int)((wa.Height - 16 * bar.Disp.Scale) / bar.Disp.Scale);
            inst.bar = bar;
            inst.ShowBelow(bar, zone, "clock", 2);
        }

        TopBar bar;
        List<Note> notes = new List<Note>();
        readonly Dictionary<string, Bitmap> logos = new Dictionary<string, Bitmap>();
        bool loading, denied;
        DateTime month;
        readonly Timer tick = new Timer { Interval = 2000 };

        internal NotificationCenter()
        {
            LogicalWidth = 372;
            tick.Tick += delegate { if (Visible) Refresh2(); };
            Media.Changed += delegate { if (Visible) Refresh2(); };
        }

        protected override void OnOpened()
        {
            var t = DateTime.Today;
            month = new DateTime(t.Year, t.Month, 1);
            Load();
            SysInfo.SampleFast();
            tick.Start();
            SysInfo.Apply();
        }

        protected override void OnClosedFlyout()
        {
            tick.Stop();
            SysInfo.Apply();
        }

        public static bool Demo; // --snapshot --demo: sample notifications for README screenshots

        void Load()
        {
            if (Demo)
            {
                var now = DateTimeOffset.Now;
                notes = new List<Note>
                {
                    new Note { Id = 1, App = "Messages", Title = "Alex", Body = "Are we still on for dinner tonight? I booked a table for 8.", Time = now.AddMinutes(-2) },
                    new Note { Id = 2, App = "Calendar", Title = "Design review", Body = "Starts in 15 minutes · Conference room B", Time = now.AddMinutes(-9) },
                    new Note { Id = 3, App = "Mail", Title = "Your order has shipped", Body = "Track your package — arriving Thursday.", Time = now.AddHours(-1) },
                };
                loading = false;
                return;
            }
            loading = true;
            try
            {
                var l = UserNotificationListener.Current;
                Async.Then(l.RequestAccessAsync(), access =>
                {
                    if (access != UserNotificationListenerAccessStatus.Allowed) { denied = true; loading = false; Refresh2(); return; }
                    Async.Then(l.GetNotificationsAsync(NotificationKinds.Toast), list =>
                    {
                        loading = false;
                        var res = new List<Note>();
                        if (list != null)
                            foreach (var n in list)
                            {
                                var note = new Note { Id = n.Id, Time = n.CreationTime };
                                try { note.App = n.AppInfo.DisplayInfo.DisplayName; note.Aumid = n.AppInfo.AppUserModelId; } catch { note.App = "App"; }
                                try
                                {
                                    var b = n.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric);
                                    if (b != null)
                                    {
                                        var parts = new List<string>();
                                        foreach (var t in b.GetTextElements()) if (!string.IsNullOrWhiteSpace(t.Text)) parts.Add(t.Text.Trim());
                                        if (parts.Count > 0) note.Title = parts[0];
                                        if (parts.Count > 1) note.Body = string.Join("\n", parts.GetRange(1, parts.Count - 1).ToArray());
                                    }
                                }
                                catch { }
                                res.Add(note);
                                LoadLogo(n, note.App);
                            }
                        res.Sort((a, b2) => b2.Time.CompareTo(a.Time));
                        notes = res;
                        Refresh2();
                    });
                });
            }
            catch (Exception ex) { loading = false; denied = true; App.Log(ex); }
        }

        void LoadLogo(UserNotification n, string app)
        {
            if (app == null || logos.ContainsKey(app)) return;
            logos[app] = null;
            string aumid = null;
            try { aumid = n.AppInfo.AppUserModelId; } catch { }
            if (string.IsNullOrEmpty(aumid)) return;
            var arr = ShellIcons.LoadIcons(@"shell:AppsFolder\" + aumid, (int)Math.Round(22 * S));
            if (arr != null) logos[app] = arr[0];
        }
        void Dismiss(Note n)
        {
            try { UserNotificationListener.Current.RemoveNotification(n.Id); } catch { }
            notes.Remove(n);
        }

        void ClearAll()
        {
            try { UserNotificationListener.Current.ClearNotifications(); } catch { }
            notes.Clear();
        }

        static string Ago(DateTimeOffset t)
        {
            var d = DateTimeOffset.Now - t;
            if (d.TotalMinutes < 1) return "now";
            if (d.TotalMinutes < 60) return (int)d.TotalMinutes + "m ago";
            if (d.TotalHours < 24) return (int)d.TotalHours + "h ago";
            if (d.TotalDays < 2) return "Yesterday";
            return t.LocalDateTime.ToString("d MMM", CultureInfo.CurrentCulture);
        }

        void Card(UI ui, RectangleF r, bool hot)
        {
            ui.Fill(r, hot ? Theme.TileHover : Theme.Tile, 14 * S);
            using (var pen = new Pen(Theme.TileBorder, 1)) using (var p = Gfx.RoundRect(r, 14 * S)) ui.G.DrawPath(pen, p);
        }

        protected override void Build(UI ui)
        {
            float pad = 12 * S, w = ui.W - pad * 2;
            ui.Y = 14 * S;
            var now = DateTime.Now;
            var culture = CultureInfo.CurrentCulture;
            using (var big = new Font(Fonts.UiBold, 30f * S, GraphicsUnit.Pixel))
                ui.Text(now.ToString(culture.DateTimeFormat.ShortTimePattern.Contains("H") ? "HH:mm" : "h:mm", culture),
                    new RectangleF(pad + 4 * S, ui.Y, w, 38 * S), big, ui.TextBrush);
            ui.Text(now.ToString("dddd, d MMMM", culture), new RectangleF(pad + 6 * S, ui.Y + 38 * S, w, 18 * S), ui.Body, ui.DimBrush);
            ui.Y += 66 * S;

            // ---- notifications
            var head = new RectangleF(pad, ui.Y, w, 24 * S);
            ui.Text("NOTIFICATIONS", new RectangleF(head.X + 4 * S, head.Y, 200 * S, head.Height), ui.Small, ui.DimBrush);
            if (notes.Count > 0)
            {
                var clr = new RectangleF(head.Right - 70 * S, head.Y, 70 * S, head.Height);
                if (Hover == "clear") ui.Fill(clr, Theme.TileHover, 8 * S);
                using (var ab = new SolidBrush(Theme.Accent)) ui.Text("Clear All", clr, ui.Small, ab, 1);
                ui.AddHit("clear", clr, ClearAll, true);
            }
            ui.Y += 28 * S;

            if (denied) InfoCard(ui, pad, w, "Allow Harbor to read notifications in Windows Settings › Privacy › Notifications.");
            else if (loading && notes.Count == 0) InfoCard(ui, pad, w, "Loading…");
            else if (notes.Count == 0) InfoCard(ui, pad, w, "You're all caught up");
            else
            {
                int shown = 0;
                foreach (var n in notes)
                {
                    if (++shown > 20) break;
                    NoteCard(ui, pad, w, n);
                }
            }

            // ---- widgets
            if (Look.Bool("NcWidgets"))
            {
                ui.Y += 8 * S;
                ui.Text("WIDGETS", new RectangleF(pad + 4 * S, ui.Y, 200 * S, 24 * S), ui.Small, ui.DimBrush);
                ui.Y += 28 * S;
                CalendarCard(ui, pad, w);
                if (Media.Has) MediaCard(ui, pad, w);
                SystemCard(ui, pad, w);
            }

            ui.Y += 4 * S;
            var set = new RectangleF(pad, ui.Y, w, 30 * S);
            if (Hover == "nset") ui.Fill(set, Theme.TileHover, 8 * S);
            ui.Text("Notification Settings…", new RectangleF(set.X + 10 * S, set.Y, set.Width, set.Height), ui.Small, ui.DimBrush);
            ui.AddHit("nset", set, () => Launcher.Run("ms-settings:notifications"));
            ui.Y += 36 * S;
        }

        void InfoCard(UI ui, float pad, float w, string text)
        {
            var r = new RectangleF(pad, ui.Y, w, 52 * S);
            Card(ui, r, false);
            ui.Text(text, r, ui.Small, ui.DimBrush, 1);
            ui.Y += r.Height + 8 * S;
        }

        void NoteCard(UI ui, float pad, float w, Note n)
        {
            string id = "n" + n.Id;
            float bodyH = 0;
            float tx = pad + 44 * S, tw = w - 56 * S;
            if (!string.IsNullOrEmpty(n.Body))
            {
                var sz = ui.G.MeasureString(n.Body, ui.Small, (int)tw);
                bodyH = Math.Min(sz.Height, 3 * 16 * S);
            }
            float h = 54 * S + bodyH;
            var r = new RectangleF(pad, ui.Y, w, h);
            bool hot = Hover == id || Hover == id + "x";
            Card(ui, r, hot);
            ui.AddHit(id, r, () =>
            {
                if (!string.IsNullOrEmpty(n.Aumid)) Launcher.Open(@"shell:AppsFolder\" + n.Aumid);
                Dismiss(n);
            });
            Bitmap logo;
            logos.TryGetValue(n.App ?? "", out logo);
            var lr = new RectangleF(pad + 12 * S, r.Y + 12 * S, 22 * S, 22 * S);
            if (logo != null) ui.G.DrawImage(logo, lr);
            else ui.Fill(lr, Theme.CircleOff, 6 * S);
            ui.Text((n.App ?? "").ToUpperInvariant(), new RectangleF(tx, r.Y + 8 * S, tw - 70 * S, 15 * S), ui.Small, ui.DimBrush);
            ui.Text(Ago(n.Time), new RectangleF(tx, r.Y + 8 * S, tw, 15 * S), ui.Small, ui.DimBrush, 2);
            ui.Text(n.Title ?? "", new RectangleF(tx, r.Y + 26 * S, tw, 18 * S), ui.Bold, ui.TextBrush);
            if (bodyH > 0)
                using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisWord })
                    ui.G.DrawString(n.Body, ui.Small, ui.DimBrush, new RectangleF(tx, r.Y + 45 * S, tw, bodyH), sf);
            if (hot)
            {
                var x = new RectangleF(r.X - 6 * S, r.Y - 6 * S, 20 * S, 20 * S);
                using (var br = new SolidBrush(Theme.PanelSolid)) ui.G.FillEllipse(br, x);
                using (var pen = new Pen(Theme.TileBorder, 1)) ui.G.DrawEllipse(pen, x);
                using (var f = new Font(Fonts.Icons, 7f * S, GraphicsUnit.Pixel)) ui.Glyph("", x, f, ui.TextBrush);
                ui.AddHit(id + "x", x, () => Dismiss(n), true);
            }
            ui.Y += h + 8 * S;
        }

        void CalendarCard(UI ui, float pad, float w)
        {
            var culture = CultureInfo.CurrentCulture;
            var today = DateTime.Today;
            float cellH = 26 * S;
            var r = new RectangleF(pad, ui.Y, w, 44 * S + cellH * 7);
            Card(ui, r, false);
            ui.Text(month.ToString("MMMM yyyy", culture), new RectangleF(r.X + 14 * S, r.Y + 8 * S, 200 * S, 22 * S), ui.Bold, ui.TextBrush);
            var prev = new RectangleF(r.Right - 62 * S, r.Y + 6 * S, 26 * S, 26 * S);
            var next = new RectangleF(r.Right - 34 * S, r.Y + 6 * S, 26 * S, 26 * S);
            if (Hover == "mprev") ui.Fill(prev, Theme.TileHover, 7 * S);
            if (Hover == "mnext") ui.Fill(next, Theme.TileHover, 7 * S);
            using (var f = new Font(Fonts.Icons, 10f * S, GraphicsUnit.Pixel))
            {
                ui.Glyph("", prev, f, ui.TextBrush);
                ui.Glyph("", next, f, ui.TextBrush);
            }
            ui.AddHit("mprev", prev, () => month = month.AddMonths(-1), true);
            ui.AddHit("mnext", next, () => month = month.AddMonths(1), true);
            var first = culture.DateTimeFormat.FirstDayOfWeek;
            float cw = (r.Width - 16 * S) / 7, gx = r.X + 8 * S, gy = r.Y + 38 * S;
            for (int i = 0; i < 7; i++)
                ui.Text(culture.DateTimeFormat.GetShortestDayName((DayOfWeek)(((int)first + i) % 7)),
                    new RectangleF(gx + cw * i, gy, cw, cellH), ui.Small, ui.DimBrush, 1);
            int offset = ((int)month.DayOfWeek - (int)first + 7) % 7;
            var start = month.AddDays(-offset);
            for (int row = 0; row < 6; row++)
                for (int c = 0; c < 7; c++)
                {
                    var d = start.AddDays(row * 7 + c);
                    var cell = new RectangleF(gx + cw * c, gy + cellH * (row + 1), cw, cellH);
                    var dot = new RectangleF(cell.X + cell.Width / 2 - 12 * S, cell.Y + 1 * S, 24 * S, 24 * S);
                    bool isToday = d == today;
                    if (isToday) using (var br = new SolidBrush(Theme.Accent)) ui.G.FillEllipse(br, dot);
                    Brush b = isToday ? Brushes.White : (d.Month == month.Month ? (Brush)ui.TextBrush : ui.DimBrush);
                    ui.Text(d.Day.ToString(culture), dot, ui.Small, b, 1);
                }
            ui.Y += r.Height + 8 * S;
        }

        void MediaCard(UI ui, float pad, float w)
        {
            var r = new RectangleF(pad, ui.Y, w, 64 * S);
            Card(ui, r, Hover == "media");
            ui.AddHit("media", new RectangleF(r.X, r.Y, r.Width - 110 * S, r.Height), () => Media.PlayPause(), true);
            var art = new RectangleF(r.X + 10 * S, r.Y + 10 * S, 44 * S, 44 * S);
            MediaFlyout.DrawArt(ui, art, 9 * S);
            float tx = art.Right + 10 * S, tw = r.Right - 112 * S - tx;
            ui.Text(Media.Title, new RectangleF(tx, r.Y + 13 * S, tw, 18 * S), ui.Bold, ui.TextBrush);
            ui.Text(string.IsNullOrEmpty(Media.Artist) ? Media.AppName : Media.Artist, new RectangleF(tx, r.Y + 33 * S, tw, 16 * S), ui.Small, ui.DimBrush);
            float bw = 32 * S, bx = r.Right - bw * 3 - 8 * S, by = r.Y + (r.Height - bw) / 2;
            Btn(ui, "mp", new RectangleF(bx, by, bw, bw), "", Media.Previous, ui.Icon);
            Btn(ui, "mpp", new RectangleF(bx + bw, by, bw, bw), Media.Playing ? "" : "", Media.PlayPause, ui.BigIcon);
            Btn(ui, "mn", new RectangleF(bx + bw * 2, by, bw, bw), "", Media.Next, ui.Icon);
            ui.Y += r.Height + 8 * S;
        }

        void Btn(UI ui, string id, RectangleF r, string glyph, Action act, Font f)
        {
            if (Hover == id) using (var br = new SolidBrush(Theme.TileHover)) ui.G.FillEllipse(br, r);
            ui.Glyph(glyph, r, f, ui.TextBrush);
            ui.AddHit(id, r, act, true);
        }

        void SystemCard(UI ui, float pad, float w)
        {
            var ps = SystemInformation.PowerStatus;
            bool battery = (ps.BatteryChargeStatus & BatteryChargeStatus.NoSystemBattery) == 0 &&
                           ps.BatteryChargeStatus != BatteryChargeStatus.Unknown && ps.BatteryLifePercent <= 1f;
            int rows = battery ? 3 : 2;
            var r = new RectangleF(pad, ui.Y, w, 14 * S + rows * 30 * S);
            Card(ui, r, false);
            float y = r.Y + 10 * S;
            Meter(ui, r, ref y, "", "CPU", SysInfo.Cpu / 100f, SysInfo.Cpu + "%");
            Meter(ui, r, ref y, "", "Memory", SysInfo.Ram / 100f, SysInfo.Ram + "%");
            if (battery)
                Meter(ui, r, ref y, "", "Battery", ps.BatteryLifePercent,
                    (int)Math.Round(ps.BatteryLifePercent * 100) + "%" + (ps.PowerLineStatus == PowerLineStatus.Online ? " ⚡" : ""));
            ui.Y += r.Height + 8 * S;
        }

        void Meter(UI ui, RectangleF card, ref float y, string glyph, string label, float v, string value)
        {
            ui.Glyph(glyph, new RectangleF(card.X + 12 * S, y, 20 * S, 22 * S), ui.Icon, ui.TextBrush);
            ui.Text(label, new RectangleF(card.X + 40 * S, y, 80 * S, 22 * S), ui.Body, ui.TextBrush);
            var t = new RectangleF(card.X + 120 * S, y + 8 * S, card.Width - 180 * S, 6 * S);
            ui.Fill(t, Theme.Track, 3 * S);
            // negative value = "higher is better" (battery): green when full, red when low
            bool good = v < 0; v = Math.Abs(v);
            Color c = good ? (v < 0.2f ? Color.FromArgb(255, 255, 69, 58) : Color.FromArgb(255, 48, 209, 88))
                           : (v > 0.85f ? Color.FromArgb(255, 255, 69, 58) : (v > 0.65f ? Color.FromArgb(255, 255, 159, 10) : Theme.Accent));
            ui.Fill(new RectangleF(t.X, t.Y, Math.Max(6 * S, t.Width * Math.Max(0, Math.Min(1, v))), t.Height), c, 3 * S);
            ui.Text(value, new RectangleF(card.X, y, card.Width - 12 * S, 22 * S), ui.Small, ui.DimBrush, 2);
            y += 30 * S;
        }
    }
}
