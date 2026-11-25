using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Harbor
{
    /// <summary>Colour weather icons (sun, moon, clouds, rain, snow, storm, fog), drawn once at 4x and scaled down smooth.</summary>
    static class WeatherIcons
    {
        static readonly Dictionary<string, Bitmap> cache = new Dictionary<string, Bitmap>();

        public static void Draw(Graphics g, RectangleF r, int code, bool day)
        {
            int px = (int)Math.Ceiling(Math.Max(r.Width, r.Height));
            string key = code + "|" + day + "|" + px;
            Bitmap b;
            if (!cache.TryGetValue(key, out b))
            {
                if (cache.Count > 80) { foreach (var v in cache.Values) v.Dispose(); cache.Clear(); }
                using (var big = Render(code, day, px * 4)) b = Gfx.Scale(big, px);
                cache[key] = b;
            }
            g.DrawImage(b, r.X + (r.Width - px) / 2, r.Y + (r.Height - px) / 2, px, px);
        }

        static Bitmap Render(int code, bool day, int s)
        {
            var b = new Bitmap(s, s, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                bool cloudy = code == 2 || code == 3 || code >= 45;
                bool partly = code == 1 || code == 2;
                if (!cloudy || partly)
                {
                    // sun or moon: whole when clear, small in the top-left corner behind a cloud otherwise
                    var c = partly && code == 2 ? new RectangleF(s * 0.06f, s * 0.08f, s * 0.5f, s * 0.5f)
                          : partly ? new RectangleF(s * 0.12f, s * 0.12f, s * 0.6f, s * 0.6f)
                          : new RectangleF(s * 0.2f, s * 0.2f, s * 0.6f, s * 0.6f);
                    if (day) Sun(g, c); else Moon(g, c);
                }
                if (cloudy)
                {
                    bool dark = code == 3 || code >= 61;
                    var cr = code == 2 ? new RectangleF(s * 0.22f, s * 0.36f, s * 0.74f, s * 0.46f) : new RectangleF(s * 0.1f, s * 0.2f, s * 0.82f, s * 0.52f);
                    Cloud(g, cr, dark ? Color.FromArgb(255, 196, 202, 214) : Color.FromArgb(255, 244, 246, 250));
                    float y = cr.Bottom + s * 0.04f;
                    if ((code >= 51 && code <= 67) || (code >= 80 && code <= 82))
                        using (var pen = new Pen(Color.FromArgb(255, 90, 170, 255), s * 0.055f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                            for (int i = 0; i < 3; i++) { float x = cr.X + cr.Width * (0.28f + i * 0.22f); g.DrawLine(pen, x, y, x - s * 0.06f, y + s * 0.16f); }
                    else if ((code >= 71 && code <= 77) || code == 85 || code == 86)
                        using (var br = new SolidBrush(Color.White))
                            for (int i = 0; i < 3; i++) { float x = cr.X + cr.Width * (0.28f + i * 0.22f); g.FillEllipse(br, x - s * 0.04f, y + (i % 2) * s * 0.06f, s * 0.08f, s * 0.08f); }
                    else if (code >= 95)
                        using (var br = new SolidBrush(Color.FromArgb(255, 255, 204, 0)))
                            g.FillPolygon(br, new[] { new PointF(s * 0.52f, y - s * 0.02f), new PointF(s * 0.38f, y + s * 0.13f), new PointF(s * 0.48f, y + s * 0.13f), new PointF(s * 0.42f, y + s * 0.24f), new PointF(s * 0.6f, y + s * 0.07f), new PointF(s * 0.5f, y + s * 0.07f) });
                    else if (code == 45 || code == 48)
                        using (var pen = new Pen(Color.FromArgb(220, 210, 214, 222), s * 0.05f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                            for (int i = 0; i < 2; i++) g.DrawLine(pen, s * (0.2f + i * 0.08f), y + i * s * 0.09f, s * (0.8f - i * 0.04f), y + i * s * 0.09f);
                }
            }
            return b;
        }

        static void Sun(Graphics g, RectangleF r)
        {
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, rad = r.Width * 0.26f;
            using (var pen = new Pen(Color.FromArgb(255, 255, 196, 0), r.Width * 0.07f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4;
                    g.DrawLine(pen, cx + (float)Math.Cos(a) * rad * 1.45f, cy + (float)Math.Sin(a) * rad * 1.45f, cx + (float)Math.Cos(a) * rad * 1.9f, cy + (float)Math.Sin(a) * rad * 1.9f);
                }
            using (var br = new LinearGradientBrush(new RectangleF(cx - rad, cy - rad, rad * 2, rad * 2), Color.FromArgb(255, 255, 220, 60), Color.FromArgb(255, 255, 170, 0), 60f))
                g.FillEllipse(br, cx - rad, cy - rad, rad * 2, rad * 2);
        }

        static void Moon(Graphics g, RectangleF r)
        {
            // a crescent: the moon disc with a second disc cut out of its top-right (drawn at 4x, so the clip edge is smooth once scaled)
            float rad = r.Width * 0.36f, cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            using (var cut = new GraphicsPath())
            {
                cut.AddEllipse(cx - rad * 0.35f, cy - rad * 1.25f, rad * 1.9f, rad * 1.9f);
                var clip = g.Clip;
                g.SetClip(cut, CombineMode.Exclude);
                using (var br = new SolidBrush(Color.FromArgb(255, 238, 236, 225))) g.FillEllipse(br, cx - rad, cy - rad, rad * 2, rad * 2);
                g.Clip = clip;
            }
        }

        static void Cloud(Graphics g, RectangleF r, Color c)
        {
            // three puffs and a flat base, filled one by one with the same gradient: they overlap without seams
            using (var br = new LinearGradientBrush(r, c, Color.FromArgb(c.A, (int)(c.R * 0.86), (int)(c.G * 0.86), (int)(c.B * 0.88)), 90f))
            {
                g.FillEllipse(br, r.X, r.Y + r.Height * 0.35f, r.Width * 0.42f, r.Height * 0.65f);
                g.FillEllipse(br, r.X + r.Width * 0.2f, r.Y, r.Width * 0.5f, r.Height * 0.85f);
                g.FillEllipse(br, r.X + r.Width * 0.5f, r.Y + r.Height * 0.25f, r.Width * 0.5f, r.Height * 0.75f);
                g.FillRectangle(br, r.X + r.Width * 0.2f, r.Y + r.Height * 0.6f, r.Width * 0.62f, r.Height * 0.4f);
            }
        }
    }
}
