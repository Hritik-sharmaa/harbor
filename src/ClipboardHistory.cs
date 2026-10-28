using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Harbor
{
    /// <summary>
    /// Clipboard history for the island: the last 30 things you copied (text, links, images, files) plus every new
    /// screenshot (Win+Shift+S, PrtScn, Win+PrtScn). Windows tells us when the clipboard changes; nothing polls.
    /// Text is kept in memory only and is gone when Harbor quits, like Windows' own history. Copied images are written
    /// to %LOCALAPPDATA%\Harbor\clipboard so they can be dragged out; that folder is emptied whenever Harbor starts or quits.
    /// Copies that a password manager marks as private are skipped.
    /// </summary>
    static class ClipboardHistory
    {
        public sealed class Item
        {
            public string Kind;          // "text", "image" or "files"
            public string Text;          // the text, or the file list (one per line)
            public string ImagePath;     // the PNG on disk, for images and screenshots
            public bool OwnsFile;        // written by us (deleted with the item), not a file in your Screenshots folder
            public Bitmap Thumb;         // small preview for images
            public Size ImageSize;
            public string Source;        // the app it came from, when Windows says
            public bool Screenshot;
            public DateTime At = DateTime.Now;
            public string Key;           // spots the same thing copied again
        }

        const int Max = 30;
        static readonly List<Item> items = new List<Item>();
        static Listener listener;
        static FileSystemWatcher shots;
        static Timer settle;
        static int ignoreUntil;          // our own copy back to the clipboard
        public static event Action Changed;

        public static List<Item> Items { get { return items; } }
        public static bool On { get { return listener != null || demo; } }

        static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Harbor", "clipboard"); } }

        /// <summary>Starts (or stops) listening, following the "ClipboardHistory" setting.</summary>
        public static void Init()
        {
            if (!Look.Bool("ClipboardHistory")) { Stop(); return; }
            if (listener != null) return;
            WipeDir();
            try { listener = new Listener(); }
            catch (Exception ex) { App.Log(ex); listener = null; return; }
            WatchScreenshots();
        }

        public static void Stop()
        {
            if (listener != null) { listener.Dispose(); listener = null; }
            if (shots != null) { shots.Dispose(); shots = null; }
            Clear();
            WipeDir();
        }

        static void WipeDir()
        {
            try { if (Directory.Exists(Dir)) foreach (var f in Directory.GetFiles(Dir)) File.Delete(f); } catch { }
        }

        static void Raise() { if (Changed != null) Changed(); }

        // ------------------------------------------------------------------ listening

        /// <summary>A message-only window that Windows notifies on every clipboard change.</summary>
        sealed class Listener : NativeWindow, IDisposable
        {
            [DllImport("user32.dll", SetLastError = true)] static extern bool AddClipboardFormatListener(IntPtr h);
            [DllImport("user32.dll")] static extern bool RemoveClipboardFormatListener(IntPtr h);

            public Listener()
            {
                CreateHandle(new CreateParams { Caption = "HarborClipboard", Parent = (IntPtr)(-3) /*HWND_MESSAGE*/ });
                if (!AddClipboardFormatListener(Handle)) throw new System.ComponentModel.Win32Exception();
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == 0x031D /*WM_CLIPBOARDUPDATE*/) Soon();
                base.WndProc(ref m);
            }

            public void Dispose()
            {
                if (Handle == IntPtr.Zero) return;
                RemoveClipboardFormatListener(Handle);
                DestroyHandle();
            }
        }

        /// <summary>Apps put several formats on the clipboard one after another: read it once they're done.</summary>
        static void Soon()
        {
            if (settle == null)
            {
                settle = new Timer { Interval = 150 };
                settle.Tick += delegate { settle.Stop(); Capture(); };
            }
            settle.Stop();
            settle.Start();
        }

        [DllImport("user32.dll")] static extern bool IsClipboardFormatAvailable(uint format);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterClipboardFormat(string name);
        [DllImport("user32.dll")] static extern IntPtr GetClipboardOwner();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

        /// <summary>Password managers (and Windows' own history) mark private copies with these formats.</summary>
        static bool Private(IDataObject d)
        {
            if (IsClipboardFormatAvailable(RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing"))) return true;
            if (IsClipboardFormatAvailable(RegisterClipboardFormat("Clipboard Viewer Ignore"))) return true;
            try
            {
                const string can = "CanIncludeInClipboardHistory";
                if (d.GetDataPresent(can))
                {
                    var ms = d.GetData(can) as MemoryStream;
                    if (ms != null && ms.Length >= 4 && new BinaryReader(ms).ReadInt32() == 0) return true;
                }
            }
            catch { }
            return false;
        }

        static string OwnerApp()
        {
            try
            {
                var h = GetClipboardOwner();
                if (h == IntPtr.Zero) return null;
                uint pid;
                GetWindowThreadProcessId(h, out pid);
                if (pid == 0) return null;
                using (var p = Process.GetProcessById((int)pid)) return p.ProcessName;
            }
            catch { return null; }
        }

        static bool IsSnipping(string app)
        {
            return app != null && (app.IndexOf("ScreenClipping", StringComparison.OrdinalIgnoreCase) >= 0
                || app.IndexOf("SnippingTool", StringComparison.OrdinalIgnoreCase) >= 0
                || app.IndexOf("ScreenSketch", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        static void Capture()
        {
            if (unchecked(Environment.TickCount - ignoreUntil) < 0) return;
            IDataObject d;
            try { d = Clipboard.GetDataObject(); }
            catch { return; } // someone else still has it open; the next change will come
            if (d == null || Private(d)) return;
            string app = OwnerApp();
            try
            {
                // files first, then text (Office also puts a picture of copied text), then images
                if (d.GetDataPresent(DataFormats.FileDrop))
                {
                    var files = d.GetData(DataFormats.FileDrop) as string[];
                    if (files != null && files.Length > 0)
                    {
                        string list = string.Join("\n", files);
                        Add(new Item { Kind = "files", Text = list, Key = "f:" + list, Source = Friendly(app) });
                        return;
                    }
                }
                if (d.GetDataPresent(DataFormats.UnicodeText))
                {
                    string t = d.GetData(DataFormats.UnicodeText) as string;
                    if (!string.IsNullOrEmpty(t) && t.Trim() != "")
                    {
                        if (t.Length > 50000) t = t.Substring(0, 50000);
                        Add(new Item { Kind = "text", Text = t, Key = "t:" + t, Source = Friendly(app) });
                        return;
                    }
                }
                if (d.GetDataPresent(DataFormats.Bitmap) || d.GetDataPresent(DataFormats.Dib))
                {
                    var img = Clipboard.GetImage();
                    if (img != null) AddImage(img, null, IsSnipping(app), Friendly(app));
                }
            }
            catch (Exception ex) { App.Log(ex); }
        }

        static string Friendly(string app)
        {
            if (string.IsNullOrEmpty(app)) return null;
            if (IsSnipping(app)) return "Screenshot";
            switch (app.ToLowerInvariant())
            {
                case "msedge": return "Edge";
                case "chrome": return "Chrome";
                case "firefox": return "Firefox";
                case "code": return "VS Code";
                case "explorer": return "File Explorer";
                case "winword": return "Word";
                case "excel": return "Excel";
                case "powerpnt": return "PowerPoint";
                case "windowsterminal": return "Terminal";
                case "spotify": return "Spotify";
            }
            return char.ToUpper(app[0]) + app.Substring(1);
        }

        /// <summary>Adds a picture: a small preview now, the full PNG written in the background.</summary>
        static void AddImage(Image img, string existingFile, bool screenshot, string source)
        {
            var it = new Item { Kind = "image", ImageSize = img.Size, Screenshot = screenshot, Source = screenshot ? "Screenshot" : source };
            it.Thumb = MakeThumb(img);
            it.Key = "i:" + img.Width + "x" + img.Height + ":" + Fingerprint(it.Thumb);
            if (existingFile != null)
            {
                it.ImagePath = existingFile;
                img.Dispose();
            }
            else
            {
                try { Directory.CreateDirectory(Dir); } catch { }
                string path = Path.Combine(Dir, (screenshot ? "Screenshot " : "Image ") + DateTime.Now.ToString("yyyy-MM-dd HHmmss") + ".png");
                it.ImagePath = path;
                it.OwnsFile = true;
                System.Threading.ThreadPool.QueueUserWorkItem(delegate
                {
                    try { img.Save(path, ImageFormat.Png); } catch (Exception ex) { App.Log(ex); }
                    finally { img.Dispose(); }
                });
            }
            Add(it);
        }

        static Bitmap MakeThumb(Image img)
        {
            const int maxW = 240, maxH = 150;
            float k = Math.Min(1f, Math.Min(maxW / (float)img.Width, maxH / (float)img.Height));
            int w = Math.Max(1, (int)(img.Width * k)), h = Math.Max(1, (int)(img.Height * k));
            var b = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(b))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(img, 0, 0, w, h);
            }
            return b;
        }

        static string Fingerprint(Bitmap b)
        {
            unchecked
            {
                int hsh = 17;
                for (int i = 0; i < 64; i++) hsh = hsh * 31 + b.GetPixel(i * 7919 % b.Width, i * 104729 % b.Height).ToArgb();
                return hsh.ToString("x8");
            }
        }

        static void Add(Item it)
        {
            var same = items.FindIndex(x => x.Key == it.Key);
            if (same == 0) { items[0].At = DateTime.Now; Drop(it, false); Raise(); return; }
            if (same > 0) { var old = items[same]; items.RemoveAt(same); Drop(old, true); }
            items.Insert(0, it);
            while (items.Count > Max) { var last = items[items.Count - 1]; items.RemoveAt(items.Count - 1); Drop(last, true); }
            Raise();
        }

        /// <summary>Frees an item's preview (and its PNG, if we wrote it).</summary>
        static void Drop(Item it, bool deleteFile)
        {
            if (it.Thumb != null) { it.Thumb.Dispose(); it.Thumb = null; }
            if (deleteFile && it.OwnsFile && it.ImagePath != null)
            {
                string p = it.ImagePath;
                System.Threading.ThreadPool.QueueUserWorkItem(delegate { try { System.Threading.Thread.Sleep(2000); File.Delete(p); } catch { } });
            }
        }

        // ------------------------------------------------------------------ screenshots saved to Pictures\Screenshots

        static void WatchScreenshots()
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
                if (!Directory.Exists(dir)) return;
                shots = new FileSystemWatcher(dir, "*.png") { NotifyFilter = NotifyFilters.FileName, IncludeSubdirectories = false };
                shots.Created += (s, e) => { string p = e.FullPath; App.Post(() => ScreenshotSaved(p, 0)); };
                shots.EnableRaisingEvents = true;
            }
            catch (Exception ex) { App.Log(ex); }
        }

        static void ScreenshotSaved(string path, int attempt)
        {
            if (listener == null) return;
            // Win+Shift+S also copies it: that copy is already here, just mark it as a screenshot
            if (items.Count > 0 && items[0].Kind == "image" && (DateTime.Now - items[0].At).TotalSeconds < 6)
            {
                items[0].Screenshot = true;
                items[0].Source = "Screenshot";
                Raise();
                return;
            }
            Image img = null;
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var src = Image.FromStream(fs)) img = new Bitmap(src);
            }
            catch
            {
                // still being written
                if (attempt < 5) { var t = new Timer { Interval = 300 }; t.Tick += delegate { t.Dispose(); ScreenshotSaved(path, attempt + 1); }; t.Start(); }
                return;
            }
            AddImage(img, path, true, "Screenshot");
        }

        // ------------------------------------------------------------------ using an item

        /// <summary>Puts it back on the clipboard (and at the top of the list).</summary>
        public static void CopyBack(Item it)
        {
            ignoreUntil = Environment.TickCount + 600;
            try
            {
                if (it.Kind == "text") Clipboard.SetText(it.Text);
                else if (it.Kind == "files")
                {
                    var sc = new StringCollection();
                    sc.AddRange(it.Text.Split('\n'));
                    Clipboard.SetFileDropList(sc);
                }
                else if (it.ImagePath != null && File.Exists(it.ImagePath))
                {
                    using (var fs = new FileStream(it.ImagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var src = Image.FromStream(fs))
                    using (var b = new Bitmap(src))
                    {
                        var d = new DataObject();
                        d.SetImage(b);
                        d.SetFileDropList(new StringCollection { it.ImagePath }); // pastes as a file into folders and upload boxes too
                        Clipboard.SetDataObject(d, true);
                    }
                }
            }
            catch (Exception ex) { App.Log(ex); }
            int i = items.IndexOf(it);
            if (i > 0) { items.RemoveAt(i); items.Insert(0, it); }
            it.At = DateTime.Now;
            Raise();
        }

        /// <summary>A file to drag out for this item, if it has one.</summary>
        public static string DragPath(Item it)
        {
            if (it.Kind == "image") return it.ImagePath != null && File.Exists(it.ImagePath) ? it.ImagePath : null;
            if (it.Kind == "files") { string f = it.Text.Split('\n')[0]; return File.Exists(f) || Directory.Exists(f) ? f : null; }
            return null;
        }

        public static void Remove(Item it)
        {
            if (!items.Remove(it)) return;
            Drop(it, true);
            Raise();
        }

        public static void Clear()
        {
            foreach (var it in items) Drop(it, true);
            items.Clear();
            Raise();
        }

        // ------------------------------------------------------------------ --snapshot --demo

        static bool demo;

        public static void UseDemo()
        {
            demo = true;
            var now = DateTime.Now;
            items.Add(new Item { Kind = "text", Text = "https://github.com/Hritik-sharmaa/harbor/releases", Source = "Edge", At = now.AddMinutes(-1) });
            var b = new Bitmap(240, 150, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(b))
            using (var br = new LinearGradientBrush(new Rectangle(0, 0, 240, 150), Color.FromArgb(255, 60, 90, 200), Color.FromArgb(255, 200, 80, 160), 35f))
            {
                g.FillRectangle(br, 0, 0, 240, 150);
                using (var w = new SolidBrush(Color.FromArgb(200, 255, 255, 255))) g.FillRectangle(w, 30, 30, 180, 22);
            }
            items.Add(new Item { Kind = "image", Thumb = b, ImageSize = new Size(1920, 1200), Screenshot = true, Source = "Screenshot", At = now.AddMinutes(-4) });
            items.Add(new Item { Kind = "text", Text = "git commit -m \"Add guest checkout\"", Source = "Terminal", At = now.AddMinutes(-12) });
            items.Add(new Item { Kind = "text", Text = "Meeting moved to Thursday 4 PM. Bring the Q3 numbers and the onboarding draft.", Source = "Outlook", At = now.AddMinutes(-35) });
            items.Add(new Item { Kind = "files", Text = @"C:\Users\you\Downloads\invoice-2026-10.pdf", Source = "File Explorer", At = now.AddHours(-2) });
        }
    }
}
