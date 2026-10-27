using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Harbor
{
    /// <summary>
    /// The pieces of Now Playing, shared by the media panel and the island so they look and behave the same:
    /// a colour wash from the cover, the cover, a seekable progress bar, controls and karaoke-style lyrics.
    /// One instance per host keeps that host's scroll / seek state.
    /// </summary>
    sealed class NowPlayingView
    {
        public static readonly Color White = Color.FromArgb(250, 255, 255, 255);
        public static readonly Color Dim = Color.FromArgb(165, 255, 255, 255);
        public static readonly Color Faint = Color.FromArgb(110, 255, 255, 255);

        float lyricScroll, lyricTarget;
        bool snap = true;
        float scrub = -1;               // 0..1 while dragging the progress bar
        string lastSong;
        Bitmap ambient, ambientSrc;     // cover art shrunk to a few pixels: stretched, it's a soft colour wash

        /// <summary>True while the lyrics are gliding to the next line (the host should redraw at ~30 fps).</summary>
        public bool Animating { get { return Math.Abs(lyricTarget - lyricScroll) > 0.5f; } }

        /// <summary>Jump straight to the right line next frame (opened, new song, seeked, lyrics arrived).</summary>
        public void Snap() { snap = true; }

        public void NoteSong()
        {
            string song = (Media.Artist ?? "") + "|" + (Media.Title ?? "");
            if (song != lastSong) { lastSong = song; snap = true; }
        }

        /// <summary>Call from the host's OnMouseUp: finishing a drag on the progress bar seeks there.</summary>
        public void MouseUp(Flyout host)
        {
            if (host.Dragging == "seek" && scrub >= 0)
            {
                Media.Seek(TimeSpan.FromTicks((long)(Media.Duration.Ticks * scrub)));
                scrub = -1;
                snap = true;
            }
        }

        public static string Clock(TimeSpan t)
        {
            return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : ((int)t.TotalMinutes) + ":" + t.Seconds.ToString("00");
        }

        /// <summary>The cover art blurred into a colour wash, darkened so white text always reads.</summary>
        public void Backdrop(Graphics g, RectangleF r)
        {
            // a solid base first: the shrunken cover has soft see-through edges that would let the desktop show through
            using (var basis = new SolidBrush(Color.FromArgb(255, 16, 16, 20))) g.FillRectangle(basis, r);
            if (Media.Art != null && Media.Has)
            {
                if (ambientSrc != Media.Art)
                {
                    if (ambient != null) ambient.Dispose();
                    ambient = Gfx.Scale(Media.Art, 6);
                    ambientSrc = Media.Art;
                }
                var mode = g.InterpolationMode;
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                using (var ia = new System.Drawing.Imaging.ImageAttributes())
                {
                    ia.SetWrapMode(WrapMode.TileFlipXY); // no dark seams at the edges
                    g.DrawImage(ambient, Rectangle.Round(RectangleF.Inflate(r, r.Width * 0.15f, r.Height * 0.15f)), 0, 0, ambient.Width, ambient.Height, GraphicsUnit.Pixel, ia);
                }
                g.InterpolationMode = mode;
            }
            else
                using (var br = new LinearGradientBrush(r, Color.FromArgb(255, 70, 60, 130), Color.FromArgb(255, 150, 60, 110), 60f)) g.FillRectangle(br, r);
            using (var shade = new LinearGradientBrush(r, Color.FromArgb(120, 0, 0, 0), Color.FromArgb(175, 0, 0, 0), 90f))
                g.FillRectangle(shade, r);
        }

        public void Cover(UI ui, RectangleF art, float radius)
        {
            float S = ui.S;
            for (int i = 1; i <= 6; i++)
                using (var sp = Gfx.RoundRect(new RectangleF(art.X - i * S, art.Y - i * S + 4 * S, art.Width + 2 * i * S, art.Height + 2 * i * S), radius + i * S))
                using (var sb = new SolidBrush(Color.FromArgb(14, 0, 0, 0)))
                    ui.G.FillPath(sb, sp);
            MediaFlyout.DrawArt(ui, art, radius);
        }

        /// <summary>Progress bar (drag or click to seek) with elapsed / remaining time under it. Needs ~30 px.</summary>
        public void Progress(UI ui, Flyout host, float x, float y, float w, Font small)
        {
            float S = ui.S;
            var bar = new RectangleF(x, y + 4 * S, w, 5 * S);
            float v = scrub >= 0 ? scrub : (float)(Media.Position.TotalSeconds / Media.Duration.TotalSeconds);
            v = Math.Max(0, Math.Min(1, v));
            bool hot = host.Hover == "seek" || host.Dragging == "seek";
            if (hot) bar = RectangleF.Inflate(bar, 0, 1.5f * S);
            ui.Fill(bar, Color.FromArgb(55, 255, 255, 255), bar.Height / 2);
            ui.Fill(new RectangleF(bar.X, bar.Y, Math.Max(bar.Height, bar.Width * v), bar.Height), Color.FromArgb(hot ? 255 : 225, 255, 255, 255), bar.Height / 2);
            var hit = ui.AddHit("seek", new RectangleF(bar.X - 6 * S, bar.Y - 9 * S, bar.Width + 12 * S, bar.Height + 18 * S), null);
            hit.Track = new RectangleF(bar.X, bar.Y, bar.Width, 0);
            hit.Slide = f => scrub = f;
            var at = TimeSpan.FromTicks((long)(Media.Duration.Ticks * v));
            using (var fb = new SolidBrush(Faint))
            {
                ui.Text(Clock(at), new RectangleF(x, y + 14 * S, 80 * S, 16 * S), small, fb);
                ui.Text("-" + Clock(Media.Duration - at), new RectangleF(x + w - 80 * S, y + 14 * S, 80 * S, 16 * S), small, fb, 2);
            }
        }

        /// <summary>Previous / play-pause / next around (cx, cy). 'size' scales the whole group (1 = 52 px play button).</summary>
        public void Controls(UI ui, Flyout host, float cx, float cy, float size = 1)
        {
            float S = ui.S * size;
            var play = new RectangleF(cx - 26 * S, cy - 26 * S, 52 * S, 52 * S);
            using (var pb = new SolidBrush(Color.FromArgb(host.Hover == "play" ? 255 : 240, 255, 255, 255))) ui.G.FillEllipse(pb, play);
            using (var gf = new Font(Fonts.Icons, 20f * S, GraphicsUnit.Pixel))
            using (var gb = new SolidBrush(Color.FromArgb(235, 18, 18, 22)))
                ui.Glyph(Media.Playing ? "" : "", new RectangleF(play.X + (Media.Playing ? 0 : 1.5f * S), play.Y, play.Width, play.Height), gf, gb);
            ui.AddHit("play", play, Media.PlayPause, true);
            using (var gf = new Font(Fonts.Icons, 17f * S, GraphicsUnit.Pixel))
            using (var wb = new SolidBrush(White))
            {
                RoundButton(ui, host, "prev", new PointF(cx - 74 * S, cy), 21 * S, "", Media.Previous, gf, wb);
                RoundButton(ui, host, "next", new PointF(cx + 74 * S, cy), 21 * S, "", Media.Next, gf, wb);
            }
        }

        static void RoundButton(UI ui, Flyout host, string id, PointF c, float radius, string glyph, Action act, Font f, Brush b)
        {
            var r = new RectangleF(c.X - radius, c.Y - radius, radius * 2, radius * 2);
            if (host.Hover == id) using (var hb = new SolidBrush(Color.FromArgb(40, 255, 255, 255))) ui.G.FillEllipse(hb, r);
            ui.Glyph(glyph, r, f, b);
            ui.AddHit(id, r, act, true);
        }

        /// <summary>
        /// The lyrics area: karaoke lines when synced, otherwise a status line ("Looking for lyrics…", "No lyrics found")
        /// or, for unsynced lyrics, the first lines that fit. Returns false when lyrics are off.
        /// </summary>
        public bool Lyrics(UI ui, Flyout host, RectangleF box, float fontPx)
        {
            Harbor.Lyrics.Result lyr = null;
            var state = Media.Has ? Harbor.Lyrics.For(out lyr) : Harbor.Lyrics.State.Off;
            if (state == Harbor.Lyrics.State.Off) return false;
            float S = ui.S;
            using (var body = new Font(Fonts.Ui, 13.5f * S, GraphicsUnit.Pixel))
            using (var db = new SolidBrush(Dim))
            {
                string note = state == Harbor.Lyrics.State.Loading ? "Looking for lyrics…"
                    : lyr == null || lyr.NotFound ? "No lyrics found for this song"
                    : lyr.Failed ? "Couldn't reach the lyrics service"
                    : lyr.Instrumental ? "♪  Instrumental" : null;
                if (note != null) { ui.Text(note, box, body, db, 1); return true; }
                if (lyr.Synced != null) { Synced(ui, host, box, lyr.Synced, fontPx); return true; }
                using (var f = new Font(Fonts.UiBold, fontPx * 0.85f * S, GraphicsUnit.Pixel))
                using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisWord })
                    ui.G.DrawString(lyr.Plain, f, db, box, sf);
            }
            return true;
        }

        /// <summary>Plain (unsynced) lyrics for this song, or null.</summary>
        public static string PlainLyrics()
        {
            Harbor.Lyrics.Result lyr;
            return Media.Has && Harbor.Lyrics.For(out lyr) == Harbor.Lyrics.State.Ready && lyr != null && lyr.Synced == null ? lyr.Plain : null;
        }

        /// <summary>Karaoke view: the current line bright, neighbours fading out, smooth scroll between lines. Click a line to jump there.</summary>
        void Synced(UI ui, Flyout host, RectangleF box, List<Harbor.Lyrics.Line> lines, float fontPx)
        {
            float S = ui.S;
            var now = Media.Position + TimeSpan.FromMilliseconds(150); // land slightly early, like Apple Music
            int cur = -1;
            for (int i = 0; i < lines.Count; i++) { if (lines[i].At <= now) cur = i; else break; }

            using (var f = new Font(Fonts.UiBold, fontPx * S, GraphicsUnit.Pixel))
            using (var sf = new StringFormat())
            {
                float lineGap = fontPx * 0.6f * S, wrapW = box.Width - 16 * S;
                var tops = new float[lines.Count];
                var heights = new float[lines.Count];
                float yy = 0;
                for (int i = 0; i < lines.Count; i++)
                {
                    string t = string.IsNullOrWhiteSpace(lines[i].Text) ? "♪" : lines[i].Text;
                    heights[i] = ui.G.MeasureString(t, f, (int)wrapW, sf).Height;
                    tops[i] = yy;
                    yy += heights[i] + lineGap;
                }
                int focus = Math.Max(0, cur);
                lyricTarget = tops.Length > 0 ? tops[focus] + heights[focus] / 2 - box.Height * 0.4f : 0;
                if (snap) { lyricScroll = lyricTarget; snap = false; }
                else lyricScroll += (lyricTarget - lyricScroll) * 0.18f;

                var clip = ui.G.Clip;
                ui.G.SetClip(box, CombineMode.Intersect);
                for (int i = 0; i < lines.Count; i++)
                {
                    float top = box.Y + tops[i] - lyricScroll;
                    if (top + heights[i] < box.Y || top > box.Bottom) continue;
                    var r = new RectangleF(box.X + 8 * S, top, wrapW, heights[i]);
                    // brightness: current line full, others fade with distance and near the edges of the box
                    float mid = top + heights[i] / 2, edge = Math.Min(mid - box.Y, box.Bottom - mid) / (box.Height * 0.32f);
                    float fade = Math.Max(0.15f, Math.Min(1, edge));
                    int a = i == cur ? 255 : (i < cur ? 70 : 120);
                    string id = "ly" + i;
                    if (host.Hover == id && i != cur) { ui.Fill(RectangleF.Inflate(r, 6 * S, 2 * S), Color.FromArgb(28, 255, 255, 255), 8 * S); a = Math.Max(a, 190); }
                    using (var b = new SolidBrush(Color.FromArgb((int)(a * fade), 255, 255, 255)))
                        ui.G.DrawString(string.IsNullOrWhiteSpace(lines[i].Text) ? "♪" : lines[i].Text, f, b, r, sf);
                    var at = lines[i].At;
                    ui.AddHit(id, RectangleF.Intersect(r, box), () => { Media.Seek(at); snap = true; }, true);
                }
                ui.G.Clip = clip;
            }
        }

        /// <summary>The Lyrics on/off pill.</summary>
        public void LyricsSwitch(UI ui, Flyout host, RectangleF pill, Font small)
        {
            bool on = Look.Bool("Lyrics");
            ui.Fill(pill, on ? Color.FromArgb(host.Hover == "lyr" ? 255 : 235, 255, 255, 255) : Color.FromArgb(host.Hover == "lyr" ? 70 : 45, 255, 255, 255), pill.Height / 2);
            using (var pb = new SolidBrush(on ? Color.FromArgb(230, 20, 20, 24) : White))
                ui.Text("Lyrics", pill, small, pb, 1);
            ui.AddHit("lyr", pill, () => { Look.Set("Lyrics", !Look.Bool("Lyrics")); Settings.Save(); snap = true; }, true);
        }
    }

    /// <summary>
    /// Now Playing panel (click the song in the menu bar): Apple Music style colour wash from the cover, a seekable
    /// progress bar, big controls, and synced lyrics (from LRCLIB) that follow the song.
    /// </summary>
    sealed class MediaFlyout : Flyout
    {
        static MediaFlyout inst;
        public static void Toggle(TopBar bar, RectangleF zone) { (inst ?? (inst = new MediaFlyout())).ShowBelow(bar, zone, "media", 1); }

        const float Margin = 18, ArtSize = 92, LyricsBoxH = 212;

        readonly NowPlayingView view = new NowPlayingView();
        readonly Timer tick = new Timer { Interval = 33 };
        int lastFrame;

        internal MediaFlyout()
        {
            LogicalWidth = 340;
            Media.Changed += delegate { if (Visible) Refresh2(); };
            Lyrics.Changed += delegate { view.Snap(); if (Visible) Refresh2(); };
            tick.Tick += delegate { Tick(); };
        }

        protected override void OnOpened() { Media.WantTimeline = true; Media.RefreshTimeline(); view.Snap(); tick.Start(); }
        protected override void OnClosedFlyout() { Media.WantTimeline = Island.ShowingMedia; tick.Stop(); }

        /// <summary>Redraws only while something moves: the progress bar a few times a second, lyrics at 30 fps while scrolling.</summary>
        void Tick()
        {
            if (!Visible) { tick.Stop(); return; }
            int now = Environment.TickCount;
            if (view.Animating || (Media.Playing && now - lastFrame > 250) || Dragging != null) { lastFrame = now; Refresh2(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            view.MouseUp(this);
            base.OnMouseUp(e);
        }

        protected override void Build(UI ui)
        {
            view.NoteSong();
            Lyrics.Result lyr = null;
            var state = Media.Has ? Lyrics.For(out lyr) : Lyrics.State.Off;
            bool timeline = Media.Has && Media.Duration > TimeSpan.Zero;
            float W = ui.W, m = Margin * S;
            string plain = NowPlayingView.PlainLyrics();

            // ---- measure first, so the colour wash can cover the whole panel
            float h = Margin + ArtSize + 18 + (timeline ? 34 : 0) + 64;
            float plainH = 0;
            if (plain != null)
                using (var f = new Font(Fonts.UiBold, 14f * S, GraphicsUnit.Pixel))
                    plainH = ui.G.MeasureString(plain, f, (int)(W - 2 * m)).Height / S;
            if (state != Lyrics.State.Off)
                h += 8 + (lyr != null && lyr.Synced != null ? LyricsBoxH : (plain != null ? plainH + 26 : 48)) + 22;
            h += 6;
            view.Backdrop(ui.G, new RectangleF(0, 0, W, h * S));

            float y = Margin * S;
            var art = new RectangleF(m, y, ArtSize * S, ArtSize * S);
            view.Cover(ui, art, 12 * S);

            float tx = art.Right + 14 * S, tw = W - m - tx;
            using (var titleF = new Font(Fonts.UiBold, 16f * S, GraphicsUnit.Pixel))
            using (var bodyF = new Font(Fonts.Ui, 13.5f * S, GraphicsUnit.Pixel))
            using (var smallF = new Font(Fonts.Ui, 11.5f * S, GraphicsUnit.Pixel))
            using (var wb = new SolidBrush(NowPlayingView.White))
            using (var db = new SolidBrush(NowPlayingView.Dim))
            using (var fb = new SolidBrush(NowPlayingView.Faint))
            {
                ui.Text(Media.Has ? Media.Title : "Nothing playing", new RectangleF(tx, art.Y + 10 * S, tw, 22 * S), titleF, wb);
                ui.Text(Media.Has ? Media.Artist : "Play something in Spotify, a browser or any media app", new RectangleF(tx, art.Y + 34 * S, tw, 20 * S), bodyF, db);
                if (Media.Has)
                {
                    ui.Text(Media.AppName, new RectangleF(tx, art.Bottom - 22 * S, tw - 70 * S, 18 * S), smallF, fb);
                    view.LyricsSwitch(ui, this, new RectangleF(W - m - 58 * S, art.Bottom - 24 * S, 58 * S, 22 * S), smallF);
                }
                y = art.Bottom + 18 * S;

                if (timeline) { view.Progress(ui, this, m, y, W - 2 * m, smallF); y += 34 * S; }

                view.Controls(ui, this, W / 2, y + 30 * S);
                y += 64 * S;

                if (state != Lyrics.State.Off)
                {
                    y += 8 * S;
                    if (lyr != null && lyr.Synced != null)
                    {
                        view.Lyrics(ui, this, new RectangleF(m - 4 * S, y, W - 2 * m + 8 * S, LyricsBoxH * S), 17f);
                        y += LyricsBoxH * S;
                    }
                    else if (plain != null)
                    {
                        // unsynced: the whole text, the panel scrolls
                        ui.Text("These lyrics aren't time-synced", new RectangleF(m, y, W - 2 * m, 18 * S), smallF, fb);
                        using (var f = new Font(Fonts.UiBold, 14f * S, GraphicsUnit.Pixel))
                        using (var sf = new StringFormat())
                            ui.G.DrawString(plain, f, db, new RectangleF(m, y + 24 * S, W - 2 * m, plainH * S + 4 * S), sf);
                        y += (plainH + 26) * S;
                    }
                    else
                    {
                        view.Lyrics(ui, this, new RectangleF(m, y, W - 2 * m, 40 * S), 17f);
                        y += 48 * S;
                    }
                    ui.Text("Lyrics from LRCLIB", new RectangleF(m, y + 4 * S, W - 2 * m, 16 * S), smallF, fb, 1);
                    y += 22 * S;
                }
            }
            ui.Y = y - 2 * S; // the flyout adds 8 px below the content: 6 px of padding in all
        }

        public static void DrawArt(UI ui, RectangleF art, float radius)
        {
            if (Media.Art != null)
            {
                using (var p = Gfx.RoundRect(art, radius))
                {
                    var clip = ui.G.Clip;
                    ui.G.SetClip(p, CombineMode.Intersect);
                    ui.G.DrawImage(Media.Art, art);
                    ui.G.Clip = clip;
                }
            }
            else
            {
                using (var br = new LinearGradientBrush(art, Color.FromArgb(255, 255, 94, 98), Color.FromArgb(255, 191, 90, 242), 45f))
                using (var p = Gfx.RoundRect(art, radius)) ui.G.FillPath(br, p);
                using (var f = new Font(Fonts.Icons, art.Height * 0.4f, GraphicsUnit.Pixel))
                    ui.Glyph("", art, f, Brushes.White);
            }
        }
    }
}
