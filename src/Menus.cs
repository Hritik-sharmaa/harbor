using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Harbor
{
    /// <summary>Menu model (same API as before) rendered as a styled flyout. Submenus open in place with a Back row.</summary>
    sealed class PopMenu
    {
        internal sealed class Entry
        {
            public string Text, Right;
            public Action Act;
            public bool Check, Enabled = true, IsSep, IsLabel;
            public PopMenu Sub;
        }

        internal readonly List<Entry> Entries = new List<Entry>();
        internal string Title;
        internal PopMenu Parent;

        public PopMenu Add(string text, Action a, bool check = false, bool enabled = true)
        {
            string right = null;
            int tab = text.IndexOf('\t');
            if (tab >= 0) { right = text.Substring(tab + 1); text = text.Substring(0, tab); }
            Entries.Add(new Entry { Text = text.Replace("&&", "&"), Right = right, Act = a, Check = check, Enabled = enabled });
            return this;
        }

        public PopMenu Label(string text)
        {
            Entries.Add(new Entry { Text = text, IsLabel = true });
            return this;
        }

        public PopMenu Sep()
        {
            if (Entries.Count > 0 && !Entries[Entries.Count - 1].IsSep) Entries.Add(new Entry { IsSep = true });
            return this;
        }

        public PopMenu Sub(string text, bool enabled = true)
        {
            var sub = new PopMenu { Title = text.Replace("&&", "&"), Parent = this };
            Entries.Add(new Entry { Text = sub.Title, Sub = sub, Enabled = enabled });
            return sub;
        }

        public static bool IsOpen { get { return Flyout.AnyOpen; } }

        /// <summary>Shows at a screen point; align uses the TPM_* flags (center / bottom).</summary>
        public void Show(int x, int y, uint align)
        {
            bool above = (align & N.TPM_BOTTOMALIGN) != 0;
            int mode = (align & N.TPM_CENTERALIGN) != 0 ? 1 : ((align & N.TPM_RIGHTALIGN) != 0 ? 2 : 0);
            float s = N.MonitorScale(N.MonitorFromPoint(new POINT(x, y), 2));
            MenuFlyout.Instance.Open(this, new Rectangle(x, y, 0, 0), above, mode, s);
        }

        public void ShowBelow(TopBar bar, RectangleF zone, string zoneId)
        {
            MenuFlyout.Instance.OpenBelow(this, bar, zone, zoneId);
        }

        public static string Escape(string s)
        {
            if (s == null) return "";
            return s.Length > 60 ? s.Substring(0, 57) + "..." : s;
        }
    }

    sealed class MenuFlyout : Flyout
    {
        static MenuFlyout inst;
        public static MenuFlyout Instance { get { return inst ?? (inst = new MenuFlyout()); } }

        PopMenu page;

        internal MenuFlyout() { LogicalWidth = 270; }
        internal void SetPage(PopMenu m) { page = m; }

        public void Open(PopMenu m, Rectangle anchor, bool above, int align, float s)
        {
            page = m;
            Key = "menu";
            Open(anchor, above, align, s, null);
        }

        public void OpenBelow(PopMenu m, TopBar bar, RectangleF zone, string zoneId)
        {
            if (JustClosed(zoneId + "@" + bar.Disp.Index)) return;
            page = m;
            ShowBelow(bar, zone, zoneId, 0);
        }

        protected override void Build(UI ui)
        {
            if (page == null) return;
            if (page.Parent != null)
            {
                var p = page.Parent;
                ui.Item("back", page.Title, () => { page = p; Refresh2(); }, "", keep: true);
                ui.Separator();
            }
            int i = 0;
            foreach (var e in page.Entries)
            {
                string id = "m" + (i++);
                if (e.IsSep) { ui.Separator(); continue; }
                if (e.IsLabel) { ui.Info(e.Text); continue; }
                if (e.Sub != null)
                {
                    var sub = e.Sub;
                    ui.Item(id, e.Text, () => { page = sub; Hover = null; Refresh2(); }, enabled: e.Enabled, keep: true, chevron: true);
                    continue;
                }
                ui.Item(id, e.Text, e.Act, rightText: e.Right, check: e.Check, enabled: e.Enabled && e.Act != null);
            }
        }
    }
}
