using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Harbor
{
    /// <summary>Shell helpers: high quality icons (with alpha), display names, shortcut targets.</summary>
    static class ShellIcons
    {
        static Guid IID_IShellItem = new Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe");
        static readonly Dictionary<string, string> nameCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static string ParsingName(string path)
        {
            return Environment.ExpandEnvironmentVariables(path);
        }

        static IShellItem Item(string path)
        {
            IShellItem item;
            int hr = N.SHCreateItemFromParsingName(ParsingName(path), IntPtr.Zero, ref IID_IShellItem, out item);
            return hr == 0 ? item : null;
        }

        public static bool Exists(string path)
        {
            try
            {
                var it = Item(path);
                if (it == null) return false;
                Marshal.ReleaseComObject(it);
                return true;
            }
            catch { return false; }
        }

        /// <summary>Returns square premultiplied bitmaps of the item's icon at each size, or null.</summary>
        public static Bitmap[] LoadIcons(string path, params int[] sizes)
        {
            IShellItem item = null;
            try
            {
                item = Item(path);
                if (item == null) return null;
                var f = (IShellItemImageFactory)item;
                int max = 0;
                foreach (int s in sizes) max = Math.Max(max, s);
                int req = max <= 48 ? 64 : (max <= 96 ? 128 : 256);
                IntPtr hbm;
                // SIIGBF_BIGGERSIZEOK (1) | SIIGBF_ICONONLY (4)
                if (f.GetImage(new SIZE(req, req), 0x1 | 0x4, out hbm) != 0 || hbm == IntPtr.Zero) return null;
                try
                {
                    using (var raw = FromHBitmap(hbm))
                    {
                        if (raw == null) return null;
                        var res = new Bitmap[sizes.Length];
                        for (int i = 0; i < sizes.Length; i++) res[i] = Gfx.Scale(raw, sizes[i]);
                        return res;
                    }
                }
                finally { N.DeleteObject(hbm); }
            }
            catch { return null; }
            finally { if (item != null) Marshal.ReleaseComObject(item); }
        }

        static Bitmap FromHBitmap(IntPtr hbm)
        {
            BITMAP bm;
            if (N.GetObject(hbm, Marshal.SizeOf(typeof(BITMAP)), out bm) == 0) return null;
            int w = bm.bmWidth, h = Math.Abs(bm.bmHeight);
            if (w <= 0 || h <= 0) return null;
            var bi = new BITMAPINFO();
            bi.h.biSize = Marshal.SizeOf(typeof(BITMAPINFOHEADER));
            bi.h.biWidth = w;
            bi.h.biHeight = -h; // top-down
            bi.h.biPlanes = 1;
            bi.h.biBitCount = 32;
            byte[] buf = new byte[w * h * 4];
            IntPtr dc = N.GetDC(IntPtr.Zero);
            try { if (N.GetDIBits(dc, hbm, 0, (uint)h, buf, ref bi, 0) == 0) return null; }
            finally { N.ReleaseDC(IntPtr.Zero, dc); }

            bool anyAlpha = false, premul = true;
            for (int i = 0; i < buf.Length; i += 4)
            {
                byte a = buf[i + 3];
                if (a != 0) anyAlpha = true;
                if (buf[i] > a || buf[i + 1] > a || buf[i + 2] > a) premul = false;
            }
            if (!anyAlpha) { for (int i = 3; i < buf.Length; i += 4) buf[i] = 255; premul = true; }

            var bmp = new Bitmap(w, h, premul ? PixelFormat.Format32bppPArgb : PixelFormat.Format32bppArgb);
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, bmp.PixelFormat);
            for (int y = 0; y < h; y++)
                Marshal.Copy(buf, y * w * 4, data.Scan0 + y * data.Stride, w * 4);
            bmp.UnlockBits(data);
            return bmp;
        }

        public static string DisplayName(string path)
        {
            string cached;
            if (nameCache.TryGetValue(path, out cached)) return cached;
            string name = null;
            string p = ParsingName(path);
            try
            {
                if (p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(p))
                {
                    if (Path.GetFileName(p).Equals("explorer.exe", StringComparison.OrdinalIgnoreCase)) name = "File Explorer";
                    else
                    {
                        var vi = FileVersionInfo.GetVersionInfo(p);
                        name = Clean(vi.FileDescription);
                        if (string.IsNullOrEmpty(name) || name.Length > 40) name = Clean(vi.ProductName);
                    }
                    if (string.IsNullOrEmpty(name) || name.Length > 40) name = Path.GetFileNameWithoutExtension(p);
                }
                else if (p.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                    name = Path.GetFileNameWithoutExtension(p);
                else
                {
                    var it = Item(p);
                    if (it != null)
                    {
                        IntPtr s;
                        it.GetDisplayName(0, out s); // SIGDN_NORMALDISPLAY
                        name = Marshal.PtrToStringUni(s);
                        Marshal.FreeCoTaskMem(s);
                        Marshal.ReleaseComObject(it);
                    }
                }
            }
            catch { }
            if (string.IsNullOrEmpty(name)) name = Path.GetFileNameWithoutExtension(p.TrimEnd('\\'));
            if (string.IsNullOrEmpty(name)) name = p;
            nameCache[path] = name;
            return name;
        }

        static string Clean(string s) { return s == null ? null : s.Trim(); }

        public static string ResolveLnk(string lnk)
        {
            object o = null;
            try
            {
                o = new ShellLinkCoClass();
                ((System.Runtime.InteropServices.ComTypes.IPersistFile)o).Load(lnk, 0);
                var sb = new StringBuilder(1024);
                ((IShellLinkW)o).GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
                string t = sb.ToString();
                return t.Length > 0 ? t : null;
            }
            catch { return null; }
            finally { if (o != null) Marshal.ReleaseComObject(o); }
        }

        public static string KnownFolder(string guid)
        {
            try
            {
                Guid g = new Guid(guid);
                IntPtr p;
                if (N.SHGetKnownFolderPath(ref g, 0, IntPtr.Zero, out p) == 0)
                {
                    string s = Marshal.PtrToStringUni(p);
                    Marshal.FreeCoTaskMem(p);
                    return s;
                }
            }
            catch { }
            return null;
        }

        public static string Downloads
        {
            get
            {
                return KnownFolder("374DE290-123F-4565-9164-39C4925E467B")
                    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            }
        }

        public const string RecycleBin = "::{645FF040-5081-101B-9F08-00AA002F954E}";

        public static bool RecycleBinEmpty()
        {
            var info = new SHQUERYRBINFO();
            info.cbSize = Marshal.SizeOf(info);
            try { if (N.SHQueryRecycleBin(null, ref info) == 0) return info.i64NumItems == 0; }
            catch { }
            return true;
        }
    }

    /// <summary>Ref-counted icon cache shared by all docks (one copy per icon, however many monitors).</summary>
    static class IconCache
    {
        sealed class Entry { public Bitmap[] Bmps; public int Refs; }
        static readonly Dictionary<string, Entry> map = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        public static Bitmap[] Acquire(string path, string name, int big, int small, string variant, out string key)
        {
            key = (path ?? ("letter:" + name)) + "|" + variant + "|" + big + "|" + small;
            Entry e;
            if (!map.TryGetValue(key, out e))
            {
                Bitmap[] b;
                bool tiles = variant.Contains("tile");
                bool dark = variant.Contains("dark");
                if (variant.Contains("whitesur"))
                {
                    // style ONCE at high resolution, then scale: the resting and magnified icons always match
                    int hiPx = Math.Max(128, big * 2);
                    Bitmap[] raw = path == null ? null : (IsImage(path) ? LoadImage(path, hiPx, hiPx) : ShellIcons.LoadIcons(path, hiPx));
                    Bitmap src = raw != null ? raw[0] : Gfx.LetterIcon(name, hiPx);
                    if (raw != null) for (int i = 1; i < raw.Length; i++) raw[i].Dispose();
                    string look = null;
                    int lk = variant.IndexOf("look=");
                    if (lk >= 0) look = variant.Substring(lk + 5);
                    using (src)
                    using (var tile = Gfx.MakeWhiteSur(src, look, name))
                        b = new[] { Gfx.Scale(tile, big), Gfx.Scale(tile, small) };
                    for (int i = 0; i < b.Length; i++)
                    {
                        var s = Gfx.WithShadow(b[i]);
                        b[i].Dispose();
                        b[i] = s;
                    }
                    e = new Entry { Bmps = b };
                    map[key] = e;
                    e.Refs++;
                    return e.Bmps;
                }
                b = path == null ? null : (IsImage(path) ? LoadImage(path, big, small) : ShellIcons.LoadIcons(path, big, small));
                if (b == null) b = new[] { Gfx.LetterIcon(name, big), Gfx.LetterIcon(name, small) };
                for (int i = 0; i < b.Length; i++)
                {
                    if (tiles)
                    {
                        var t = Gfx.MakeGlassTile(b[i], dark);
                        b[i].Dispose();
                        b[i] = t;
                    }
                    var s = Gfx.WithShadow(b[i]);
                    b[i].Dispose();
                    b[i] = s;
                }
                e = new Entry { Bmps = b };
                map[key] = e;
            }
            e.Refs++;
            return e.Bmps;
        }

        static bool IsImage(string p)
        {
            string e = Path.GetExtension(p).ToLowerInvariant();
            return e == ".png" || e == ".jpg" || e == ".jpeg" || e == ".bmp" || e == ".gif" || e == ".ico";
        }

        /// <summary>Custom dock icons: any picture file, scaled to fit.</summary>
        static Bitmap[] LoadImage(string p, int big, int small)
        {
            try
            {
                using (var fs = File.OpenRead(p))
                using (var img = Image.FromStream(fs))
                using (var bmp = new Bitmap(img))
                    return new[] { Gfx.Scale(bmp, big), Gfx.Scale(bmp, small) };
            }
            catch { return null; }
        }

        public static void Release(string key)
        {
            Entry e;
            if (key == null || !map.TryGetValue(key, out e)) return;
            if (--e.Refs > 0) return;
            foreach (var b in e.Bmps) b.Dispose();
            map.Remove(key);
        }
    }

    /// <summary>Small GDI+ drawing helpers.</summary>
    static class Gfx
    {
        /// <summary>
        /// Uniform dock tile: a squircle tinted with the app's own colour, the original icon centred on it.
        /// Gives every app (Win32, UWP, folders) the same shape, like macOS Big Sur icons.
        /// </summary>
        public static Bitmap MakeTile(Bitmap icon, bool dark)
        {
            int px = icon.Width;
            Color dom = Dominant(icon);
            var dst = new Bitmap(px, px, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(dst))
            using (var ia = new ImageAttributes())
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                ia.SetWrapMode(WrapMode.TileFlipXY);

                float m = px * 0.045f;
                var r = new RectangleF(m, m, px - 2 * m, px - 2 * m);
                // bright, clean tile (macOS style) - the app colour is only a whisper at the bottom
                Color top = Color.FromArgb(255, 255, 255, 255);
                Color bottom = Mix(Color.FromArgb(255, 236, 237, 242), dom, 0.10f);
                using (var path = Squircle(r))
                {
                    using (var br = new LinearGradientBrush(r, top, bottom, 90f)) g.FillPath(br, path);
                    // crisp hairline so white tiles still read on light wallpapers
                    using (var pen = new Pen(Color.FromArgb(38, 0, 0, 0), Math.Max(1f, px / 96f)))
                        g.DrawPath(pen, path);
                }
                float isz = r.Width * 0.74f;
                var ir = new Rectangle((int)Math.Round(px / 2f - isz / 2), (int)Math.Round(px / 2f - isz / 2), (int)Math.Round(isz), (int)Math.Round(isz));
                g.DrawImage(icon, ir, 0, 0, icon.Width, icon.Height, GraphicsUnit.Pixel, ia);
            }
            return dst;
        }

        /// <summary>
        /// "Glass" dock icon: frosted squircle, a soft glow in the app's own colours behind the logo,
        /// a light rim on top and a hairline edge (the look from modern macOS / iOS icon kits).
        /// </summary>
        public static Bitmap MakeGlassTile(Bitmap icon, bool dark)
        {
            int px = icon.Width;
            var dst = new Bitmap(px, px, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(dst))
            using (var ia = new ImageAttributes())
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                ia.SetWrapMode(WrapMode.TileFlipXY);

                float m = px * 0.055f;
                var r = new RectangleF(m, m, px - 2 * m, px - 2 * m);
                using (var path = Squircle(r))
                {
                    // frosted body
                    Color top = dark ? Color.FromArgb(70, 255, 255, 255) : Color.FromArgb(250, 255, 255, 255);
                    Color bot = dark ? Color.FromArgb(34, 255, 255, 255) : Color.FromArgb(232, 246, 246, 250);
                    using (var br = new LinearGradientBrush(r, top, bot, 90f)) g.FillPath(br, path);

                    // colour glow: the icon itself, heavily blurred, bleeding softly into the glass
                    var clip = g.Clip;
                    g.SetClip(path);
                    using (var glow = SoftBlur(icon, Math.Max(4, px / 7)))
                    {
                        float gs = r.Width * 0.92f;
                        var gr = new RectangleF(px / 2f - gs / 2, px / 2f - gs / 2 + px * 0.05f, gs, gs);
                        var cm = new ColorMatrix { Matrix33 = dark ? 0.75f : 0.5f };
                        using (var gia = new ImageAttributes())
                        {
                            gia.SetColorMatrix(cm);
                            gia.SetWrapMode(WrapMode.TileFlipXY);
                            g.DrawImage(glow, new[] { new PointF(gr.X, gr.Y), new PointF(gr.Right, gr.Y), new PointF(gr.X, gr.Bottom) },
                                new RectangleF(0, 0, glow.Width, glow.Height), GraphicsUnit.Pixel, gia);
                        }
                    }
                    g.Clip = clip;

                    // rim light (bright at the top, fading down) + hairline edge
                    float pw = Math.Max(1f, px / 72f);
                    using (var rim = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2),
                        Color.FromArgb(dark ? 150 : 255, 255, 255, 255), Color.FromArgb(dark ? 20 : 90, 255, 255, 255), 90f))
                    using (var pen = new Pen(rim, pw))
                    using (var inner = Squircle(RectangleF.Inflate(r, -pw, -pw)))
                        g.DrawPath(pen, inner);
                    using (var pen = new Pen(dark ? Color.FromArgb(46, 255, 255, 255) : Color.FromArgb(22, 0, 0, 0), pw))
                        g.DrawPath(pen, path);
                }
                float isz = r.Width * 0.62f;
                var ir = new Rectangle((int)Math.Round(px / 2f - isz / 2), (int)Math.Round(px / 2f - isz / 2), (int)Math.Round(isz), (int)Math.Round(isz));
                g.DrawImage(icon, ir, 0, 0, icon.Width, icon.Height, GraphicsUnit.Pixel, ia);
            }
            return dst;
        }

        enum IconShape { FullBleed, Mono, Multi }

        /// <summary>Looks at an icon: already a filled square? one main colour? or a multi-colour logo?</summary>
        static IconShape Analyse(Bitmap icon, out Color brand, out float darkShare, out bool lightLogo)
        {
            brand = Color.Gray;
            darkShare = 0;
            lightLogo = false;
            int w = icon.Width, h = icon.Height;
            var rect = new Rectangle(0, 0, w, h);
            var px = new byte[w * h * 4];
            var d = icon.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            for (int y = 0; y < h; y++) Marshal.Copy(d.Scan0 + y * d.Stride, px, y * w * 4, w * 4);
            icon.UnlockBits(d);

            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (px[(y * w + x) * 4 + 3] > 24) { if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y; }
            if (maxX < 0) return IconShape.Multi;
            int bw = maxX - minX + 1, bh = maxY - minY + 1;

            int solid = 0, opaque = 0, saturated = 0, light = 0, dark = 0;
            double lumSum = 0;
            double vSum = 0, vSq = 0; // brightness spread of the coloured pixels (shading = detail worth keeping)
            var buckets = new int[12];
            double sr = 0, sg = 0, sb = 0;
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    int i = (y * w + x) * 4;
                    byte a = px[i + 3];
                    if (a > 200) solid++;
                    if (a < 128) continue;
                    opaque++;
                    var c = Color.FromArgb(px[i + 2], px[i + 1], px[i]);
                    float s = c.GetSaturation(), v = Math.Max(c.R, Math.Max(c.G, c.B)) / 255f;
                    if (s < 0.18f && v > 0.82f) light++; // white details (letters, eyes...) a silhouette would erase
                    if (v < 0.28f) dark++;                // dark details (Spotify waves...) that must stay visible
                    lumSum += v;
                    if (s > 0.35f && v > 0.25f)
                    {
                        saturated++;
                        vSum += v; vSq += v * v;
                        buckets[(int)(c.GetHue() / 30) % 12]++;
                        sr += c.R; sg += c.G; sb += c.B;
                    }
                }
            float coverage = solid / (float)(bw * bh);
            darkShare = dark / (float)Math.Max(1, opaque);
            // a pale, colourless logo (ChatGPT...) would vanish on a white tile
            lightLogo = opaque > 0 && saturated < opaque * 0.06f && lumSum / opaque > 0.88 && light > opaque * 0.6f;
            float span = Math.Max(bw, bh) / (float)w, aspect = bw / (float)bh;
            if (coverage > 0.93f && span > 0.78f && aspect > 0.9f && aspect < 1.11f) return IconShape.FullBleed;

            if (saturated > 0)
            {
                brand = Color.FromArgb(255, (int)(sr / saturated), (int)(sg / saturated), (int)(sb / saturated));
                int top = 0, second = 0;
                for (int b = 0; b < 12; b++)
                {
                    // neighbouring hue buckets count as the same colour family
                    int fam = buckets[b] + buckets[(b + 1) % 12];
                    if (fam > top) { second = top; top = fam; } else if (fam > second) second = fam;
                }
                float satShare = saturated / (float)Math.Max(1, opaque);
                float lightShare = light / (float)Math.Max(1, opaque);
                double mean = vSum / saturated, spread = Math.Sqrt(Math.Max(0, vSq / saturated - mean * mean));
                if (satShare > 0.55f && top > saturated * 0.82f && lightShare < 0.012f && spread < 0.115) return IconShape.Mono;
            }
            return IconShape.Multi;
        }

        /// <summary>
        /// WhiteSur / macOS Big Sur style: every app becomes a squircle —
        /// single-colour logos sit in white on a tile of their brand colour, multi-colour logos sit on a clean white tile,
        /// icons that are already square are reshaped to the squircle.
        /// </summary>
        public static Bitmap MakeWhiteSur(Bitmap icon, string look = null, string name = null)
        {
            int px = icon.Width;
            Color brand;
            float darkShare;
            bool lightLogo;
            var shape = Analyse(icon, out brand, out darkShare, out lightLogo);
            // which look: the user's choice for this app, a known brand style, or the automatic analysis
            string mode = look;
            if (mode == null || mode == "auto") mode = BrandLook(name);
            if (mode == null)
                mode = shape == IconShape.FullBleed ? "full" : (shape == IconShape.Mono ? "brand" : (lightLogo ? "dark" : "white"));
            if (mode == "original") return Scale(icon, icon.Width);
            if (mode == "brand" && brand == Color.Gray) brand = Dominant(icon);
            var dst = new Bitmap(px, px, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(dst))
            using (var ia = new ImageAttributes())
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                ia.SetWrapMode(WrapMode.TileFlipXY);
                float m = px * 0.07f;
                var r = new RectangleF(m, m, px - 2 * m, px - 2 * m);
                using (var path = Squircle(r))
                {
                    var clip = g.Clip;
                    if (mode == "full")
                    {
                        // trim the transparent margin, then fill the squircle with the icon itself
                        var bounds = OpaqueBounds(icon);
                        g.SetClip(path);
                        var dr = RectangleF.Inflate(r, r.Width * 0.03f, r.Height * 0.03f);
                        g.DrawImage(icon, new[] { new PointF(dr.X, dr.Y), new PointF(dr.Right, dr.Y), new PointF(dr.X, dr.Bottom) },
                            bounds, GraphicsUnit.Pixel, ia);
                        g.Clip = clip;
                    }
                    else if (mode == "brand")
                    {
                        var top = Lighten(brand, 0.18f);
                        var bot = Darken(brand, 0.12f);
                        using (var br = new LinearGradientBrush(r, top, bot, 90f)) g.FillPath(br, path);
                        if (darkShare > 0.06f)
                        {
                            // logo has dark details (Spotify waves): cut the brand colour away, keep the details
                            using (var knock = KnockOut(icon, brand))
                            {
                                float isz = r.Width * 0.74f;
                                g.DrawImage(knock, new RectangleF(px / 2f - isz / 2, px / 2f - isz / 2, isz, isz));
                            }
                        }
                        else
                            using (var white = Silhouette(icon, Color.White))
                            {
                                float isz = r.Width * 0.62f;
                                var ir = new RectangleF(px / 2f - isz / 2, px / 2f - isz / 2, isz, isz);
                                // tiny shadow under the glyph so it lifts off the colour
                                using (var shadow = Silhouette(icon, Color.FromArgb(60, 0, 0, 0)))
                                    g.DrawImage(shadow, new RectangleF(ir.X, ir.Y + px * 0.012f, ir.Width, ir.Height));
                                g.DrawImage(white, ir);
                            }
                    }
                    else
                    {
                        if (mode == "dark")
                            using (var br = new LinearGradientBrush(r, Color.FromArgb(255, 52, 52, 56), Color.FromArgb(255, 18, 18, 20), 90f)) g.FillPath(br, path);
                        else
                            using (var br = new LinearGradientBrush(r, Color.White, Color.FromArgb(255, 236, 236, 241), 90f)) g.FillPath(br, path);
                        float isz = r.Width * 0.70f;
                        var ir = new Rectangle((int)Math.Round(px / 2f - isz / 2), (int)Math.Round(px / 2f - isz / 2), (int)Math.Round(isz), (int)Math.Round(isz));
                        g.DrawImage(icon, ir, 0, 0, icon.Width, icon.Height, GraphicsUnit.Pixel, ia);
                    }

                    // gloss: soft light on the upper half + a crisp rim
                    g.SetClip(path);
                    using (var gl = new LinearGradientBrush(new RectangleF(r.X, r.Y, r.Width, r.Height * 0.55f),
                        Color.FromArgb(mode == "white" ? 0 : (mode == "dark" ? 26 : 46), 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                        g.FillRectangle(gl, r.X, r.Y, r.Width, r.Height * 0.55f);
                    g.Clip = clip;
                    float pw = Math.Max(1f, px / 80f);
                    using (var rim = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2),
                        Color.FromArgb(120, 255, 255, 255), Color.FromArgb(10, 255, 255, 255), 90f))
                    using (var pen = new Pen(rim, pw))
                    using (var inner = Squircle(RectangleF.Inflate(r, -pw / 2, -pw / 2)))
                        g.DrawPath(pen, inner);
                    using (var pen = new Pen(Color.FromArgb(mode == "white" ? 34 : 22, 0, 0, 0), pw))
                        g.DrawPath(pen, path);
                }
            }
            return dst;
        }

        /// <summary>Brands whose real app icon is their logo on black (or white) — matches WhiteSur / their own icons.</summary>
        static string BrandLook(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string n = name.ToLowerInvariant();
            string[] dark = { "netflix", "tiktok", "github", "threads", "epic games", "prime video", "apple tv", "x (twitter)", "notion calendar", "ubisoft", "obs studio", "cursor", "warp" };
            string[] white = { "youtube", "google", "gmail", "chrome", "notion", "figma", "slack", "zoom", "teams", "outlook", "word", "excel", "powerpoint", "onenote" };
            foreach (var k in dark) if (n.Contains(k)) return "dark";
            foreach (var k in white) if (n.Contains(k)) return "white";
            return null;
        }

        static RectangleF OpaqueBounds(Bitmap b)
        {
            int minX = b.Width, minY = b.Height, maxX = 0, maxY = 0;
            for (int y = 0; y < b.Height; y += 1)
                for (int x = 0; x < b.Width; x += 1)
                    if (b.GetPixel(x, y).A > 24) { if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y; }
            if (maxX <= minX) return new RectangleF(0, 0, b.Width, b.Height);
            return new RectangleF(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        /// <summary>Removes the brand-coloured pixels (they become the tile), keeping every other detail.</summary>
        static Bitmap KnockOut(Bitmap icon, Color brand)
        {
            int w = icon.Width, h = icon.Height;
            var rect = new Rectangle(0, 0, w, h);
            var px = new byte[w * h * 4];
            var d = icon.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            for (int y = 0; y < h; y++) Marshal.Copy(d.Scan0 + y * d.Stride, px, y * w * 4, w * 4);
            icon.UnlockBits(d);
            float bh = brand.GetHue();
            for (int i = 0; i < px.Length; i += 4)
            {
                if (px[i + 3] == 0) continue;
                var c = Color.FromArgb(px[i + 2], px[i + 1], px[i]);
                float s = c.GetSaturation(), v = Math.Max(c.R, Math.Max(c.G, c.B)) / 255f;
                float dh = Math.Abs(c.GetHue() - bh);
                if (dh > 180) dh = 360 - dh;
                if (s > 0.3f && v > 0.25f && dh < 40)
                {
                    // fade by how "brand" the pixel is so edges stay smooth
                    float k = Math.Min(1f, (s - 0.3f) / 0.25f) * Math.Min(1f, (v - 0.25f) / 0.2f);
                    px[i + 3] = (byte)(px[i + 3] * (1 - k));
                }
            }
            var outp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            var od = outp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            for (int y = 0; y < h; y++) Marshal.Copy(px, y * w * 4, od.Scan0 + y * od.Stride, w * 4);
            outp.UnlockBits(od);
            return outp;
        }

        /// <summary>The icon's shape filled with one colour (alpha kept).</summary>
        static Bitmap Silhouette(Bitmap icon, Color c)
        {
            int w = icon.Width, h = icon.Height;
            var rect = new Rectangle(0, 0, w, h);
            var px = new byte[w * h * 4];
            var d = icon.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            for (int y = 0; y < h; y++) Marshal.Copy(d.Scan0 + y * d.Stride, px, y * w * 4, w * 4);
            icon.UnlockBits(d);
            for (int i = 0; i < px.Length; i += 4)
            {
                px[i] = c.B; px[i + 1] = c.G; px[i + 2] = c.R;
                px[i + 3] = (byte)(px[i + 3] * c.A / 255);
            }
            var outp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            var od = outp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            for (int y = 0; y < h; y++) Marshal.Copy(px, y * w * 4, od.Scan0 + y * od.Stride, w * 4);
            outp.UnlockBits(od);
            return outp;
        }

        public static Color Darken(Color c, float k)
        {
            return Color.FromArgb(c.A, (int)(c.R * (1 - k)), (int)(c.G * (1 - k)), (int)(c.B * (1 - k)));
        }

        /// <summary>Cheap, smooth blur: shrink a lot, then scale back up twice with bilinear filtering.</summary>
        public static Bitmap SoftBlur(Bitmap src, int radius)
        {
            int w = src.Width, h = src.Height;
            int sw = Math.Max(2, w / radius), sh = Math.Max(2, h / radius);
            var tiny = new Bitmap(sw + 2, sh + 2, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(tiny))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(src, new Rectangle(1, 1, sw, sh));
            }
            var mid = new Bitmap(Math.Max(4, w / 3), Math.Max(4, h / 3), PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(mid))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(tiny, new Rectangle(0, 0, mid.Width, mid.Height), 0.5f, 0.5f, tiny.Width - 1, tiny.Height - 1, GraphicsUnit.Pixel);
            }
            tiny.Dispose();
            var outp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(outp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(mid, new Rectangle(0, 0, w, h), 0.5f, 0.5f, mid.Width - 1, mid.Height - 1, GraphicsUnit.Pixel);
            }
            mid.Dispose();
            return outp;
        }

        /// <summary>Continuous-corner rounded square (closer to an iOS/macOS squircle than a plain rounded rect).</summary>
        public static GraphicsPath Squircle(RectangleF r)
        {
            var p = new GraphicsPath();
            float w = r.Width, h = r.Height, k = 0.26f * w; // corner extent
            float c = k * 0.55f;                            // bezier handle
            p.StartFigure();
            p.AddLine(r.X + k, r.Y, r.Right - k, r.Y);
            p.AddBezier(r.Right - k, r.Y, r.Right - k + c, r.Y, r.Right, r.Y + k - c, r.Right, r.Y + k);
            p.AddLine(r.Right, r.Y + k, r.Right, r.Bottom - k);
            p.AddBezier(r.Right, r.Bottom - k, r.Right, r.Bottom - k + c, r.Right - k + c, r.Bottom, r.Right - k, r.Bottom);
            p.AddLine(r.Right - k, r.Bottom, r.X + k, r.Bottom);
            p.AddBezier(r.X + k, r.Bottom, r.X + k - c, r.Bottom, r.X, r.Bottom - k + c, r.X, r.Bottom - k);
            p.AddLine(r.X, r.Bottom - k, r.X, r.Y + k);
            p.AddBezier(r.X, r.Y + k, r.X, r.Y + k - c, r.X + k - c, r.Y, r.X + k, r.Y);
            p.CloseFigure();
            return p;
        }

        /// <summary>Most characteristic colour of an icon (saturated, opaque pixels win).</summary>
        public static Color Dominant(Bitmap icon)
        {
            using (var small = new Bitmap(24, 24, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(small))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.DrawImage(icon, 0, 0, 24, 24);
                }
                double r = 0, gr = 0, b = 0, wsum = 0;
                for (int y = 0; y < 24; y++)
                    for (int x = 0; x < 24; x++)
                    {
                        var c = small.GetPixel(x, y);
                        if (c.A < 128) continue;
                        float sat = c.GetSaturation(), bri = c.GetBrightness();
                        double w = (c.A / 255.0) * (0.15 + sat * sat) * (bri > 0.15 && bri < 0.95 ? 1 : 0.2);
                        r += c.R * w; gr += c.G * w; b += c.B * w; wsum += w;
                    }
                if (wsum < 0.001) return Color.FromArgb(255, 128, 128, 136);
                return Color.FromArgb(255, (int)(r / wsum), (int)(gr / wsum), (int)(b / wsum));
            }
        }

        public static Color Mix(Color a, Color b, float t)
        {
            return Color.FromArgb(255, (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        /// <summary>Bakes a soft drop shadow under an icon (macOS icons have one; Windows icons usually don't).</summary>
        public static Bitmap WithShadow(Bitmap src)
        {
            int w = src.Width, h = src.Height;
            var rect = new Rectangle(0, 0, w, h);
            var px = new byte[w * h * 4];
            var d = src.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            for (int y = 0; y < h; y++) Marshal.Copy(d.Scan0 + y * d.Stride, px, y * w * 4, w * 4);
            src.UnlockBits(d);

            int r = Math.Max(1, w / 36), dy = Math.Max(1, w / 48);
            float[] a = new float[w * h], t = new float[w * h];
            for (int i = 0; i < w * h; i++) a[i] = px[i * 4 + 3] / 255f;
            for (int pass = 0; pass < 2; pass++)
            {
                BoxBlur(a, t, w, h, r, true);
                BoxBlur(t, a, w, h, r, false);
            }
            for (int y = h - 1; y >= 0; y--)
                for (int x = 0; x < w; x++)
                {
                    int i = (y * w + x) * 4;
                    float sa = y - dy >= 0 ? a[(y - dy) * w + x] * 0.30f : 0;
                    float ia = px[i + 3] / 255f;
                    float oa = ia + sa * (1 - ia);
                    px[i + 3] = (byte)Math.Min(255, Math.Round(oa * 255)); // colour channels are premultiplied; black shadow adds none
                }

            var dst = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            var dd = dst.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            for (int y = 0; y < h; y++) Marshal.Copy(px, y * w * 4, dd.Scan0 + y * dd.Stride, w * 4);
            dst.UnlockBits(dd);
            return dst;
        }

        static void BoxBlur(float[] src, float[] dst, int w, int h, int r, bool horizontal)
        {
            int n = horizontal ? w : h, lines = horizontal ? h : w;
            float k = 1f / (2 * r + 1);
            for (int l = 0; l < lines; l++)
            {
                float sum = 0;
                for (int i = -r; i <= r; i++) sum += Get(src, w, h, horizontal, l, i);
                for (int i = 0; i < n; i++)
                {
                    int idx = horizontal ? l * w + i : i * w + l;
                    dst[idx] = sum * k;
                    sum += Get(src, w, h, horizontal, l, i + r + 1) - Get(src, w, h, horizontal, l, i - r);
                }
            }
        }

        static float Get(float[] a, int w, int h, bool horizontal, int line, int i)
        {
            if (horizontal) return i < 0 || i >= w ? 0 : a[line * w + i];
            return i < 0 || i >= h ? 0 : a[i * w + line];
        }

        public static Bitmap Scale(Bitmap src, int px)
        {
            var dst = new Bitmap(px, px, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(dst))
            using (var ia = new ImageAttributes())
            {
                ia.SetWrapMode(WrapMode.TileFlipXY);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                float k = Math.Min((float)px / src.Width, (float)px / src.Height);
                float w = src.Width * k, h = src.Height * k;
                g.DrawImage(src, new Rectangle((int)Math.Round((px - w) / 2), (int)Math.Round((px - h) / 2), (int)Math.Round(w), (int)Math.Round(h)),
                    0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia);
            }
            return dst;
        }

        /// <summary>Fallback icon: rounded tile with the app's first letter.</summary>
        public static Bitmap LetterIcon(string name, int px)
        {
            var bmp = new Bitmap(px, px, PixelFormat.Format32bppPArgb);
            int hash = 0;
            foreach (char c in name ?? "?") hash = hash * 31 + c;
            Color[] palette = { Color.FromArgb(10, 132, 255), Color.FromArgb(48, 209, 88), Color.FromArgb(255, 159, 10),
                                Color.FromArgb(191, 90, 242), Color.FromArgb(255, 69, 58), Color.FromArgb(100, 210, 255) };
            Color c1 = palette[Math.Abs(hash) % palette.Length];
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                var r = new RectangleF(px * 0.06f, px * 0.06f, px * 0.88f, px * 0.88f);
                using (var path = RoundRect(r, px * 0.22f))
                using (var br = new LinearGradientBrush(r, Lighten(c1, 0.25f), c1, 90f))
                    g.FillPath(br, path);
                string letter = string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpperInvariant();
                using (var f = new Font(Fonts.UiBold, px * 0.46f, GraphicsUnit.Pixel))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(letter, f, Brushes.White, r, sf);
            }
            return bmp;
        }

        public static Color Lighten(Color c, float k)
        {
            return Color.FromArgb(c.A, (int)(c.R + (255 - c.R) * k), (int)(c.G + (255 - c.G) * k), (int)(c.B + (255 - c.B) * k));
        }

        public static GraphicsPath RoundRect(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            rad = Math.Max(0.5f, Math.Min(rad, Math.Min(r.Width, r.Height) / 2f));
            float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    static class Fonts
    {
        public static readonly string Ui = Pick("Segoe UI Variable Text", "Segoe UI");
        public static readonly string UiBold = Pick("Segoe UI Variable Text Semibold", "Segoe UI Semibold", "Segoe UI");
        public static readonly string Icons = Pick("Segoe Fluent Icons", "Segoe MDL2 Assets");

        static string Pick(params string[] names)
        {
            foreach (var n in names)
            {
                try { using (var ff = new FontFamily(n)) return n; }
                catch { }
            }
            return "Segoe UI";
        }
    }

    /// <summary>A 32bpp premultiplied DIB section with a GDI+ Graphics over it. Zero-copy presentation.</summary>
    sealed class Surface : IDisposable
    {
        public int W, H;
        public Graphics G;
        IntPtr hdc, hbmp, oldBmp;
        Bitmap bmp;

        public bool Ensure(int w, int h)
        {
            w = Math.Max(1, w); h = Math.Max(1, h);
            if (w == W && h == H && G != null) return false;
            Dispose();
            W = w; H = h;
            var bi = new BITMAPINFO();
            bi.h.biSize = Marshal.SizeOf(typeof(BITMAPINFOHEADER));
            bi.h.biWidth = w;
            bi.h.biHeight = -h;
            bi.h.biPlanes = 1;
            bi.h.biBitCount = 32;
            IntPtr screen = N.GetDC(IntPtr.Zero);
            hdc = N.CreateCompatibleDC(screen);
            N.ReleaseDC(IntPtr.Zero, screen);
            IntPtr bits;
            hbmp = N.CreateDIBSection(hdc, ref bi, 0, out bits, IntPtr.Zero, 0);
            oldBmp = N.SelectObject(hdc, hbmp);
            bmp = new Bitmap(w, h, w * 4, PixelFormat.Format32bppPArgb, bits);
            G = Graphics.FromImage(bmp);
            return true;
        }

        public Bitmap Bitmap { get { return bmp; } }

        public void Present(IntPtr hwnd, int x, int y, byte opacity = 255)
        {
            G.Flush(FlushIntention.Sync);
            N.GdiFlush();
            var dst = new POINT(x, y);
            var size = new SIZE(W, H);
            var src = new POINT(0, 0);
            var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = opacity, AlphaFormat = 1 };
            N.UpdateLayeredWindow(hwnd, IntPtr.Zero, ref dst, ref size, hdc, ref src, 0, ref blend, 2);
        }

        public void BlitTo(IntPtr dstDc)
        {
            G.Flush(FlushIntention.Sync);
            N.GdiFlush();
            N.BitBlt(dstDc, 0, 0, W, H, hdc, 0, 0, 0x00CC0020);
        }

        public void Dispose()
        {
            if (G != null) { G.Dispose(); G = null; }
            if (bmp != null) { bmp.Dispose(); bmp = null; }
            if (hdc != IntPtr.Zero)
            {
                N.SelectObject(hdc, oldBmp);
                N.DeleteObject(hbmp);
                N.DeleteDC(hdc);
                hdc = IntPtr.Zero;
            }
            W = H = 0;
        }
    }
}
