using System;
using System.Collections.Generic;
using System.IO;

namespace Harbor
{
    /// <summary>
    /// Your Downloads folder for the island: what's downloading right now (browsers write a .crdownload / .part file
    /// until they're done) and the latest finished files. A folder watcher wakes Harbor only when something changes
    /// there; a finished download shows a live activity.
    /// </summary>
    static class Downloads
    {
        public sealed class InProgress { public string Name, Path; public long Size; public DateTime Started; }

        // (not ".tmp": apps save documents through temp files too, and that isn't a download)
        static readonly string[] partial = { ".crdownload", ".part", ".partial", ".download", ".opdownload" };
        static FileSystemWatcher watcher;
        static readonly Dictionary<string, DateTime> announced = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        public static event Action Changed;

        public static string Folder { get { return ShellIcons.Downloads; } }

        public static bool IsPartial(string path)
        {
            foreach (var e in partial) if (path.EndsWith(e, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static void Init()
        {
            if (watcher != null || App.SnapshotMode) return;
            try
            {
                if (!Directory.Exists(Folder)) return;
                watcher = new FileSystemWatcher(Folder) { NotifyFilter = NotifyFilters.FileName, IncludeSubdirectories = false };
                watcher.Renamed += (s, e) => App.Post(() => OnRenamed(e.OldFullPath, e.FullPath));
                watcher.Created += (s, e) => App.Post(() => OnCreated(e.FullPath));
                watcher.Deleted += (s, e) => App.Post(() => OnDeleted(e.FullPath));
                watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex) { App.Log(ex); }
        }

        static void Fire() { if (Changed != null) Changed(); }

        // Chrome / Edge: "x.crdownload" renamed to "x" when done
        static void OnRenamed(string from, string to)
        {
            if (IsPartial(from) && !IsPartial(to)) Finished(to);
            Fire();
        }

        // Firefox: an empty "x" plus "x.part"; the .part goes away when done
        static void OnDeleted(string path)
        {
            if (IsPartial(path))
            {
                string final = path.Substring(0, path.LastIndexOf('.'));
                if (File.Exists(final)) Finished(final);
            }
            Fire();
        }

        static void OnCreated(string path)
        {
            if (!IsPartial(path))
            {
                // saved straight to its name (some apps do): wait a moment, then make sure no partial file belongs to it
                var t = new System.Windows.Forms.Timer { Interval = 2500 };
                t.Tick += delegate
                {
                    t.Dispose();
                    bool busy = false;
                    foreach (var e in partial) if (File.Exists(path + e)) busy = true;
                    try { if (!busy && File.Exists(path) && new FileInfo(path).Length > 0) Finished(path); } catch { }
                };
                t.Start();
            }
            Fire();
        }

        static void Finished(string path)
        {
            DateTime last;
            if (announced.TryGetValue(path, out last) && (DateTime.Now - last).TotalSeconds < 30) return;
            announced[path] = DateTime.Now;
            if (announced.Count > 50) announced.Clear();
            string name = System.IO.Path.GetFileName(path);
            LiveActivity.Show(new LiveActivity.Item
            {
                Glyph = "", Title = "Downloaded", Detail = name, Tint = System.Drawing.Color.FromArgb(255, 100, 180, 255),
                Action = "Open", Click = () => Launcher.Open(path),
            });
        }

        /// <summary>Files still downloading (written to in the last couple of minutes).</summary>
        public static List<InProgress> Active()
        {
            var l = new List<InProgress>();
            if (demo) { l.Add(new InProgress { Name = "harbor-demo-video.mp4", Size = 184L * 1024 * 1024, Started = DateTime.Now.AddSeconds(-40) }); return l; }
            try
            {
                foreach (var f in new DirectoryInfo(Folder).GetFiles())
                    if (IsPartial(f.Name) && (DateTime.Now - f.LastWriteTime).TotalMinutes < 2 && !f.Name.StartsWith("~"))
                    {
                        string name = f.Name.Substring(0, f.Name.LastIndexOf('.'));
                        if (name.StartsWith("Unconfirmed ", StringComparison.OrdinalIgnoreCase)) name = "Starting download…";
                        l.Add(new InProgress { Name = name, Path = f.FullName, Size = f.Length, Started = f.CreationTime });
                    }
            }
            catch { }
            return l;
        }

        /// <summary>The newest finished downloads.</summary>
        public static List<FileSystemInfo> Recent(int max)
        {
            var l = new List<FileSystemInfo>();
            if (demo) // --snapshot --demo: Windows' own sounds stand in for your files
            {
                try { foreach (var f in new DirectoryInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media")).GetFiles("*.wav")) { l.Add(f); if (l.Count >= max) break; } } catch { }
                return l;
            }
            try
            {
                foreach (var e in new DirectoryInfo(Folder).GetFileSystemInfos())
                    if (!IsPartial(e.Name) && (e.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0 && !e.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) l.Add(e);
                l.Sort((a, b) => b.LastWriteTime.CompareTo(a.LastWriteTime));
                if (l.Count > max) l.RemoveRange(max, l.Count - max);
            }
            catch { }
            return l;
        }

        public static string Size(long b)
        {
            return b >= 1L << 30 ? (b / (double)(1L << 30)).ToString("0.0") + " GB" : b >= 1 << 20 ? (b / (double)(1 << 20)).ToString("0.0") + " MB" : b >= 1024 ? (b / 1024) + " KB" : b + " B";
        }

        static bool demo;
        public static void UseDemo() { demo = true; }
    }
}
