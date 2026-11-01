using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Harbor
{
    /// <summary>The island's Media, Pomodoro and Reminders pages, and the small springy controls they share.</summary>
    sealed partial class Island
    {
        // ------------------------------------------------------------------ shared controls

        /// <summary>A bare glyph button: a soft circle fades in under the mouse, and it squashes when pressed.</summary>
        void IconBtn(UI ui, string id, PointF c, float r, string glyph, float glyphPx, Action act, Brush b, bool enabled = true)
        {
            float k = Scale(id), hot = enabled ? Hot(id) : 0;
            var rr = new RectangleF(c.X - r * k, c.Y - r * k, 2 * r * k, 2 * r * k);
            if (hot > 0.01f) using (var hb = new SolidBrush(Color.FromArgb((int)(40 * hot), 255, 255, 255))) ui.G.FillEllipse(hb, rr);
            using (var gf = new Font(Fonts.Icons, glyphPx * S * k, GraphicsUnit.Pixel))
            using (var faint = new SolidBrush(NowPlayingView.Faint))
                ui.Glyph(glyph, rr, gf, enabled ? b : faint);
            if (enabled) ui.AddHit(id, rr, act, true);
        }

        /// <summary>A rounded pill with an optional glyph; 'fill' is its colour (it brightens under the mouse).</summary>
        RectangleF PillBtn(UI ui, string id, float x, float cy, string glyph, string text, Color fill, Brush b, Action act, Fonts2 f, float h = 34)
        {
            h *= S;
            float gw = glyph != null ? 18 * S : 0;
            float w = (text != null ? ui.G.MeasureString(text, f.Bold).Width : 0) + gw + (text != null ? 26 : 22) * S;
            if (text == null) w = Math.Max(w, 70 * S);
            float k = Scale(id), hot = Hot(id);
            var r = new RectangleF(x + w * (1 - k) / 2, cy - h * k / 2, w * k, h * k);
            var c = Gfx.Lighten(fill, 0.12f * hot);
            ui.Fill(r, c, r.Height / 2);
            float tx = r.X + (text != null ? 13 * S : (r.Width - gw) / 2);
            if (glyph != null)
                using (var gf = new Font(Fonts.Icons, 12.5f * S * k, GraphicsUnit.Pixel))
                    ui.Glyph(glyph, new RectangleF(tx - 2 * S, r.Y, gw + 4 * S, r.Height), gf, b);
            if (text != null) ui.Text(text, new RectangleF(tx + gw + (glyph != null ? 4 : 0) * S, r.Y, r.Width, r.Height), f.Bold, b);
            ui.AddHit(id, r, act, true);
            return new RectangleF(x, cy - h / 2, w, h);
        }

        float PillBtnWidth(UI ui, string glyph, string text, Fonts2 f)
        {
            float gw = glyph != null ? 18 * S : 0;
            float w = (text != null ? ui.G.MeasureString(text, f.Bold).Width : 0) + gw + (text != null ? 26 : 22) * S;
            return text == null ? Math.Max(w, 70 * S) : w;
        }

        /// <summary>A tick circle; ticking it pops the check in with a bounce.</summary>
        void TickCircle(UI ui, RectangleF r, bool done, bool hot, string id = null)
        {
            float pop = id != null ? Spring("tick:" + id, done ? 1 : 0, 520, 18) : (done ? 1 : 0);
            using (var pen = new Pen(Color.FromArgb(hot ? 230 : 150, 255, 255, 255), 1.6f * S)) ui.G.DrawEllipse(pen, r);
            if (pop > 0.02f)
            {
                var pr = RectangleF.Inflate(r, r.Width * (pop - 1) / 2, r.Height * (pop - 1) / 2);
                using (var b = new SolidBrush(Color.FromArgb((int)(255 * Math.Min(1, pop)), 48, 209, 88))) ui.G.FillEllipse(b, pr);
                using (var gf = new Font(Fonts.Icons, Math.Max(1, pr.Height * 0.55f), GraphicsUnit.Pixel)) ui.Glyph("", pr, gf, Brushes.White);
            }
        }

        // ------------------------------------------------------------------ media

        void MediaTab(UI ui, RectangleF c, Fonts2 f)
        {
            view.NoteSong();
            if (!Media.Has && LastPlayed.Has) { LastPlayedCard(ui, c, f); return; }
            if (!Media.Has)
            {
                using (var gf = new Font(Fonts.Icons, 26f * S, GraphicsUnit.Pixel)) ui.Glyph("", new RectangleF(c.X, c.Y + 34 * S, c.Width, 36 * S), gf, f.Faint);
                ui.Text("Nothing playing", new RectangleF(c.X, c.Y + 78 * S, c.Width, 24 * S), f.Title, f.White, 1);
                ui.Text("Play something in Spotify, YouTube or any media app", new RectangleF(c.X, c.Y + 104 * S, c.Width, 20 * S), f.Body, f.Dim, 1);
                return;
            }
            bool lyrics = Look.Bool("Lyrics");
            float lw = lyrics ? 318 * S : c.Width;

            // cover, title, artist
            var art = new RectangleF(c.X + 4 * S, c.Y + 4 * S, 84 * S, 84 * S);
            view.Cover(ui, art, 14 * S);
            float tx = art.Right + 18 * S, tw = c.X + lw - tx;
            using (var title = new Font(Fonts.UiBold, 18f * S, GraphicsUnit.Pixel))
                ui.Text(Media.Title, new RectangleF(tx, art.Y + 18 * S, tw, 26 * S), title, f.White);
            ui.Text(Media.Artist, new RectangleF(tx, art.Y + 46 * S, tw, 20 * S), f.Body, f.Dim);

            // elapsed · bar · remaining
            float y = art.Bottom + 26 * S;
            if (Media.Duration > TimeSpan.Zero) view.ProgressRow(ui, this, c.X + 4 * S, y, lw - 8 * S, f.Bold);

            // lyrics · prev play next · output
            float by = y + 40 * S, mid = c.X + lw / 2;
            IconBtn(ui, "mlyr", new PointF(c.X + 18 * S, by), 17 * S, lyrics ? "" : "", 15, view.ToggleLyrics, lyrics ? f.White : f.Dim);
            IconBtn(ui, "prev", new PointF(mid - 58 * S, by), 19 * S, "", 17, Media.Previous, f.White);
            IconBtn(ui, "play", new PointF(mid, by), 22 * S, Media.Playing ? "" : "", 21, Media.PlayPause, f.White);
            IconBtn(ui, "next", new PointF(mid + 58 * S, by), 19 * S, "", 17, Media.Next, f.White);
            OutputButton(ui, new PointF(c.X + lw - 18 * S, by), f);

            if (!lyrics) return;
            float sx = c.X + lw + 20 * S;
            using (var pen = new Pen(Color.FromArgb(40, 255, 255, 255), 1)) ui.G.DrawLine(pen, sx, c.Y + 8 * S, sx, c.Bottom - 8 * S);
            var box = new RectangleF(sx + 14 * S, c.Y - 4 * S, c.Right - sx - 14 * S, c.Height + 8 * S);
            if (!view.Lyrics(ui, this, box, 18f)) ui.Text("Lyrics are off", box, f.Body, f.Faint, 1);
        }

        /// <summary>The sound output: a speaker glyph that steps to the next output (its name shows under the mouse).</summary>
        void OutputButton(UI ui, PointF c, Fonts2 f)
        {
            if (outputs == null || unchecked(Environment.TickCount - outputsAt) > 5000) { outputs = AudioOutputs.List(); outputsAt = Environment.TickCount; }
            var cur = outputs.Find(d => d.Default);
            if (cur == null || outputs.Count < 2) return;
            IconBtn(ui, "mout", c, 17 * S, "", 15, () => { AudioOutputs.Next(); outputs = null; }, f.Dim);
            float a = Hot("mout");
            if (a > 0.05f)
            {
                float w = ui.G.MeasureString(cur.Name, f.Small).Width + 16 * S;
                var r = new RectangleF(c.X + 20 * S - w, c.Y - 44 * S + (1 - a) * 6 * S, w, 22 * S);
                using (var bg = new SolidBrush(Color.FromArgb((int)(240 * a), 44, 44, 48))) using (var p = Gfx.RoundRect(r, 11 * S)) ui.G.FillPath(bg, p);
                using (var tb = new SolidBrush(Color.FromArgb((int)(240 * a), 255, 255, 255))) ui.Text(cur.Name, r, f.Small, tb, 1);
            }
        }

        // ------------------------------------------------------------------ pomodoro

        static readonly Color Ember = Color.FromArgb(255, 255, 99, 99), Calm = Color.FromArgb(255, 100, 210, 160);

        void PomodoroTab(UI ui, RectangleF c, Fonts2 f)
        {
            var g = ui.G;
            bool work = Pomodoro.Current == Pomodoro.Phase.Work;
            Color tint = work ? Ember : Calm;

            // the ring, glowing in the phase's colour; the arc glides rather than ticks
            float d = 112 * S, cx = c.X + 92 * S, cy = c.Y + 76 * S;
            var ring = new RectangleF(cx - d / 2, cy - d / 2, d, d);
            float p = Spring("pomo", Pomodoro.Progress, 120, 22);
            float glow = Pomodoro.Running ? 1 : 0.55f;
            for (int i = 3; i >= 1; i--)
                using (var gp = new Pen(Color.FromArgb((int)(16 * glow * (4 - i)), tint), (10 + i * 7) * S)) g.DrawEllipse(gp, ring);
            using (var track = new Pen(Color.FromArgb(70, tint), 10 * S)) g.DrawEllipse(track, ring);
            if (p * 360 >= 1) // GDI+ fails on a near-zero arc
                using (var arc = new Pen(tint, 10 * S) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(arc, ring, -90, 360 * Math.Min(1, p));
            using (var inner = new SolidBrush(Color.FromArgb(255, 34, 10, 10))) if (work) g.FillEllipse(inner, RectangleF.Inflate(ring, -9 * S, -9 * S));
            using (var gf = new Font(Fonts.Icons, 30f * S, GraphicsUnit.Pixel)) ui.Glyph(work ? "" : "", ring, gf, f.White);

            // today's sessions under the ring
            int done = Pomodoro.SessionsToday;
            for (int i = 0; i < 4; i++)
                using (var b = new SolidBrush(i < done % 4 || (done > 0 && done % 4 == 0) ? tint : Color.FromArgb(50, 255, 255, 255)))
                    g.FillEllipse(b, cx - 26 * S + i * 14 * S, ring.Bottom + 22 * S, 7 * S, 7 * S);
            ui.Text(done == 0 ? "No sessions yet" : done + (done == 1 ? " session today" : " sessions today"), new RectangleF(cx - 90 * S, ring.Bottom + 34 * S, 180 * S, 18 * S), f.Small, f.Faint, 1);

            // the time, big, with reset and skip beside it
            float rx = c.X + 210 * S, rw = c.Right - rx, mid = rx + rw / 2;
            var rem = Pomodoro.Remaining;
            string time = ((int)rem.TotalMinutes).ToString("00") + ":" + rem.Seconds.ToString("00");
            using (var huge = new Font(Fonts.UiBold, 46f * S, GraphicsUnit.Pixel))
            {
                float tw = g.MeasureString(time, huge, 400, StringFormat.GenericTypographic).Width;
                ui.Text(time, new RectangleF(mid - tw / 2 - 20 * S, c.Y + 6 * S, tw + 10 * S, 56 * S), huge, f.White);
                IconBtn(ui, "preset", new PointF(mid + tw / 2 + 4 * S, c.Y + 34 * S), 15 * S, "", 13, Pomodoro.Reset, f.White);
                IconBtn(ui, "pskip", new PointF(mid + tw / 2 + 38 * S, c.Y + 34 * S), 15 * S, "", 13, Pomodoro.Skip, f.Dim);
            }
            string label = Pomodoro.Running ? (work ? "Focus time" : Pomodoro.Name(Pomodoro.Current)) : (Pomodoro.Progress > 0 ? "Paused" : (work ? "Ready to focus" : Pomodoro.Name(Pomodoro.Current)));
            ui.Text(label, new RectangleF(rx, c.Y + 62 * S, rw, 20 * S), f.Body, f.Dim, 1);

            // the other two phases either side of play / pause
            var others = new List<Pomodoro.Phase>();
            foreach (Pomodoro.Phase ph in Enum.GetValues(typeof(Pomodoro.Phase))) if (ph != Pomodoro.Current) others.Add(ph);
            string[] glyphs = { "", "", "" }; // work: timer, short break: cup, long break: moon
            Func<Pomodoro.Phase, string> nameOf = ph => ph == Pomodoro.Phase.Work ? "Focus" : Pomodoro.Name(ph);
            float w0 = PillBtnWidth(ui, glyphs[(int)others[0]], nameOf(others[0]), f), w2 = PillBtnWidth(ui, glyphs[(int)others[1]], nameOf(others[1]), f), w1 = 70 * S, gap = 10 * S;
            float x = mid - (w0 + w1 + w2 + 2 * gap) / 2, py = c.Y + 112 * S;
            var dim = Color.FromArgb(255, 34, 34, 36);
            Pomodoro.Phase a0 = others[0], a2 = others[1];
            PillBtn(ui, "ph0", x, py, glyphs[(int)a0], nameOf(a0), dim, f.White, () => Pomodoro.Select(a0), f);
            x += w0 + gap;
            PillBtn(ui, "ptoggle", x, py, Pomodoro.Running ? "" : "", null, tint, Brushes.White, Pomodoro.Toggle, f);
            x += w1 + gap;
            PillBtn(ui, "ph2", x, py, glyphs[(int)a2], nameOf(a2), dim, f.White, () => Pomodoro.Select(a2), f);

            // what this session is for (click to step through your reminders)
            var focus = Pomodoro.Focus;
            string ft = focus != null ? focus.Text : (Reminders.Open().Count > 0 ? "Pick a reminder to focus on" : null);
            if (ft != null)
            {
                var fr = new RectangleF(rx + 20 * S, c.Y + 146 * S, rw - 40 * S, 26 * S);
                float fh = Hot("pfocus");
                if (fh > 0.01f) ui.Fill(fr, Color.FromArgb((int)(22 * fh), 255, 255, 255), 13 * S);
                using (var tf = new Font(Fonts.Icons, 11f * S, GraphicsUnit.Pixel))
                {
                    float fw = Math.Min(fr.Width - 30 * S, g.MeasureString(ft, f.Small).Width + 4 * S);
                    float fx = fr.X + (fr.Width - fw - 20 * S) / 2;
                    ui.Glyph("", new RectangleF(fx, fr.Y, 16 * S, fr.Height), tf, focus != null ? f.White : f.Faint);
                    ui.Text(ft, new RectangleF(fx + 20 * S, fr.Y, fw, fr.Height), f.Small, focus != null ? f.White : f.Faint);
                }
                ui.AddHit("pfocus", fr, Pomodoro.NextFocus, true);
            }
        }

        // ------------------------------------------------------------------ reminders

        void RemindersTab(UI ui, RectangleF c, Fonts2 f)
        {
            var g = ui.G;
            var open = Reminders.Open();
            var finished = Reminders.All.FindAll(i => i.Done && i.Finished.Date == DateTime.Today);
            int doneToday = finished.Count, total = doneToday + open.Count;

            // left: how the day is going, and a new one
            float lw = 236 * S;
            using (var huge = new Font(Fonts.UiBold, 40f * S, GraphicsUnit.Pixel))
                ui.Text(total == 0 ? "0" : doneToday + "/" + total, new RectangleF(c.X, c.Y - 2 * S, lw, 50 * S), huge, f.White);
            ui.Text(total == 0 ? "Nothing on your list" : open.Count == 0 ? "All done today" : "Done today", new RectangleF(c.X + 2 * S, c.Y + 46 * S, lw, 18 * S), f.Body, f.Dim);
            ui.Pad = c.X - 8 * S;
            ui.Right = c.X + lw + 8 * S;
            ui.Y = c.Y + 74 * S;
            ui.TextField("rem", "Add a reminder…", false, AddReminder);
            float py = c.Y + 140 * S;
            var dim = Color.FromArgb(255, 34, 34, 36);
            bool can = Field("rem").Trim() != "";
            var addR = PillBtn(ui, "radd", c.X, py, "", "Add", can ? Color.FromArgb(255, 48, 160, 80) : dim, can ? Brushes.White : (Brush)f.Dim, AddReminder, f);
            // when it's due: one pill that steps through the choices
            PillBtn(ui, "rdue", addR.Right + 8 * S, py, "", dueNames[dueChoice], dim, f.White, () => dueChoice = (dueChoice + 1) % dueNames.Length, f);

            // right: open ones first, then what you finished today (struck through, in green)
            float rx = c.X + lw + 26 * S, rw = c.Right - rx;
            if (total == 0) { ui.Text("Add something on the left and tick it off here", new RectangleF(rx, c.Y + 76 * S, rw, 20 * S), f.Body, f.Faint, 1); return; }
            float y = c.Y;
            var rows = new List<Reminders.Item>(open);
            rows.AddRange(finished);
            foreach (var it in rows)
            {
                if (y + 34 * S > c.Bottom + 2 * S) break;
                var row = new RectangleF(rx, y, rw, 34 * S);
                string id = "rr" + it.Id;
                bool hot = Hover == id || Hover == "rt" + it.Id || Hover == "rx" + it.Id;
                float h = Hot(id) + Hot("rt" + it.Id) + Hot("rx" + it.Id);
                ui.Fill(row, it.Done ? Color.FromArgb(255, 18, 52, 30) : Color.FromArgb((int)(30 + 18 * Math.Min(1, h)), 255, 255, 255), 11 * S);
                ui.AddHit(id, row, null);
                var tick = new RectangleF(row.X + 10 * S, y + 8 * S, 18 * S, 18 * S);
                TickCircle(ui, tick, it.Done, Hover == "rt" + it.Id, it.Id);
                var item = it;
                ui.AddHit("rt" + it.Id, RectangleF.Inflate(tick, 5 * S, 5 * S), () => Reminders.Toggle(item), true);
                var tr = new RectangleF(tick.Right + 10 * S, y, rw - 140 * S, 34 * S);
                using (var tf = new Font(Fonts.Ui, 13.5f * S, it.Done ? FontStyle.Strikeout : FontStyle.Regular, GraphicsUnit.Pixel))
                    ui.Text(it.Text, tr, tf, it.Done ? f.Dim : f.White);
                string due = it.Done ? null : Reminders.DueText(it);
                if (due != null)
                    using (var db = new SolidBrush(Reminders.Overdue(it) ? Color.FromArgb(255, 255, 105, 97) : NowPlayingView.Dim))
                        ui.Text(due, new RectangleF(rx, y, rw - (hot ? 34 : 12) * S, 34 * S), f.Small, db, 2);
                if (hot)
                {
                    var x = new RectangleF(row.Right - 28 * S, y + 7 * S, 20 * S, 20 * S);
                    using (var gf = new Font(Fonts.Icons, 9f * S, GraphicsUnit.Pixel)) ui.Glyph("", x, gf, Hover == "rx" + it.Id ? f.White : f.Dim);
                    ui.AddHit("rx" + it.Id, x, () => Reminders.Remove(item), true);
                }
                y += 38 * S;
            }
        }
    }
}
