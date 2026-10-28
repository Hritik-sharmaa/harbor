using System;
using System.Collections.Generic;
using System.IO;

namespace Harbor
{
    /// <summary>
    /// The island's file shelf: drop files on the notch to keep them handy, drag them out again wherever you need them.
    /// Only the paths are kept (in %APPDATA%\Harbor\shelf.txt); the files stay where they are.
    /// </summary>
    static class Shelf
    {
        const int Max = 40;
        static List<string> items;
        public static event Action Changed;

        static string FilePath { get { return Path.Combine(Settings.Dir, "shelf.txt"); } }

        public static List<string> Items { get { if (items == null) Load(); return items; } }

        static bool Exists(string p) { return File.Exists(p) || Directory.Exists(p); }

        public static void Add(IEnumerable<string> paths)
        {
            bool any = false;
            foreach (var p in paths)
            {
                if (string.IsNullOrEmpty(p) || !Exists(p)) continue;
                Items.RemoveAll(x => string.Equals(x, p, StringComparison.OrdinalIgnoreCase));
                Items.Insert(0, p);
                any = true;
            }
            if (!any) return;
            if (Items.Count > Max) Items.RemoveRange(Max, Items.Count - Max);
            Save();
        }

        public static void Remove(string p)
        {
            if (Items.RemoveAll(x => string.Equals(x, p, StringComparison.OrdinalIgnoreCase)) > 0) Save();
        }

        public static void Clear()
        {
            if (Items.Count == 0) return;
            Items.Clear();
            Save();
        }

        /// <summary>Forgets files that were moved or deleted since they were shelved.</summary>
        public static void Prune()
        {
            if (demo) return;
            if (Items.RemoveAll(p => !Exists(p)) > 0) Save();
        }

        static void Load()
        {
            items = new List<string>();
            try
            {
                if (File.Exists(FilePath))
                    foreach (var l in File.ReadAllLines(FilePath))
                        if (l.Trim() != "" && Exists(l.Trim())) items.Add(l.Trim());
            }
            catch (Exception ex) { App.Log(ex); }
        }

        static void Save()
        {
            if (!demo)
                try { Directory.CreateDirectory(Settings.Dir); File.WriteAllLines(FilePath, items.ToArray()); }
                catch (Exception ex) { App.Log(ex); }
            if (Changed != null) Changed();
        }

        static bool demo;

        /// <summary>--snapshot --demo: a few of Windows' own files stand in for yours.</summary>
        public static void UseDemo()
        {
            demo = true;
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            items = new List<string>();
            foreach (var p in new[] { Path.Combine(win, "explorer.exe"), Path.Combine(win, "win.ini"), Path.Combine(win, "Web"), Path.Combine(win, "notepad.exe") })
                if (Exists(p)) items.Add(p);
        }
    }
}
