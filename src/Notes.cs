using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Harbor
{
    /// <summary>Quick notes for the island: short lines you jot down (a number, an address, a thought). Kept in %APPDATA%\Harbor\notes.txt.</summary>
    static class Notes
    {
        public sealed class Note
        {
            public string Id = Guid.NewGuid().ToString("N").Substring(0, 8), Text;
            public DateTime At = DateTime.Now;
            public bool Pinned;
        }

        static List<Note> items;
        public static event Action Changed;

        static string FilePath { get { return Path.Combine(Settings.Dir, "notes.txt"); } }

        /// <summary>Pinned first, then newest first.</summary>
        public static List<Note> All
        {
            get
            {
                if (items == null) Load();
                var l = new List<Note>(items);
                l.Sort((a, b) => a.Pinned != b.Pinned ? (a.Pinned ? -1 : 1) : b.At.CompareTo(a.At));
                return l;
            }
        }

        public static void Add(string text)
        {
            text = (text ?? "").Trim();
            if (text == "") return;
            if (items == null) Load();
            items.Add(new Note { Text = text });
            Save();
        }

        public static void Remove(Note n) { if (items != null && items.Remove(n)) Save(); }
        public static void TogglePin(Note n) { n.Pinned = !n.Pinned; Save(); }

        static void Load()
        {
            items = new List<Note>();
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (var line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    var p = line.Split('\t');
                    if (p.Length < 3) continue;
                    long ticks;
                    items.Add(new Note { Pinned = p[0] == "1", At = long.TryParse(p[1], out ticks) ? new DateTime(ticks) : DateTime.Now, Text = p[2].Replace("\\n", "\n").Replace("\\t", "\t") });
                }
            }
            catch (Exception ex) { App.Log(ex); }
        }

        static void Save()
        {
            if (!demo)
                try
                {
                    var sb = new StringBuilder();
                    foreach (var n in items) sb.Append(n.Pinned ? "1" : "0").Append('\t').Append(n.At.Ticks).Append('\t').Append(n.Text.Replace("\t", "\\t").Replace("\r", "").Replace("\n", "\\n")).Append('\n');
                    Directory.CreateDirectory(Settings.Dir);
                    File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8);
                }
                catch (Exception ex) { App.Log(ex); }
            if (Changed != null) Changed();
        }

        static bool demo;

        public static void UseDemo()
        {
            demo = true;
            var now = DateTime.Now;
            items = new List<Note>
            {
                new Note { Text = "Wi-Fi at the studio: Harbor-5G / tide-pool-42", Pinned = true, At = now.AddDays(-2) },
                new Note { Text = "Call the dentist back: 022 4567 8910", At = now.AddMinutes(-20) },
                new Note { Text = "Gift ideas: noise-cancelling headphones, a good notebook", At = now.AddHours(-3) },
            };
        }
    }
}
