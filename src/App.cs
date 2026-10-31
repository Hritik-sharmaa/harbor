using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Harbor
{
    /// <summary>One monitor: its menu bar and dock.</summary>
    sealed class Display
    {
        public int Index;
        public IntPtr HMon;
        public bool Primary;
        public float Scale = 1;
        public string Name;
        public TopBar Bar;
        public DockBack Back;
        public Dock Dock;
        public bool Fullscreen;
        public bool DockWanted { get { return Primary || Settings.DockAllDisplays; } }
    }

    /// <summary>Hidden message window: shell hook, theme / display / explorer-restart notifications, menu owner.</summary>
    sealed class Host : NativeWindow
    {
        readonly int taskbarCreated = N.RegisterWindowMessage("TaskbarCreated");
        public static readonly int OpenControlCenter = N.RegisterWindowMessage("Harbor_ControlCenter");
        public static readonly int OpenSettings = N.RegisterWindowMessage("Harbor_OpenSettings");

        public Host() { CreateHandle(new CreateParams { Caption = "HarborHost" }); }

        protected override void WndProc(ref Message m)
        {
            if (Tracker.HandleMessage(ref m)) return;
            if (m.Msg == N.WM_CLOSE) { App.Quit(); return; }
            if (m.Msg == OpenControlCenter) { ControlCenter.ToggleOnPrimary(); return; }
            if (m.Msg == OpenSettings) { SettingsWindow.ShowWindow(); return; }
            if (m.Msg == 0x312 /*WM_HOTKEY*/) { App.OnHotkey(m.WParam.ToInt32()); return; }
            if (m.Msg == taskbarCreated)
            {
                if (unchecked(Environment.TickCount - (int)TrayService.OwnBroadcastUntil) > 0) App.OnExplorerRestart();
            }
            else if (m.Msg == N.WM_SETTINGCHANGE)
            {
                string area = m.LParam != IntPtr.Zero ? Marshal.PtrToStringUni(m.LParam) : null;
                if (area == "ImmersiveColorSet") App.OnThemeChanged();
                else if (m.WParam.ToInt32() == 0x2F && Settings.AutoHide) App.ScheduleRelayout(); // SPI_SETWORKAREA
            }
            else if (m.Msg == N.WM_DISPLAYCHANGE || m.Msg == N.WM_DPICHANGED) App.ScheduleRelayout();
            else if (m.Msg == N.WM_POWERBROADCAST) App.OnPower(m.WParam.ToInt32(), m.LParam);
            base.WndProc(ref m);
        }
    }

    static class App
    {
        public static Host Host;
        public static readonly List<Display> Displays = new List<Display>();
        public static bool SnapshotMode;
        static bool quitting;
        static SynchronizationContext ui;
        static System.Windows.Forms.Timer relayoutTimer, trimTimer, fsTimer, idleTrim;

        public static Display PrimaryDisplay
        {
            get { foreach (var d in Displays) if (d.Primary) return d; return Displays.Count > 0 ? Displays[0] : null; }
        }

        public static void Start(string[] args)
        {
            SnapshotMode = Array.IndexOf(args, "--snapshot") >= 0;
            Theme.Refresh();
            Host = new Host();
            // screens turning back on (wake from sleep / Modern Standby) hand out new monitor handles
            Guid displayState = new Guid("6FE69556-704A-47A0-8F24-C28D936FDA47"); // GUID_CONSOLE_DISPLAY_STATE
            N.RegisterPowerSettingNotification(Host.Handle, ref displayState, 0);
            Tracker.Init(Host.Handle);
            Volume.Init();

            relayoutTimer = OneShot(300, Relayout);
            trimTimer = OneShot(1500, Trim);
            fsTimer = OneShot(60, OnWindowsMoved);

            // fan shared events out to every display
            Tracker.WindowsChanged += delegate { foreach (var d in Displays) { d.Dock.Rebuild(); d.Bar.RenderBar(); } CheckFullscreenSoon(); };
            Tracker.ForegroundChanged += delegate
            {
                foreach (var d in Displays) { d.Dock.RequestRender(); d.Bar.RenderBar(); }
                CheckFullscreenSoon();
                if (Settings.HideTaskbar) Taskbar.Enforce();
            };
            Tracker.Flash += h => { foreach (var d in Displays) d.Dock.OnFlash(h); };
            Tracker.SafetyTick += delegate
            {
                foreach (var d in Displays) d.Dock.CheckTrash();
                Taskbar.Enforce();
                OnWindowsMoved();
                TrayService.Prune();
                TrayService.RaiseAboveExplorer();
            };
            TrayService.Changed += RenderBars;
            Volume.Changed += RenderBars;
            Pomodoro.Changed += RenderBars; // the timer in the centre of the bar (once a second, only while it runs)
            Media.Changed += RenderBars;
            Radios.Changed += delegate { RenderBars(); if (Flyout.Current != null) Flyout.Current.Refresh2(); };
            Tracker.ForegroundChanged += WatchForeground;
            Tracker.ForegroundChanged += ClaudeBridge.ForegroundChanged; // a question that waited while its editor was in front
            Tracker.ForegroundChanged += delegate { foreach (var d in Displays) d.Dock.ClearAttention(); ApplyDockRules(); };
            Tracker.TitleChanged += h => { if (Look.Bool("DockBadges")) foreach (var d in Displays) d.Dock.UpdateBadges(); };
            SysInfo.Changed += RenderBars;
            BtBattery.Changed += delegate { RenderBars(); if (Flyout.Current != null) Flyout.Current.Refresh2(); };
            Radios.Changed += delegate { BtBattery.Refresh(); };
            try
            {
                NetworkChange.NetworkAddressChanged += delegate { Post(() => { TopBar.RefreshNetStatic(); RenderBars(); }); };
                NetworkChange.NetworkAvailabilityChanged += delegate { Post(() => { TopBar.RefreshNetStatic(); RenderBars(); }); };
            }
            catch { }
            TopBar.RefreshNetStatic();

            // hide the taskbar first so our app-bars don't stack on top of its reserved space
            if (Settings.HideTaskbar && !SnapshotMode) Taskbar.Apply(true);
            BuildDisplays();
            ui = SynchronizationContext.Current;

            if (SnapshotMode) { RunSnapshot(args); return; }

            if (Settings.TrayIcons) TrayService.Start();
            Radios.Init();
            Media.Init();
            SysInfo.Init();
            BtBattery.Init();
            RegisterHotkeys();
            Banners.Apply();
            ScreenTime.Init();
            Reminders.Init();
            ClipboardHistory.Init();
            Updates.Init();
            LiveActivity.Init();
            Timers.Init();
            Timers.Changed += RenderBars; // the countdown in the centre of the menu bar
            CalendarFeed.Init();
            Downloads.Init();
            Breaks.Init();
            LastPlayed.Init();
            ClaudeBridge.Start();
            ClaudeBridge.EnsureHooks(Application.ExecutablePath); // e.g. a new editor or account profile since last time
            ClaudeBridge.Asked += Island.Attention;
            WatchForeground();
            foreach (var d in Displays) d.Bar.RenderBar();
            ScheduleRelayoutAfter(1500); // the taskbar's auto-hide switch is asynchronous

            Autostart.Sync();
            // launched by hand (Start menu / double-click) rather than at login: show the app window
            if (Array.IndexOf(args, "--startup") < 0) { var t = OneShot(400, SettingsWindow.ShowWindow); t.Start(); }
            ScheduleTrim();
            var settled = OneShot(20000, Trim); // again once icons, tray and async sources have finished loading
            settled.Tick += delegate { settled.Dispose(); };
            settled.Start();
            idleTrim = new System.Windows.Forms.Timer { Interval = 10 * 60 * 1000 };
            idleTrim.Tick += delegate { Trim(); };
            idleTrim.Start();
        }

        static string MonitorSignature()
        {
            var parts = new List<string>();
            foreach (var s in Screen.AllScreens) parts.Add(s.DeviceName + s.Bounds + s.Primary);
            parts.Sort();
            return string.Join(";", parts.ToArray());
        }

        static string builtSignature;

        static void BuildDisplays()
        {
            builtSignature = MonitorSignature();
            int i = 0;
            foreach (var s in Screen.AllScreens)
            {
                var b = s.Bounds;
                var d = new Display
                {
                    Index = i++,
                    Primary = s.Primary,
                    Name = s.DeviceName,
                    HMon = N.MonitorFromPoint(new POINT(b.X + b.Width / 2, b.Y + b.Height / 2), 2),
                };
                d.Scale = N.MonitorScale(d.HMon);
                d.Bar = new TopBar(d);
                d.Back = new DockBack();
                d.Dock = new Dock(d.Back, d);
                var h1 = d.Bar.Handle; var h2 = d.Back.Handle; var h3 = d.Dock.Handle;
                Displays.Add(d);
            }
            // primary first so its app-bars claim space before the others
            Displays.Sort((a, b) => b.Primary.CompareTo(a.Primary));
            foreach (var d in Displays)
            {
                d.Bar.Reposition();
                ApplyDock(d);
            }
        }

        static void ApplyDock(Display d)
        {
            if (d.DockWanted)
            {
                d.Dock.Reconfigure();
                if (!SnapshotMode && !d.Fullscreen) N.ShowWindow(d.Dock.Handle, N.SW_SHOWNOACTIVATE);
                d.Dock.Render();
            }
            else
            {
                d.Dock.Unregister();
                N.ShowWindow(d.Dock.Handle, N.SW_HIDE);
                d.Back.Conceal();
            }
        }

        static void TeardownDisplays()
        {
            foreach (var d in Displays)
            {
                try { d.Bar.Unregister(); d.Dock.Unregister(); } catch { }
                d.Dock.Close(); d.Back.Close(); d.Bar.Close();
                d.Dock.Dispose(); d.Back.Dispose(); d.Bar.Dispose();
            }
            Displays.Clear();
        }

        static System.Windows.Forms.Timer OneShot(int ms, Action a)
        {
            var t = new System.Windows.Forms.Timer { Interval = ms };
            t.Tick += delegate { t.Stop(); a(); };
            return t;
        }

        static void Restart(System.Windows.Forms.Timer t) { if (t == null || shuttingDown) return; t.Stop(); t.Start(); }

        public static void ScheduleRelayout() { Restart(relayoutTimer); }

        public static void ScheduleRelayoutAfter(int ms)
        {
            var t = OneShot(ms, Relayout);
            t.Tick += delegate { t.Dispose(); };
            t.Start();
        }

        static bool retryPending;

        /// <summary>One shared retry, however many bars and docks ask for it.</summary>
        public static void RetryRelayoutSoon()
        {
            if (retryPending) return;
            retryPending = true;
            var t = OneShot(1000, () => { retryPending = false; Relayout(); });
            t.Tick += delegate { t.Dispose(); };
            t.Start();
        }
        public static void ScheduleTrim() { Restart(trimTimer); }
        public static void CheckFullscreenSoon() { Restart(fsTimer); }

        public static void Post(Action a)
        {
            if (a == null) return;
            if (ui != null) ui.Post(_ => { try { a(); } catch (Exception ex) { Log(ex); } }, null);
        }

        public static void RenderBars() { foreach (var d in Displays) d.Bar.RenderBar(); }

        /// <summary>Wake from sleep, or the screens turning back on: re-measure once they've settled, and again a bit later.</summary>
        public static void OnPower(int code, IntPtr data)
        {
            bool wake = code == 0x7 || code == 0x12; // PBT_APMRESUMESUSPEND, PBT_APMRESUMEAUTOMATIC
            // PBT_POWERSETTINGCHANGE for GUID_CONSOLE_DISPLAY_STATE: POWERBROADCAST_SETTING { Guid; DWORD len; DWORD state }, 1 = on
            if (code == 0x8013 && data != IntPtr.Zero) wake = Marshal.ReadInt32(data, 20) == 1;
            if (!wake) return;
            ScheduleRelayoutAfter(1000);
            ScheduleRelayoutAfter(4000);
        }

        /// <summary>
        /// Monitor handles change when screens power off and on (sleep), even though their names and sizes don't.
        /// Looks every display's handle up again; false if a screen can't be found (then everything is rebuilt).
        /// </summary>
        static bool RefreshMonitors()
        {
            var screens = Screen.AllScreens;
            foreach (var d in Displays)
            {
                var s = Array.Find(screens, q => q.DeviceName == d.Name);
                if (s == null || s.Bounds.Width <= 0) return false;
                var b = s.Bounds;
                d.HMon = N.MonitorFromPoint(new POINT(b.X + b.Width / 2, b.Y + b.Height / 2), 2);
                if (N.MonitorInfo(d.HMon).rcMonitor.Width <= 0) return false;
            }
            return true;
        }

        public static void Relayout()
        {
            if (MonitorSignature() != builtSignature || !RefreshMonitors())
            {
                TeardownDisplays();
                BuildDisplays();
                CheckFullscreen();
                return;
            }
            foreach (var d in Displays)
            {
                d.Scale = N.MonitorScale(d.HMon);
                d.Bar.Reposition();
                ApplyDock(d);
            }
        }

        static System.Windows.Forms.Timer liveTimer;

        /// <summary>Applies look changes while the user drags sliders / picks colours (debounced, no taskbar churn).</summary>
        public static void ApplyLive()
        {
            if (liveTimer == null)
                liveTimer = OneShot(70, () =>
                {
                    Settings.Save();
                    Theme.Refresh();
                    RepaintPanels();
                    SysInfo.Apply();
                    RegisterHotkeys();
                    Banners.Apply();
                    foreach (var d in Displays)
                    {
                        d.Bar.ApplyBlur();
                        d.Bar.Reposition();
                        ApplyDock(d);
                    }
                    ApplyDockRules();
                });
            Restart(liveTimer);
        }

        public static void ApplySettings()
        {
            Settings.Save();
            Theme.Refresh();
            RepaintPanels();
            Taskbar.Apply(Settings.HideTaskbar);
            ScheduleRelayoutAfter(1500);
            Autostart.Sync();
            if (Settings.TrayIcons) TrayService.Start(); else TrayService.Stop();
            foreach (var d in Displays)
            {
                d.Bar.ApplyBlur();
                d.Bar.Reposition();
                ApplyDock(d);
            }
        }

        public static void TogglePin(string path)
        {
            if (Settings.Pinned.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)) == 0)
                Settings.Pinned.Add(path);
            Settings.Save();
            foreach (var d in Displays) d.Dock.Rebuild();
        }

        public static void RebuildDocks() { foreach (var d in Displays) d.Dock.Rebuild(); }

        /// <summary>Focus switched on or off: the dock may tuck away (or come back), the bar shows it.</summary>
        public static void OnFocusChanged()
        {
            foreach (var d in Displays) { ApplyDock(d); d.Bar.RenderBar(); }
            ScheduleRelayoutAfter(300);
        }

        /// <summary>"Hide Dock While Using This App" rules.</summary>
        public static void ApplyDockRules()
        {
            bool hide = false;
            IntPtr fg = Tracker.Foreground;
            if (IsTransient(N.GetForegroundWindow())) return;   // screenshot overlays etc. don't change anything
            if (fg != IntPtr.Zero && IsDesktop(fg)) { foreach (var d in Displays) d.Dock.SetRuleHidden(false); return; } // the desktop always shows the dock
            if (Settings.HideDockFor.Count > 0 && fg != IntPtr.Zero)
            {
                var w = Tracker.Describe(fg);
                hide = w != null && Settings.HideDockFor.Contains(w.Key);
            }
            foreach (var d in Displays) d.Dock.SetRuleHidden(hide);
        }

        // ---- global shortcuts: Alt+Win+N notifications, Alt+Win+C control center, Alt+Win+D dock auto-hide
        static bool hotkeysOn;

        static void RegisterHotkeys()
        {
            if (Host == null) return;
            bool want = Look.Bool("Hotkeys");
            if (want == hotkeysOn) return;
            const uint mods = 0x1 | 0x8 | 0x4000; // Alt + Win, no auto-repeat
            if (want)
            {
                N.RegisterHotKey(Host.Handle, 1, mods, (uint)'N');
                N.RegisterHotKey(Host.Handle, 2, mods, (uint)'C');
                N.RegisterHotKey(Host.Handle, 3, mods, (uint)'D');
                N.RegisterHotKey(Host.Handle, 4, mods, 0x20); // Space: the island, from the keyboard
                N.RegisterHotKey(Host.Handle, 5, mods, (uint)'V'); // the island's clipboard
            }
            else for (int i = 1; i <= 5; i++) N.UnregisterHotKey(Host.Handle, i);
            hotkeysOn = want;
        }

        public static void OnHotkey(int id)
        {
            var p = PrimaryDisplay;
            if (p == null) return;
            switch (id)
            {
                case 1: { var r = p.Bar.ZoneRect("clock"); NotificationCenter.Toggle(p.Bar, r.Width > 0 ? r : new RectangleF(p.Bar.ScreenBounds.Width - 200, 0, 100, 30)); break; }
                case 2: ControlCenter.ToggleOnPrimary(); break;
                case 3: Settings.AutoHide = !Settings.AutoHide; ApplySettings(); break;
                case 4: Island.ToggleFromKeyboard(null); break;
                case 5: Island.ToggleFromKeyboard("clipboard"); break;
            }
        }

        public static void Restart()
        {
            EmergencyRestore();
            System.Diagnostics.Process.Start(Application.ExecutablePath, "--startup --wait");
            Environment.Exit(0);
        }

        /// <summary>Removes Harbor: auto-start, Start-menu entry, Apps & features entry, and the installed files.</summary>
        public static void Uninstall()
        {
            if (!TopBar.Confirm("Uninstall Harbor? Your Windows taskbar will come back. Your settings file is kept.")) return;
            Installer.Remove();
            EmergencyRestore();
            Installer.DeleteFilesAfterExit();
            Environment.Exit(0);
        }

        public static void HideFromDock(string key)
        {
            if (!Settings.HiddenApps.Contains(key)) Settings.HiddenApps.Add(key);
            Settings.Save();
            foreach (var d in Displays) d.Dock.Rebuild();
        }

        static System.Windows.Forms.Timer themeTimer;

        public static void OnThemeChanged()
        {
            Repaint();
            // Windows can broadcast the change before it has written the new mode: look again once it settles
            if (themeTimer == null) themeTimer = OneShot(800, () => { bool was = Theme.Dark; Theme.Refresh(); if (was != Theme.Dark) Repaint(); });
            Restart(themeTimer);
        }

        static void Repaint()
        {
            Theme.Refresh();
            foreach (var d in Displays) { d.Bar.RenderBar(); if (d.DockWanted) d.Dock.Reconfigure(); }
            RepaintPanels();
        }

        /// <summary>Open panels and the settings window redraw in the new light/dark palette.</summary>
        static void RepaintPanels()
        {
            if (Flyout.Current != null) Flyout.Current.Refresh2();
            SettingsWindow.Repaint();
        }

        public static void OnExplorerRestart()
        {
            foreach (var d in Displays) { d.Bar.Unregister(); d.Dock.ResetAppBar(); }
            Tracker.ReRegisterShellHook();
            Taskbar.OnExplorerRestart();
            TrayService.Reclaim();
            ScheduleRelayout();
        }

        // Fullscreen enter/exit (F11, video players, games) often happens without a focus change, so we
        // listen for size/position changes of the foreground app's windows only. Event-driven, no polling.
        static WinEventDelegate locProc;
        static IntPtr locHook;
        static uint locPid;

        static void WatchForeground()
        {
            if (shuttingDown) return;
            IntPtr fg = N.GetForegroundWindow();
            uint pid;
            N.GetWindowThreadProcessId(fg, out pid);
            if (pid == 0 || pid == locPid || Tracker.IsSelf(fg)) return;
            if (locHook != IntPtr.Zero) N.UnhookWinEvent(locHook);
            if (locProc == null)
                locProc = (hook, ev, hwnd, idObject, idChild, thread, time) =>
                {
                    if (idObject == 0 && idChild == 0 && hwnd == N.GetForegroundWindow()) CheckFullscreenSoon();
                };
            locPid = pid;
            locHook = N.SetWinEventHook(0x800B, 0x800B, IntPtr.Zero, locProc, pid, 0, N.WINEVENT_OUTOFCONTEXT | N.WINEVENT_SKIPOWNPROCESS);
        }

        static readonly HashSet<string> transient = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // screenshot tools and shell overlays: they cover the screen for a moment but aren't "the app you're using"
            "screenclippinghost.exe", "snippingtool.exe", "screensketch.exe", "sharex.exe", "lightshot.exe", "greenshot.exe",
            "shellexperiencehost.exe", "startmenuexperiencehost.exe", "searchhost.exe", "searchapp.exe", "textinputhost.exe",
            "lockapp.exe", "logonui.exe", "powertoys.screenruler.exe", "powertoys.powerlauncher.exe", "raycast.exe",
        };

        /// <summary>True for screenshot overlays, Start, Search, IME... (state should not change because of them).</summary>
        public static bool IsTransient(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return true;
            if (Tracker.IsSelf(hwnd)) return true;
            var w = Tracker.Describe(hwnd);
            if (w == null || w.Exe == null) return false;
            return transient.Contains(System.IO.Path.GetFileName(w.Exe));
        }

        public static bool IsDesktop(IntPtr hwnd)
        {
            string cls = N.ClassOf(hwnd);
            return cls == "Progman" || cls == "WorkerW";
        }

        /// <summary>Windows opened / closed / moved / focused: fullscreen + Smart Hide checks.</summary>
        static void OnWindowsMoved()
        {
            CheckFullscreen();
            if (!IsTransient(N.GetForegroundWindow())) foreach (var d in Displays) d.Dock.UpdateOverlap();
        }

        static readonly EnumWindowsProc fsEnum = FullscreenEnum;
        static Dictionary<IntPtr, bool> fsDecided;
        static int fsLeft;

        /// <summary>
        /// Per-monitor fullscreen detection: for every screen, look at the top-most real window on it.
        /// (Checking only the focused window made the bar reappear over a fullscreen video
        /// as soon as you clicked something on your other screen.)
        /// </summary>
        static void CheckFullscreen()
        {
            fsDecided = new Dictionary<IntPtr, bool>();
            fsLeft = Displays.Count;
            N.EnumWindows(fsEnum, IntPtr.Zero); // z-order: top-most first
            foreach (var d in Displays)
            {
                bool full;
                fsDecided.TryGetValue(d.HMon, out full);
                if (full == d.Fullscreen) continue;
                d.Fullscreen = full;
                d.Bar.SetHidden(full);
                if (d.DockWanted) d.Dock.SetHidden(full);
            }
        }

        static bool FullscreenEnum(IntPtr h, IntPtr lp)
        {
            if (fsLeft <= 0) return false;
            if (!N.IsWindowVisible(h) || N.IsIconic(h) || N.IsCloaked(h)) return true;
            if (Tracker.IsSelf(h) || IsDesktop(h)) return true;
            string cls = N.ClassOf(h);
            if (cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd") return true;
            int ex = N.GetWindowLong(h, N.GWL_EXSTYLE);
            if ((ex & N.WS_EX_TRANSPARENT) != 0) return true;          // click-through overlays (game FPS counters...)
            if (IsTransient(h)) return true;                          // screenshot overlays, Start, Search...
            RECT r;
            if (!N.GetWindowRect(h, out r) || r.Width <= 0 || r.Height <= 0) return true;
            IntPtr mon = N.MonitorFromWindow(h, 2);
            if (fsDecided.ContainsKey(mon)) return true;
            RECT m = N.MonitorInfo(mon).rcMonitor;
            bool covers = r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom;
            if (covers)
            {
                int style = N.GetWindowLong(h, N.GWL_STYLE);
                bool normalMaximized = N.IsZoomed(h) && (style & N.WS_CAPTION) == N.WS_CAPTION;
                fsDecided[mon] = !normalMaximized;
                fsLeft--;
                return true;
            }
            // a big normal window is on top of this screen -> not fullscreen; small popups/tooltips don't decide
            long area = (long)Math.Min(r.Right, m.Right) - Math.Max(r.Left, m.Left);
            area *= Math.Max(0, Math.Min(r.Bottom, m.Bottom) - Math.Max(r.Top, m.Top));
            if (area > 0 && area >= (long)m.Width * m.Height / 4)
            {
                fsDecided[mon] = false;
                fsLeft--;
            }
            return true;
        }

        static IntPtr lastRealForeground;

        public static WinInfo ActiveApp()
        {
            IntPtr fg = Tracker.Foreground;
            // screenshot tools, Raycast, Start... don't replace the app you're actually using
            if (fg != IntPtr.Zero && IsTransient(fg))
            {
                if (lastRealForeground != IntPtr.Zero && N.IsWindow(lastRealForeground)) fg = lastRealForeground;
            }
            else lastRealForeground = fg;
            if (fg == IntPtr.Zero || !N.IsWindow(fg)) return null;
            string cls = N.ClassOf(fg);
            if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd") return null;
            var w = Tracker.Find(fg);
            if (w != null) return w;
            uint pid;
            N.GetWindowThreadProcessId(fg, out pid);
            foreach (var x in Tracker.Windows) if (x.Pid == pid) return x;
            return Tracker.Describe(fg); // popups, dialogs, overlays...
        }

        public static string ActiveAppName()
        {
            var w = ActiveApp();
            if (w == null) return "File Explorer";
            if (w.WebAppName != null) return w.WebAppName;
            if (w.Aumid != null) return ShellIcons.DisplayName(@"shell:AppsFolder\" + w.Aumid);
            if (w.Exe != null) return ShellIcons.DisplayName(w.Exe);
            string t = N.TitleOf(w.Hwnd);
            return t.Length > 30 ? t.Substring(0, 30) : (t.Length > 0 ? t : "File Explorer");
        }

        public static void BuildSettingsMenu(PopMenu m)
        {
            var size = m.Sub("Dock Size");
            int[] sizes = { 32, 36, 40, 48, 56 };
            string[] names = { "Tiny", "Small", "Medium", "Large", "Huge" };
            for (int i = 0; i < sizes.Length; i++)
            {
                int v = sizes[i];
                size.Add(names[i], () => { Settings.IconSize = v; ApplySettings(); }, Settings.IconSize == v);
            }
            var style = m.Sub("Icon Style");
            style.Add("macOS (WhiteSur)", () => { Settings.IconStyle = "whitesur"; ApplySettings(); }, Settings.IconStyle == "whitesur");
            style.Add("Glass", () => { Settings.IconStyle = "tiles"; ApplySettings(); }, Settings.IconStyle == "tiles");
            style.Add("Original", () => { Settings.IconStyle = "original"; ApplySettings(); }, Settings.IconStyle == "original");
            m.Add("Magnification", () => { Settings.Magnify = !Settings.Magnify; ApplySettings(); }, Settings.Magnify);
            var mag = m.Sub("Magnification Amount", Settings.Magnify);
            int[] amounts = { 130, 150, 175, 200 };
            string[] an = { "Subtle", "Normal", "Big", "Huge" };
            for (int i = 0; i < amounts.Length; i++)
            {
                int v = amounts[i];
                mag.Add(an[i], () => { Settings.MagnifyPercent = v; ApplySettings(); }, Settings.MagnifyPercent == v);
            }
            m.Add("Auto-hide Dock", () => { Settings.AutoHide = !Settings.AutoHide; ApplySettings(); }, Settings.AutoHide);
            m.Add("Show Open Apps in Dock", () => { Settings.ShowRunning = !Settings.ShowRunning; ApplySettings(); }, Settings.ShowRunning);
            m.Add("Show Dock on All Displays", () => { Settings.DockAllDisplays = !Settings.DockAllDisplays; ApplySettings(); }, Settings.DockAllDisplays);
            m.Sep();
            m.Add("Show Menu Bar", () => { Settings.ShowTopBar = !Settings.ShowTopBar; ApplySettings(); }, Settings.ShowTopBar);
            m.Add("Show Menu Bar on All Displays", () => { Settings.BarAllDisplays = !Settings.BarAllDisplays; ApplySettings(); }, Settings.BarAllDisplays);
            m.Add("Tray Icons Button", () => { Settings.TrayIcons = !Settings.TrayIcons; ApplySettings(); }, Settings.TrayIcons);
            m.Add("Hide Windows Taskbar", () => { Settings.HideTaskbar = !Settings.HideTaskbar; ApplySettings(); }, Settings.HideTaskbar);
            m.Add("Translucent Blur", () => { Settings.Blur = !Settings.Blur; ApplySettings(); }, Settings.Blur);
            var ap = m.Sub("Appearance");
            ap.Add("Match Windows", () => { Settings.ThemeMode = "auto"; ApplySettings(); }, Settings.ThemeMode == "auto");
            ap.Add("Light", () => { Settings.ThemeMode = "light"; ApplySettings(); }, Settings.ThemeMode == "light");
            ap.Add("Dark", () => { Settings.ThemeMode = "dark"; ApplySettings(); }, Settings.ThemeMode == "dark");
            m.Sep();
            m.Add("Launch at Login", () => { Settings.LaunchAtLogin = !Settings.LaunchAtLogin; ApplySettings(); }, Settings.LaunchAtLogin);
            m.Add("Open Settings File...", () => Launcher.Run(Path.Combine(Settings.Dir, "settings.ini")));
            m.Add("Quit Harbor", Quit);
        }

        public static void Trim()
        {
            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                N.SetProcessWorkingSetSize(N.GetCurrentProcess(), new IntPtr(-1), new IntPtr(-1));
            }
            catch { }
        }

        public static void Quit()
        {
            if (quitting) return;
            quitting = true;
            ClipboardHistory.Stop(); // its copied images are only kept while Harbor runs
            EmergencyRestore();
            Application.Exit();
        }

        /// <summary>Gives the screen back to Windows: tray icons, taskbar, app-bar space.</summary>
        static bool shuttingDown;

        public static void EmergencyRestore()
        {
            // window events can still arrive while .NET shuts down; touching a timer then crashes the process
            shuttingDown = true;
            if (locHook != IntPtr.Zero) { N.UnhookWinEvent(locHook); locHook = IntPtr.Zero; }
            try { Banners.RestoreWindows(); } catch { }
            try { ScreenTime.Shutdown(); } catch { }
            try { TrayService.Stop(); } catch { }
            try { Gamma.ResetAll(); } catch { }
            try { Taskbar.Apply(false); } catch { }
            foreach (var d in Displays)
            {
                try { d.Bar.Unregister(); } catch { }
                try { d.Dock.Unregister(); } catch { }
            }
            try { Tracker.Shutdown(); } catch { }
        }

        public static void Log(Exception ex)
        {
            if (ex == null) return;
            try
            {
                Directory.CreateDirectory(Settings.Dir);
                string f = Path.Combine(Settings.Dir, "error.log");
                if (File.Exists(f) && new FileInfo(f).Length > 256 * 1024) File.Delete(f);
                File.AppendAllText(f, DateTime.Now.ToString("s") + "  " + ex + Environment.NewLine + Environment.NewLine);
            }
            catch { }
        }

        static void RunSnapshot(string[] args)
        {
            int i = Array.IndexOf(args, "--snapshot");
            string dir = i + 1 < args.Length ? args[i + 1] : ".";
            int hover = 2;
            int hi = Array.IndexOf(args, "--hover");
            if (hi >= 0 && hi + 1 < args.Length) int.TryParse(args[hi + 1], out hover);
            Directory.CreateDirectory(dir);
            bool demo = Array.IndexOf(args, "--demo") >= 0;
            ClaudeBridge.DemoQuestion = Array.IndexOf(args, "--question") >= 0;
            if (demo) UseDemoData();
            if (Array.IndexOf(args, "--light") >= 0) { Settings.ThemeMode = "light"; Theme.Refresh(); }
            else if (Array.IndexOf(args, "--dark") >= 0) { Settings.ThemeMode = "dark"; Theme.Refresh(); }
            var p = PrimaryDisplay;
            var dock = p.Dock.Snapshot(hover);
            var bar = p.Bar.Snapshot();
            Compose(bar, Path.Combine(dir, "bar.png"));
            Compose(dock, Path.Combine(dir, "dock.png"));
            IconSheet(Path.Combine(dir, "icons.png"));
            using (var bn = Banners.Snapshot(p)) Compose(bn, Path.Combine(dir, "banner.png"));
            float s = p.Scale;
            var cc = new ControlCenter { bar = p.Bar };
            var shots = new List<KeyValuePair<string, Flyout>>
            {
                new KeyValuePair<string, Flyout>("f_cc", cc),
                new KeyValuePair<string, Flyout>("f_wifi", new WifiFlyout()),
                new KeyValuePair<string, Flyout>("f_sound", new SoundFlyout()),
                new KeyValuePair<string, Flyout>("f_cal", new CalendarFlyout()),
                new KeyValuePair<string, Flyout>("f_media", new MediaFlyout()),
            };
            if (!demo) shots.Add(new KeyValuePair<string, Flyout>("f_bt", new BluetoothFlyout())); // lists real paired device names
            var menu = new PopMenu();
            BuildSettingsMenu(menu);
            var mf = new MenuFlyout();
            mf.SetPage(menu);
            shots.Add(new KeyValuePair<string, Flyout>("f_menu", mf));
            shots.Add(new KeyValuePair<string, Flyout>("f_nc", new NotificationCenter()));
            shots.Add(new KeyValuePair<string, Flyout>("f_sys", new SystemFlyout()));
            foreach (var t in new[] { "dashboard", "media", "pomodoro", "weather", "screentime", "reminders", "clipboard", "shelf", "ai", "usage", "calendar", "timers", "notes", "downloads" })
                shots.Add(new KeyValuePair<string, Flyout>("island_" + t, Island.ForSnapshot(t)));
            foreach (int pg in new[] { 0, 1, 2, 3, 4, 6 }) shots.Add(new KeyValuePair<string, Flyout>("w_settings" + pg, new SettingsWindow { Page = pg }));
            // give async sources (radios, media, bluetooth list) a moment to arrive
            var wait = new System.Windows.Forms.Timer { Interval = 2500 };
            wait.Tick += delegate
            {
                wait.Stop();
                foreach (var kv in shots)
                    try { using (var b = kv.Value.Snap(s)) Compose(b, Path.Combine(dir, kv.Key + ".png")); }
                    catch (Exception ex) { Log(ex); }
                try { using (var c = cc.Snap(s)) Hero(p, bar, dock, c, Path.Combine(dir, "hero.png")); }
                catch (Exception ex) { Log(ex); }
                Environment.Exit(0);
            };
            Radios.Init();
            Media.Init();
            wait.Start();
        }

        /// <summary>--snapshot: original vs WhiteSur for the apps in the Start menu (visual check).</summary>
        static void IconSheet(string file)
        {
            var paths = new List<string>();
            foreach (var w in Tracker.Windows)
            {
                string p = w.WebAppIcon ?? (w.Aumid != null ? @"shell:AppsFolder\" + w.Aumid : w.Exe);
                if (p != null && !paths.Contains(p)) paths.Add(p);
            }
            paths.AddRange(Settings.Pinned);
            foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), Environment.GetFolderPath(Environment.SpecialFolder.Programs) })
                try { foreach (var l in Directory.GetFiles(root, "*.lnk", SearchOption.AllDirectories)) if (paths.Count < 40 && !l.ToLowerInvariant().Contains("uninstall")) paths.Add(l); } catch { }
            int px = 64, cols = 10, cell = 84;
            int rows = (paths.Count + cols - 1) / cols;
            using (var sheet = new Bitmap(cols * cell, rows * cell * 2 + 10, PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(sheet))
            {
                g.Clear(Color.FromArgb(255, 232, 232, 236));
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                for (int i = 0; i < paths.Count; i++)
                {
                    var b = ShellIcons.LoadIcons(paths[i], px);
                    if (b == null) continue;
                    int x = (i % cols) * cell + 10, y = (i / cols) * cell * 2 + 6;
                    g.DrawImage(b[0], x, y, px, px);
                    // real pipeline: big (magnified) and small (resting) must look identical
                    string key;
                    var ws = IconCache.Acquire(paths[i], ShellIcons.DisplayName(paths[i]), px, 40, "whitesur", out key);
                    g.DrawImage(ws[0], x, y + cell, px, px);
                    g.DrawImage(ws[1], x + px - 22, y + cell + px - 22, 40, 40);
                    b[0].Dispose();
                }
                sheet.Save(file, ImageFormat.Png);
            }
        }

        /// <summary>Sample data so README screenshots never show the user's network, music or notifications.</summary>
        static void UseDemoData()
        {
            Look.UseDefaults();
            Wifi.Demo = true;
            Wifi.Ssid = "Home Wi-Fi";
            SysInfo.Demo = true;
            SysInfo.Cpu = 9;
            SysInfo.Ram = 46;
            SysInfo.RamUsedGB = 7.4;
            SysInfo.RamTotalGB = 16;
            SysInfo.TempC = 47;
            SysInfo.CpuHistory.Clear();
            for (int i = 0; i < 60; i++) SysInfo.CpuHistory.Add(6 + (int)(10 * Math.Abs(Math.Sin(i * 0.37))) + (i % 7 == 0 ? 14 : 0));
            Brightness.Demo = true;
            Wifi.Signal = 92;
            Volume.DeviceName = "Speakers";
            Volume.Level = 0.62f;
            Volume.Muted = false;
            Media.Demo = true;
            Media.Title = "Midnight City";
            Media.Artist = "M83";
            Media.SourceApp = "Spotify.exe";
            Media.Playing = true;
            Media.DemoTimeline(TimeSpan.FromSeconds(72), TimeSpan.FromSeconds(243));
            BtBattery.Demo = true;
            BtBattery.ConnectedName = "AirPods Pro";
            BtBattery.ConnectedLevel = 85;
            NotificationCenter.Demo = true;
            Weather.UseDemo();
            ScreenTime.UseDemo();
            Pomodoro.UseDemo();
            Reminders.UseDemo();
            ClaudeHistory.UseDemo();
            ClaudeUsage.UseDemo();
            ClipboardHistory.UseDemo();
            AudioOutputs.Demo = true;
            Shelf.UseDemo();
            Timers.UseDemo();
            CalendarFeed.UseDemo();
            Notes.UseDemo();
            Downloads.UseDemo();
            ClaudeBridge.UseDemo();
            foreach (var d in Displays) d.Bar.RenderBar();
        }

        /// <summary>--snapshot: the whole desktop (wallpaper, bar, dock, Control Center) for the README.</summary>
        static void Hero(Display d, Bitmap bar, Bitmap dock, Bitmap cc, string file)
        {
            int w = Math.Max(bar.Width, dock.Width), h = Math.Max(Screen.PrimaryScreen.Bounds.Height, bar.Height + cc.Height + dock.Height + 40);
            using (var bg = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(bg))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var br = new LinearGradientBrush(new Rectangle(0, 0, w, h), Color.FromArgb(255, 34, 52, 120), Color.FromArgb(255, 150, 70, 140), 35f))
                    g.FillRectangle(br, 0, 0, w, h);
                // soft colour blobs, like the macOS wallpapers
                var blobs = new[]
                {
                    new { X = 0.18f, Y = 0.75f, R = 0.55f, C = Color.FromArgb(170, 255, 140, 90) },
                    new { X = 0.80f, Y = 0.25f, R = 0.50f, C = Color.FromArgb(150, 80, 170, 255) },
                    new { X = 0.55f, Y = 0.95f, R = 0.45f, C = Color.FromArgb(140, 240, 90, 160) },
                };
                foreach (var b in blobs)
                {
                    float r = b.R * w, cx = b.X * w, cy = b.Y * h;
                    using (var path = new GraphicsPath())
                    {
                        path.AddEllipse(cx - r, cy - r, 2 * r, 2 * r);
                        using (var pb = new PathGradientBrush(path) { CenterColor = b.C, SurroundColors = new[] { Color.FromArgb(0, b.C) } })
                            g.FillPath(pb, path);
                    }
                }
                g.DrawImage(bar, 0, 0, bar.Width, bar.Height);
                g.DrawImage(dock, (w - dock.Width) / 2, h - dock.Height, dock.Width, dock.Height);
                // the live panel is acrylic with rounded corners; the snapshot is a plain rectangle
                int m = (int)(10 * d.Scale);
                var pr = new RectangleF(w - cc.Width - m, bar.Height + m / 2, cc.Width, cc.Height);
                for (int i = 12; i > 0; i -= 2)
                    using (var sp = Gfx.RoundRect(new RectangleF(pr.X - i, pr.Y - i + 6, pr.Width + 2 * i, pr.Height + 2 * i), 16 * d.Scale + i))
                    using (var sb = new SolidBrush(Color.FromArgb(10, 0, 0, 0)))
                        g.FillPath(sb, sp);
                using (var clip = Gfx.RoundRect(pr, 16 * d.Scale))
                using (var tb = new TextureBrush(cc))
                {
                    tb.TranslateTransform(pr.X, pr.Y);
                    g.FillPath(tb, clip);
                    using (var pen = new Pen(Color.FromArgb(40, 255, 255, 255), 1)) g.DrawPath(pen, clip);
                }
                bg.Save(file, ImageFormat.Png);
            }
        }

        static void Compose(Bitmap layer, string file)
        {
            using (var bg = new Bitmap(layer.Width, layer.Height, PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(bg))
            {
                using (var br = new LinearGradientBrush(new Rectangle(0, 0, bg.Width, bg.Height),
                    Color.FromArgb(255, 58, 96, 168), Color.FromArgb(255, 190, 120, 160), 20f))
                    g.FillRectangle(br, 0, 0, bg.Width, bg.Height);
                g.DrawImage(layer, 0, 0, layer.Width, layer.Height);
                bg.Save(file, ImageFormat.Png);
            }
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // Claude Code runs us as a hook: forward the event and exit, before anything else starts
            if (Array.IndexOf(args, "--claude-hook") >= 0) { ClaudeBridge.RunHook(); return; }
            // developer check: add / remove Harbor's hooks in a copy of a settings file
            int ht = Array.IndexOf(args, "--claude-hooks-test");
            if (ht >= 0 && ht + 2 < args.Length) { ClaudeBridge.SetHooks(args[ht + 2] == "on", Application.ExecutablePath, args[ht + 1]); return; }
            int wt = Array.IndexOf(args, "--weather-test");
            if (wt >= 0 && wt + 2 < args.Length) { Weather.Test(args[wt + 1], args[wt + 2]); return; }
            int ot = Array.IndexOf(args, "--outputs-test");
            if (ot >= 0 && ot + 1 < args.Length)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var o in AudioOutputs.List()) sb.AppendLine((o.Default ? "* " : "  ") + o.Name);
                File.WriteAllText(args[ot + 1], sb.Length == 0 ? "(no outputs)" : sb.ToString());
                return;
            }
            int ct = Array.IndexOf(args, "--calendar-test");
            if (ct >= 0 && ct + 2 < args.Length)
            {
                var sb = new System.Text.StringBuilder();
                try { foreach (var e in CalendarFeed.Parse(File.ReadAllText(args[ct + 1]), DateTime.Today.AddDays(-1), DateTime.Today.AddDays(8))) sb.AppendLine(e.Start.ToString("ddd yyyy-MM-dd HH:mm") + " - " + e.End.ToString("HH:mm") + (e.AllDay ? " (all day)" : "") + "  " + e.Title + (e.Link != null ? "  [join " + e.Link + "]" : "")); }
                catch (Exception ex) { sb.AppendLine("ERROR " + ex); }
                File.WriteAllText(args[ct + 2], sb.ToString());
                return;
            }
            int ut = Array.IndexOf(args, "--update-test");
            if (ut >= 0 && ut + 2 < args.Length) { Updates.Test(args[ut + 1], args[ut + 2]); return; }
            int lt = Array.IndexOf(args, "--lyrics-test");
            if (lt >= 0 && lt + 3 < args.Length)
            {
                int secs = 0;
                if (lt + 4 < args.Length) int.TryParse(args[lt + 4], out secs);
                Lyrics.Test(args[lt + 1], args[lt + 2], args[lt + 3], secs);
                return;
            }
            if (Array.IndexOf(args, "--install") >= 0)
            {
                try { Installer.Install(); }
                catch (Exception ex) { MessageBox.Show("Harbor couldn't be installed:\n" + ex.Message, "Harbor"); }
                return;
            }
            bool created;
            using (var mutex = new Mutex(true, @"Local\Harbor_SingleInstance_5b2e", out created))
            {
                if (Array.IndexOf(args, "--wait") >= 0 && !created)
                {
                    // restarting: give the old instance a moment to exit
                    for (int i = 0; i < 40 && !created; i++)
                    {
                        Thread.Sleep(100);
                        try { created = mutex.WaitOne(0); } catch (AbandonedMutexException) { created = true; }
                    }
                }
                if (Array.IndexOf(args, "--uninstall") >= 0)
                {
                    IntPtr h0 = N.FindWindow(null, "HarborHost");
                    if (h0 != IntPtr.Zero) { N.PostMessage(h0, N.WM_CLOSE, IntPtr.Zero, IntPtr.Zero); Thread.Sleep(1500); }
                    Installer.Remove();
                    Banners.RestoreWindows();
                    Installer.DeleteFilesAfterExit();
                    return;
                }
                if (Array.IndexOf(args, "--quit") >= 0 || Array.IndexOf(args, "--cc") >= 0)
                {
                    IntPtr host = N.FindWindow(null, "HarborHost");
                    if (host != IntPtr.Zero)
                        N.PostMessage(host, Array.IndexOf(args, "--quit") >= 0 ? N.WM_CLOSE : Host.OpenControlCenter, IntPtr.Zero, IntPtr.Zero);
                    return;
                }
                if (!created && Array.IndexOf(args, "--snapshot") < 0)
                {
                    // already running: opening Harbor again shows its app window (like Seelen UI)
                    if (Array.IndexOf(args, "--startup") < 0)
                    {
                        IntPtr host = N.FindWindow(null, "HarborHost");
                        if (host != IntPtr.Zero) { N.AllowSetForegroundWindow(0xFFFFFFFF); N.PostMessage(host, Host.OpenSettings, IntPtr.Zero, IntPtr.Zero); }
                    }
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (s, e) => App.Log(e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                {
                    App.Log(e.ExceptionObject as Exception);
                    App.EmergencyRestore();
                };
                Settings.Load();
                App.Start(args);
                Application.Run();
                App.EmergencyRestore();
            }
        }
    }
}
