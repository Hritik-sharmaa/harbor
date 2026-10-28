using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace Harbor
{
    /// <summary>The island's Clipboard, Shelf and Claude Usage tabs, plus dragging things in and out of it.</summary>
    sealed partial class Island
    {
        // ------------------------------------------------------------------ dragging out (clipboard images, shelf files)

        readonly Dictionary<string, Action> drags = new Dictionary<string, Action>(); // hit id → starts its drag
        string dragKey;
        Point dragFrom;
        bool dragOut;   // our own drag is running: stay open, and don't take it back as a drop

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (dragKey != null && e.Button == MouseButtons.Left)
            {
                var d = SystemInformation.DragSize;
                if (Math.Abs(e.X - dragFrom.X) > d.Width || Math.Abs(e.Y - dragFrom.Y) > d.Height)
                {
                    Action start;
                    drags.TryGetValue(dragKey, out start);
                    dragKey = null;
                    Pressed = null;
                    if (start != null)
                    {
                        dragOut = HoldOpen = true;
                        try { start(); }
                        catch (Exception ex) { App.Log(ex); }
                        finally { dragOut = HoldOpen = false; }
                        Shelf.Prune(); // a shelf file dropped on a folder may have moved
                        Refresh2();
                        return;
                    }
                }
            }
            base.OnMouseMove(e);
        }

        void StartDragFrom(MouseEventArgs e)
        {
            dragKey = e.Button == MouseButtons.Left && Pressed != null && drags.ContainsKey(Pressed) ? Pressed : null;
            dragFrom = e.Location;
        }

        // ------------------------------------------------------------------ dropping files in (the shelf)

        bool dropHover;

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);

        /// <summary>Files are still being dragged toward us (opened from the menu bar, or over the shelf).</summary>
        bool StillDropping()
        {
            if (!dropHover) return false;
            if ((GetAsyncKeyState(1) & 0x8000) != 0 || (GetAsyncKeyState(2) & 0x8000) != 0) return true;
            dropHover = false; // the drag ended somewhere else
            return false;
        }

        /// <summary>Files dragged onto the centre of the menu bar: open on the shelf, ready for the drop.</summary>
        public static void OpenForDrop(TopBar bar)
        {
            var sb = bar.ScreenBounds;
            Open(bar, new RectangleF(sb.Width / 2f - 160 * bar.Disp.Scale, 0, 320 * bar.Disp.Scale, sb.Height), "shelf");
            inst.dropHover = true;
            inst.Refresh2();
        }

        static bool HasFiles(DragEventArgs e) { return e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop); }

        protected override void OnDragEnter(DragEventArgs e) { base.OnDragEnter(e); DragOverShelf(e); }

        protected override void OnDragOver(DragEventArgs e) { base.OnDragOver(e); DragOverShelf(e); }

        void DragOverShelf(DragEventArgs e)
        {
            if (dragOut || !HasFiles(e)) { e.Effect = DragDropEffects.None; return; }
            e.Effect = (e.AllowedEffect & DragDropEffects.Copy) != 0 ? DragDropEffects.Copy : DragDropEffects.Link;
            outsideMs = 0;
            if (tab != "shelf") SetTab("shelf", false);
            if (!dropHover) { dropHover = true; Refresh2(); }
        }

        protected override void OnDragLeave(EventArgs e)
        {
            base.OnDragLeave(e);
            if (dropHover) { dropHover = false; Refresh2(); }
        }

        protected override void OnDragDrop(DragEventArgs e)
        {
            base.OnDragDrop(e);
            dropHover = false;
            if (!dragOut && HasFiles(e))
            {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null) { Shelf.Add(files); shelfRow = 0; }
            }
            Refresh2();
        }

        // ------------------------------------------------------------------ wheel: page through clips and shelf rows

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            int step = e.Delta > 0 ? -1 : 1;
            if (tab == "clipboard") { clipFirst = Math.Max(0, clipFirst + step); Refresh2(); return; }
            if (tab == "shelf") { shelfRow = Math.Max(0, shelfRow + step); Refresh2(); return; }
            base.OnMouseWheel(e);
        }

        // ------------------------------------------------------------------ small shared bits

        Bitmap FileIcon(string path, float size)
        {
            string key = "file:" + (int)size + ":" + path;
            Bitmap b;
            if (icons.TryGetValue(key, out b)) return b;
            try { var arr = ShellIcons.LoadIcons(path, (int)Math.Round(size)); b = arr != null ? arr[0] : null; }
            catch { b = null; }
            icons[key] = b;
            return b;
        }

        /// <summary>A round icon button in a tab's header (paging arrows, clear).</summary>
        void HeaderButton(UI ui, string id, RectangleF r, string glyph, bool enabled, Action act, Fonts2 f)
        {
            if (enabled && Hover == id) ui.Fill(r, Color.FromArgb(40, 255, 255, 255), r.Height / 2);
            using (var gf = new Font(Fonts.Icons, 10f * S, GraphicsUnit.Pixel)) ui.Glyph(glyph, r, gf, enabled ? (Hover == id ? f.White : f.Dim) : f.Faint);
            if (enabled) ui.AddHit(id, r, act, true);
        }

        /// <summary>Right-aligned chips (filters); returns their left edge.</summary>
        float Chips(UI ui, float right, float y, string[] names, int selected, string idPrefix, Action<int> pick, Fonts2 f)
        {
            float x = right;
            using (var dark = new SolidBrush(Color.FromArgb(235, 18, 18, 22)))
                for (int i = names.Length - 1; i >= 0; i--)
                {
                    float w = PillWidth(ui, names[i], f.Small);
                    x -= w;
                    var r = new RectangleF(x, y, w, 22 * S);
                    string id = idPrefix + i;
                    bool sel = i == selected;
                    Pill(ui, r, names[i], sel ? Color.FromArgb(235, 255, 255, 255) : Color.FromArgb(Hover == id ? 54 : 26, 255, 255, 255), f.Small, sel ? (Brush)dark : f.Dim);
                    int k = i;
                    ui.AddHit(id, r, () => pick(k), true);
                    x -= 6 * S;
                }
            return x;
        }

        static string Clean(string s, int max)
        {
            if (s.Length > max) s = s.Substring(0, max);
            return s.Replace("\r\n", "\n").Replace('\t', ' ');
        }

        // ------------------------------------------------------------------ media: where the sound plays

        List<AudioOutputs.Device> outputs;
        int outputsAt;

        /// <summary>A small pill with the sound output (speakers, headphones...); click it to switch to the next one.</summary>
        void OutputPill(UI ui, float x, float y, float right, Fonts2 f)
        {
            // listed at most every few seconds while the Media tab is drawn (it redraws often while music plays)
            if (outputs == null || unchecked(Environment.TickCount - outputsAt) > 5000) { outputs = AudioOutputs.List(); outputsAt = Environment.TickCount; }
            var cur = outputs.Find(d => d.Default);
            if (cur == null || outputs.Count < 2) return; // nothing to switch to
            float w = Math.Min(right - x, PillWidth(ui, cur.Name, f.Small) + 18 * S);
            if (w < 60 * S) return;
            var r = new RectangleF(x, y, w, 20 * S);
            ui.Fill(r, Color.FromArgb(Hover == "mout" ? 54 : 26, 255, 255, 255), r.Height / 2);
            using (var gf = new Font(Fonts.Icons, 9.5f * S, GraphicsUnit.Pixel)) ui.Glyph("", new RectangleF(r.X + 4 * S, r.Y, 18 * S, r.Height), gf, f.Dim);
            using (var sf = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                ui.G.DrawString(cur.Name, f.Small, Hover == "mout" ? f.White : f.Dim, new RectangleF(r.X + 22 * S, r.Y, r.Width - 28 * S, r.Height), sf);
            ui.AddHit("mout", r, () => { AudioOutputs.Next(); outputs = null; }, true);
        }

        // ------------------------------------------------------------------ clipboard

        int clipFirst, clipFilter;
        ClipboardHistory.Item copied;
        Timer copiedTimer;
        static readonly string[] clipFilters = { "All", "Text", "Images" };

        void ClipboardTab(UI ui, RectangleF c, Fonts2 f)
        {
            ui.Text("Clipboard", new RectangleF(c.X, c.Y - 2 * S, 200 * S, 22 * S), f.Title, f.White);
            if (!ClipboardHistory.On)
            {
                var hint = new RectangleF(c.X, c.Y + 70 * S, c.Width, 40 * S);
                ui.Text("Clipboard history is off. Turn it on in Harbor → General → Island.", hint, f.Body, Hover == "clipset" ? f.White : f.Faint, 1);
                ui.AddHit("clipset", hint, () => { CloseFlyout(); SettingsWindow.ShowWindow(); });
                return;
            }
            var list = ClipboardHistory.Items.FindAll(i => clipFilter == 0 || (clipFilter == 1 ? i.Kind == "text" : i.Kind == "image"));

            // header: count, paging, filters, clear
            float tx = c.X + ui.G.MeasureString("Clipboard", f.Title).Width + 10 * S;
            ui.Text(list.Count == 0 ? "" : list.Count + (list.Count == 1 ? " item" : " items"), new RectangleF(tx, c.Y, 120 * S, 18 * S), f.Small, f.Faint);
            float left = Chips(ui, c.Right - 34 * S, c.Y - 2 * S, clipFilters, clipFilter, "clipf", i => { clipFilter = i; clipFirst = 0; }, f);
            if (ClipboardHistory.Items.Count > 0)
                HeaderButton(ui, "clipclear", new RectangleF(c.Right - 26 * S, c.Y - 3 * S, 24 * S, 24 * S), "", true, ClipboardHistory.Clear, f);

            float top = c.Y + 32 * S, gap = 10 * S, h = c.Bottom - top;
            int fit = 4;
            float w = (c.Width - (fit - 1) * gap) / fit;
            if (list.Count == 0)
            {
                using (var gf = new Font(Fonts.Icons, 26f * S, GraphicsUnit.Pixel)) ui.Glyph("", new RectangleF(c.X, top + 18 * S, c.Width, 40 * S), gf, f.Faint);
                ui.Text(clipFilter == 0 ? "Copy something and it shows up here. Screenshots too." : "Nothing here yet", new RectangleF(c.X, top + 66 * S, c.Width, 22 * S), f.Body, f.Dim, 1);
                ui.Text("Click to copy again · drag images out · kept until Harbor quits", new RectangleF(c.X, top + 90 * S, c.Width, 20 * S), f.Small, f.Faint, 1);
                return;
            }
            clipFirst = Math.Max(0, Math.Min(clipFirst, list.Count - fit));
            if (list.Count > fit)
            {
                float px = left - 64 * S;
                HeaderButton(ui, "clipprev", new RectangleF(px, c.Y - 3 * S, 24 * S, 24 * S), "", clipFirst > 0, () => clipFirst = Math.Max(0, clipFirst - fit), f);
                HeaderButton(ui, "clipnext", new RectangleF(px + 28 * S, c.Y - 3 * S, 24 * S, 24 * S), "", clipFirst + fit < list.Count, () => clipFirst += fit, f);
            }
            for (int k = 0; k < fit && clipFirst + k < list.Count; k++)
                ClipCard(ui, new RectangleF(c.X + k * (w + gap), top, w, h), list[clipFirst + k], clipFirst + k, f);
        }

        void ClipCard(UI ui, RectangleF r, ClipboardHistory.Item it, int n, Fonts2 f)
        {
            string id = "clip" + n, xid = "clipx" + n;
            bool hot = Hover == id || Hover == xid;
            GlassShape(ui, r, 14 * S, hot ? 30 : 14, hot ? 64 : 26);
            float ix = r.X + 11 * S, iw = r.Width - 22 * S;

            // who it came from, and when
            ui.Text(it.Source ?? (it.Kind == "image" ? "Image" : it.Kind == "files" ? "Files" : "Text"), new RectangleF(ix, r.Y + 8 * S, iw - 40 * S, 18 * S), f.Small, f.Dim);
            if (!hot) ui.Text(Ago(it.At), new RectangleF(ix, r.Y + 8 * S, iw, 18 * S), f.Small, f.Faint, 2);
            var body = new RectangleF(ix, r.Y + 32 * S, iw, r.Bottom - 10 * S - (r.Y + 32 * S));

            ui.AddHit(id, r, () => { ClipboardHistory.CopyBack(it); FlashCopied(it); clipFirst = 0; }, true);
            if (it.Kind == "image")
            {
                if (it.Thumb != null)
                {
                    var area = new RectangleF(body.X, body.Y, body.Width, body.Height - 20 * S);
                    float k = Math.Min(area.Width / it.Thumb.Width, area.Height / it.Thumb.Height);
                    var ir = new RectangleF(area.X + (area.Width - it.Thumb.Width * k) / 2, area.Y + (area.Height - it.Thumb.Height * k) / 2, it.Thumb.Width * k, it.Thumb.Height * k);
                    var g = ui.G;
                    var st = g.Save();
                    using (var clip = Gfx.RoundRect(ir, 8 * S)) g.SetClip(clip, CombineMode.Intersect);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(it.Thumb, ir);
                    g.Restore(st);
                }
                ui.Text((it.Screenshot ? "Screenshot · " : "") + it.ImageSize.Width + " × " + it.ImageSize.Height, new RectangleF(body.X, body.Bottom - 18 * S, body.Width, 18 * S), f.Small, f.Faint, 1);
                if (ClipboardHistory.DragPath(it) != null) drags[id] = () => ShellDrag.Drag(Handle, ClipboardHistory.DragPath(it));
            }
            else if (it.Kind == "files")
            {
                var paths = it.Text.Split('\n');
                var ic = FileIcon(paths[0], 40 * S);
                if (ic != null) ui.G.DrawImage(ic, new RectangleF(body.X + (body.Width - 40 * S) / 2, body.Y + 8 * S, 40 * S, 40 * S));
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter })
                    ui.G.DrawString(Path.GetFileName(paths[0].TrimEnd('\\')), f.Small, f.White, new RectangleF(body.X, body.Y + 56 * S, body.Width, 34 * S), sf);
                if (paths.Length > 1) ui.Text("+" + (paths.Length - 1) + " more", new RectangleF(body.X, body.Bottom - 18 * S, body.Width, 18 * S), f.Small, f.Faint, 1);
                if (ClipboardHistory.DragPath(it) != null) drags[id] = () => ShellDrag.Drag(Handle, ClipboardHistory.DragPath(it));
            }
            else
            {
                string t = Clean(it.Text.Trim(), 400);
                bool link = t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || t.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
                using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter })
                using (var lb = new SolidBrush(Color.FromArgb(255, 100, 180, 255)))
                    ui.G.DrawString(t, f.Body, link ? lb : f.White, body, sf);
                string text = it.Text;
                drags[id] = () => DoDragDrop(text, DragDropEffects.Copy);
            }

            // just copied: say so
            if (copied == it)
            {
                var pill = new RectangleF(r.X + (r.Width - 78 * S) / 2, r.Y + (r.Height - 26 * S) / 2, 78 * S, 26 * S);
                using (var dark = new SolidBrush(Color.FromArgb(240, 18, 18, 22)))
                    Pill(ui, pill, "Copied", Color.FromArgb(240, 255, 255, 255), f.Bold, dark);
            }
            if (hot)
            {
                var x = new RectangleF(r.Right - 30 * S, r.Y + 6 * S, 22 * S, 22 * S);
                ui.Fill(x, Color.FromArgb(Hover == xid ? 70 : 34, 255, 255, 255), 11 * S);
                using (var gf = new Font(Fonts.Icons, 8f * S, GraphicsUnit.Pixel)) ui.Glyph("", x, gf, f.White);
                ui.AddHit(xid, x, () => ClipboardHistory.Remove(it), true);
            }
        }

        void FlashCopied(ClipboardHistory.Item it)
        {
            copied = it;
            if (copiedTimer == null)
            {
                copiedTimer = new Timer { Interval = 1100 };
                copiedTimer.Tick += delegate { copiedTimer.Stop(); copied = null; Refresh2(); };
            }
            copiedTimer.Stop();
            copiedTimer.Start();
        }

        // ------------------------------------------------------------------ shelf

        int shelfRow;

        void ShelfTab(UI ui, RectangleF c, Fonts2 f)
        {
            var list = Shelf.Items;
            ui.Text("Shelf", new RectangleF(c.X, c.Y - 2 * S, 200 * S, 22 * S), f.Title, f.White);
            float tx = c.X + ui.G.MeasureString("Shelf", f.Title).Width + 10 * S;
            ui.Text(list.Count == 0 ? "" : list.Count + (list.Count == 1 ? " file" : " files"), new RectangleF(tx, c.Y, 120 * S, 18 * S), f.Small, f.Faint);

            var area = new RectangleF(c.X, c.Y + 32 * S, c.Width, c.Bottom - c.Y - 32 * S);
            if (list.Count == 0)
            {
                DropZone(ui, area, f, dropHover);
                return;
            }
            {
                var clear = new RectangleF(c.Right - 70 * S, c.Y - 2 * S, 70 * S, 22 * S);
                Pill(ui, clear, "Clear all", Color.FromArgb(Hover == "shclear" ? 54 : 26, 255, 255, 255), f.Small, Hover == "shclear" ? f.White : f.Dim);
                ui.AddHit("shclear", clear, Shelf.Clear, true);
            }

            const int cols = 7;
            float gap = 6 * S, tw = (area.Width - (cols - 1) * gap) / cols, th = (area.Height - gap) / 2;
            int rows = (list.Count + cols - 1) / cols;
            shelfRow = Math.Max(0, Math.Min(shelfRow, rows - 2));
            if (rows > 2)
            {
                float px = c.Right - 70 * S - 64 * S;
                HeaderButton(ui, "shup", new RectangleF(px, c.Y - 3 * S, 24 * S, 24 * S), "", shelfRow > 0, () => shelfRow--, f);
                HeaderButton(ui, "shdown", new RectangleF(px + 28 * S, c.Y - 3 * S, 24 * S, 24 * S), "", shelfRow + 2 < rows, () => shelfRow++, f);
            }
            float isz = 40 * S;
            for (int i = shelfRow * cols, k = 0; i < list.Count && k < cols * 2; i++, k++)
            {
                string p = list[i];
                var r = new RectangleF(area.X + (k % cols) * (tw + gap), area.Y + (k / cols) * (th + gap), tw, th);
                string id = "sh" + i, xid = "shx" + i;
                bool hot = Hover == id || Hover == xid;
                if (hot) GlassShape(ui, r, 12 * S, 26, 50);
                var ic = FileIcon(p, isz);
                if (ic != null) ui.G.DrawImage(ic, new RectangleF(r.X + (tw - isz) / 2, r.Y + 8 * S, isz, isz));
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter })
                    ui.G.DrawString(Path.GetFileName(p.TrimEnd('\\')), f.Small, f.White, new RectangleF(r.X + 3 * S, r.Y + isz + 12 * S, tw - 6 * S, th - isz - 14 * S), sf);
                ui.AddHit(id, r, () => { if (LastButton == MouseButtons.Right) Launcher.Reveal(p); else Launcher.Open(p); }, true);
                drags[id] = () => ShellDrag.Drag(Handle, p);
                if (hot)
                {
                    var x = new RectangleF(r.Right - 22 * S, r.Y + 4 * S, 18 * S, 18 * S);
                    ui.Fill(x, Color.FromArgb(Hover == xid ? 80 : 40, 255, 255, 255), 9 * S);
                    using (var gf = new Font(Fonts.Icons, 7f * S, GraphicsUnit.Pixel)) ui.Glyph("", x, gf, f.White);
                    ui.AddHit(xid, x, () => Shelf.Remove(p), true);
                }
            }
            // files being dragged over a full shelf: light up the whole area
            if (dropHover)
                using (var pen = new Pen(Color.FromArgb(200, 255, 255, 255), 1.6f * S) { DashPattern = new[] { 4f, 3f } })
                using (var path = Gfx.RoundRect(RectangleF.Inflate(area, 2 * S, 2 * S), 14 * S))
                    ui.G.DrawPath(pen, path);
        }

        void DropZone(UI ui, RectangleF area, Fonts2 f, bool hot)
        {
            ui.Fill(area, Color.FromArgb(hot ? 30 : 10, 255, 255, 255), 16 * S);
            using (var pen = new Pen(Color.FromArgb(hot ? 210 : 70, 255, 255, 255), 1.4f * S) { DashPattern = new[] { 5f, 4f } })
            using (var path = Gfx.RoundRect(area, 16 * S))
                ui.G.DrawPath(pen, path);
            using (var gf = new Font(Fonts.Icons, 26f * S, GraphicsUnit.Pixel)) ui.Glyph("", new RectangleF(area.X, area.Y + 26 * S, area.Width, 40 * S), gf, hot ? f.White : f.Dim);
            ui.Text(hot ? "Drop to keep them here" : "Drop files here to keep them handy", new RectangleF(area.X, area.Y + 74 * S, area.Width, 22 * S), f.Bold, f.White, 1);
            ui.Text("Drag them out again into a browser, chat, mail or folder. The files stay where they are.", new RectangleF(area.X, area.Y + 98 * S, area.Width, 20 * S), f.Small, f.Faint, 1);
        }

        // ------------------------------------------------------------------ Claude usage

        static readonly Color Amber = Color.FromArgb(255, 255, 159, 10), Red = Color.FromArgb(255, 255, 69, 58);

        void UsageTab(UI ui, RectangleF c, Fonts2 f)
        {
            ClaudeUsage.Refresh();
            var now = DateTime.Now;
            float lw = 286 * S;
            ui.Text("Claude usage", new RectangleF(c.X, c.Y - 2 * S, lw, 22 * S), f.Title, f.White);
            if (!ClaudeUsage.Loaded)
            {
                ui.Text("Reading Claude Code's files…", new RectangleF(c.X, c.Y + 80 * S, c.Width, 20 * S), f.Body, f.Faint, 1);
                return;
            }

            // plan limits, as Claude Code last saved them
            float y = c.Y + 32 * S;
            var limits = ClaudeUsage.Limits;
            if (limits.Count == 0)
            {
                using (var sf = new StringFormat())
                    ui.G.DrawString("Your plan limits show here once Claude Code has checked them. Type /usage in Claude Code once to load them.", f.Body, f.Dim, new RectangleF(c.X, y, lw, 80 * S), sf);
            }
            for (int i = 0; i < limits.Count && i < 3; i++)
            {
                var l = limits[i];
                bool reset = l.Resets.HasValue && l.Resets.Value <= now;
                int pct = reset ? 0 : Math.Max(0, Math.Min(100, l.Percent));
                Color col = pct >= 90 ? Red : pct >= 75 ? Amber : Color.FromArgb(235, 255, 255, 255);
                ui.Text(l.Name, new RectangleF(c.X, y, lw - 50 * S, 20 * S), f.Bold, f.White);
                using (var pb = new SolidBrush(col)) ui.Text(reset ? "Reset" : pct + "%", new RectangleF(c.X, y, lw, 20 * S), f.Bold, reset ? f.Dim : (Brush)pb, 2);
                var bar = new RectangleF(c.X, y + 24 * S, lw, 6 * S);
                ui.Fill(bar, Color.FromArgb(34, 255, 255, 255), 3 * S);
                if (pct > 0) ui.Fill(new RectangleF(bar.X, bar.Y, Math.Max(bar.Height, bar.Width * pct / 100f), bar.Height), col, 3 * S);
                string sub = !l.Resets.HasValue ? (pct == 0 ? "Starts with your next message" : "")
                    : reset ? "Reset " + When(l.Resets.Value) + " · run /usage for new numbers" : "Resets " + When(l.Resets.Value);
                ui.Text(sub, new RectangleF(c.X, y + 32 * S, lw, 18 * S), f.Small, f.Faint);
                y += 58 * S;
            }
            if (ClaudeUsage.LimitsAt.HasValue)
                {
                var at = ClaudeUsage.LimitsAt.Value;
                string when = at.Date == DateTime.Today ? at.ToString("t", CultureInfo.CurrentCulture)
                    : at.Date == DateTime.Today.AddDays(-1) ? "yesterday " + at.ToString("t", CultureInfo.CurrentCulture) : at.ToString("d MMM", CultureInfo.CurrentCulture);
                ui.Text(ClaudeUsage.LimitsFrom + " · updated " + when, new RectangleF(c.X, c.Bottom - 18 * S, lw, 18 * S), f.Small, f.Faint);
            }

            // the last 7 days on this PC, from the transcripts
            float rx = c.X + lw + 30 * S, rw = c.Right - rx;
            var st = ClaudeUsage.Week;
            ui.Text("Last 7 days on this PC", new RectangleF(rx, c.Y, rw, 18 * S), f.Bold, f.White);
            int show = 6;
            for (int d = 0; d < 7; d++) if (Hover == "ud" + d) show = d;
            DateTime day = DateTime.Today.AddDays(show - 6);
            string head = (show == 6 ? "Today" : day.ToString("ddd d MMM", CultureInfo.CurrentCulture)) + "  " + ClaudeHistory.Tokens(st.Week[show]) + " tokens · " + st.Replies[show] + " replies";
            ui.Text(head, new RectangleF(rx, c.Y, rw, 18 * S), f.Small, show == 6 ? f.Dim : f.White, 2);

            float chartTop = c.Y + 30 * S, chartH = 92 * S, slot = rw / 7, bw = Math.Min(30 * S, slot * 0.56f);
            long max = 1;
            foreach (var v in st.Week) max = Math.Max(max, v);
            for (int d = 0; d < 7; d++)
            {
                string id = "ud" + d;
                float h = st.Week[d] <= 0 ? 0 : Math.Max(4 * S, chartH * st.Week[d] / max);
                var col = new RectangleF(rx + d * slot, chartTop, slot, chartH + 20 * S);
                var b = new RectangleF(col.X + (slot - bw) / 2, chartTop + chartH - h, bw, h);
                ui.Fill(new RectangleF(b.X, chartTop, bw, chartH), Color.FromArgb(Hover == id ? 22 : 10, 255, 255, 255), 6 * S);
                if (h > 0) ui.Fill(b, d == 6 || Hover == id ? Color.FromArgb(240, 255, 255, 255) : Color.FromArgb(110, 255, 255, 255), Math.Min(6 * S, h / 2));
                ui.Text(DateTime.Today.AddDays(d - 6).ToString("ddd", CultureInfo.CurrentCulture).Substring(0, 2), new RectangleF(col.X, chartTop + chartH + 3 * S, slot, 16 * S), f.Small, d == 6 ? f.White : f.Faint, 1);
                ui.AddHit(id, col, null);
            }

            // where it went: last 5 hours, editors, models
            float py = chartTop + chartH + 26 * S, px = rx;
            long week = 0;
            foreach (var v in st.Week) week += v;
            PillRow(ui, ref px, py, rx + rw, "Last 5 h  " + ClaudeHistory.Tokens(st.Last5h), Color.FromArgb(46, 255, 255, 255), f.White, f);
            foreach (var kv in st.Sources) PillRow(ui, ref px, py, rx + rw, kv.Key + "  " + Share(kv.Value, week), Color.FromArgb(26, 255, 255, 255), f.Dim, f);
            px = rx;
            py += 26 * S;
            foreach (var kv in st.Models) PillRow(ui, ref px, py, rx + rw, kv.Key + "  " + Share(kv.Value, week), Color.FromArgb(26, 255, 255, 255), f.Dim, f);
        }

        void PillRow(UI ui, ref float x, float y, float right, string text, Color fill, Brush b, Fonts2 f)
        {
            float w = PillWidth(ui, text, f.Small);
            if (x + w > right) return;
            Pill(ui, new RectangleF(x, y, w, 20 * S), text, fill, f.Small, b);
            x += w + 6 * S;
        }

        static string Share(long part, long total) { return total <= 0 ? "0%" : Math.Max(1, (int)Math.Round(100.0 * part / total)) + "%"; }

        /// <summary>"in 2h 14m", or "Thu 12:29 AM" when it's more than a day away (and "at 3:19 PM" in the past).</summary>
        static string When(DateTime t)
        {
            var d = t - DateTime.Now;
            string clock = t.ToString("t", CultureInfo.CurrentCulture);
            if (d.TotalSeconds <= 0) return t.Date == DateTime.Today ? "at " + clock : t.ToString("ddd", CultureInfo.CurrentCulture) + " " + clock;
            if (d.TotalHours < 24) return "in " + Span(d);
            return "in " + (int)Math.Round(d.TotalDays) + "d · " + t.ToString("ddd", CultureInfo.CurrentCulture) + " " + clock; // like Claude's own "Resets in 6d"
        }
    }
}
