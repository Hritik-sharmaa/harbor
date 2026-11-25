using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

namespace Harbor
{
    /// <summary>
    /// What harbord offers: a JSON view of each part of Core (Get) and the actions the UI can take (Call).
    /// Everything here runs on the main loop, like the Windows UI calls Core on its UI thread.
    /// </summary>
    sealed class Api
    {
        readonly LinuxPlatform platform;
        readonly Action<string> changed;
        public Api(LinuxPlatform platform, Action<string> changed) { this.platform = platform; this.changed = changed; }

        public static readonly string[] Sections = { "dock", "settings", "reminders", "notes", "timers", "pomodoro", "focus", "weather", "calendar", "claude", "shelf", "lyrics" };

        /// <summary>Starts Core's timers and hooks each part's Changed event to the Changed signal.</summary>
        public void Start()
        {
            Theme.Refresh();
            Reminders.Init();
            Timers.Init();
            Breaks.Init();
            CalendarFeed.Init();
            Reminders.Changed += () => changed("reminders");
            Notes.Changed += () => changed("notes");
            Timers.Changed += () => changed("timers");
            Pomodoro.Changed += () => changed("pomodoro");
            FocusMode.Changed += () => changed("focus");
            Weather.Changed += () => changed("weather");
            CalendarFeed.Changed += () => changed("calendar");
            ClaudeUsage.Changed += () => changed("claude");
            ClaudeHistory.Changed += () => changed("claude");
            Shelf.Changed += () => changed("shelf");
            Lyrics.Changed += () => changed("lyrics");
        }

        // ------------------------------------------------------------------ Get

        public string Get(string section)
        {
            if (section == "all")
            {
                var all = new Dictionary<string, object>();
                foreach (var s in Sections) if (s != "lyrics") all[s] = State(s);
                return Json.Write(all);
            }
            return Json.Write(State(section));
        }

        object State(string section)
        {
            switch (section)
            {
                case "dock": return DockState();
                case "settings": return SettingsState();
                case "reminders": return RemindersState();
                case "notes": return NotesState();
                case "timers": return TimersState();
                case "pomodoro": return PomodoroState();
                case "focus": return new Dictionary<string, object> { { "on", FocusMode.On }, { "left", FocusMode.Left }, { "leftText", FocusMode.LeftText() } };
                case "weather": return WeatherState();
                case "calendar": return CalendarState();
                case "claude": return ClaudeState();
                case "shelf": return new Dictionary<string, object> { { "items", Shelf.Items } };
                case "lyrics": return LyricsState();
                default: throw new ArgumentException("Unknown section \"" + section + "\". Try one of: all, " + string.Join(", ", Sections));
            }
        }

        static Dictionary<string, object> DockState()
        {
            return new Dictionary<string, object>
            {
                { "pinned", Settings.Pinned }, { "hidden", Settings.HiddenApps }, { "iconSize", Settings.IconSize },
                { "magnify", Settings.Magnify }, { "magnifyPercent", Settings.MagnifyPercent }, { "autoHide", Settings.AutoHide },
                { "showRunning", Settings.ShowRunning }, { "position", Look.Get("DockPosition") }, { "dots", Look.Bool("DockDots") },
                { "spacing", Look.Int("DockSpacing") }, { "padH", Look.Int("DockPadH") }, { "padV", Look.Int("DockPadV") },
                { "radius", Look.Int("DockRadius") }, { "margin", Look.Int("DockMargin") }, { "bounce", Look.Bool("DockBounce") },
                { "fill", Rgba(Look.DockFill) }, { "border", Rgba(Look.DockBorder) }, { "borderWidth", Look.Int("DockBorderWidth") },
                { "dark", Theme.Dark }, { "accent", Look.Hex(Theme.Accent) },
            };
        }

        static Dictionary<string, object> SettingsState()
        {
            var look = new Dictionary<string, object>();
            foreach (var k in Look.Keys) look[k] = Look.Get(k);
            return new Dictionary<string, object>
            {
                { "iconSize", Settings.IconSize }, { "magnify", Settings.Magnify }, { "magnifyPercent", Settings.MagnifyPercent },
                { "autoHide", Settings.AutoHide }, { "showRunning", Settings.ShowRunning }, { "showTopBar", Settings.ShowTopBar },
                { "theme", Settings.ThemeMode }, { "dark", Theme.Dark }, { "pinned", Settings.Pinned }, { "hidden", Settings.HiddenApps },
                { "look", look }, { "configDir", Settings.Dir }, { "version", AppInfo.Version },
            };
        }

        static string Rgba(System.Drawing.Color c)
        {
            return "rgba(" + c.R + "," + c.G + "," + c.B + "," + (c.A / 255.0).ToString("0.###", CultureInfo.InvariantCulture) + ")";
        }

        static object RemindersState()
        {
            var open = new List<object>();
            foreach (var i in Reminders.Open())
                open.Add(new Dictionary<string, object> { { "id", i.Id }, { "text", i.Text }, { "due", i.Due }, { "dueText", Reminders.DueText(i) }, { "overdue", Reminders.Overdue(i) } });
            var done = new List<object>();
            foreach (var i in Reminders.All)
                if (i.Done && i.Finished.Date == DateTime.Today) done.Add(new Dictionary<string, object> { { "id", i.Id }, { "text", i.Text }, { "finished", i.Finished } });
            return new Dictionary<string, object> { { "open", open }, { "doneToday", done } };
        }

        static object NotesState()
        {
            var l = new List<object>();
            foreach (var n in Notes.All) l.Add(new Dictionary<string, object> { { "id", n.Id }, { "text", n.Text }, { "at", n.At }, { "pinned", n.Pinned } });
            return new Dictionary<string, object> { { "notes", l } };
        }

        static object TimersState()
        {
            var timers = new List<object>();
            foreach (var c in Timers.Running)
                timers.Add(new Dictionary<string, object> { { "id", c.Id }, { "label", c.Label }, { "left", c.Left }, { "leftText", Timers.Text(c.Left) }, { "length", c.Length }, { "paused", c.Paused }, { "progress", c.Progress } });
            var alarms = new List<object>();
            foreach (var a in Timers.Alarms)
                alarms.Add(new Dictionary<string, object> { { "id", a.Id }, { "label", a.Label }, { "time", Timers.AlarmText(a) }, { "on", a.On } });
            var clocks = new List<object>();
            foreach (var c in Timers.Clocks)
            {
                var tz = c.Tz;
                clocks.Add(new Dictionary<string, object> { { "label", c.Label }, { "zone", c.Zone }, { "time", tz == null ? "" : TimeZoneInfo.ConvertTime(DateTime.Now, tz).ToString("t", CultureInfo.CurrentCulture) } });
            }
            var soon = Timers.Soonest();
            return new Dictionary<string, object> { { "timers", timers }, { "alarms", alarms }, { "clocks", clocks }, { "soonest", soon == null ? null : soon.Id } };
        }

        static object PomodoroState()
        {
            var f = Pomodoro.Focus;
            return new Dictionary<string, object>
            {
                { "phase", Pomodoro.Current.ToString() }, { "phaseName", Pomodoro.Name(Pomodoro.Current) }, { "running", Pomodoro.Running },
                { "remaining", Pomodoro.Remaining }, { "remainingText", Timers.Text(Pomodoro.Remaining) }, { "progress", Pomodoro.Progress },
                { "sessionsToday", Pomodoro.SessionsToday }, { "focus", f == null ? null : f.Text },
            };
        }

        static object WeatherState()
        {
            Weather.Ensure();
            var d = new Dictionary<string, object>
            {
                { "hasCity", Weather.HasCity }, { "place", Weather.Place }, { "fahrenheit", Weather.Fahrenheit },
                { "loading", Weather.Loading }, { "failed", Weather.Failed }, { "error", Weather.Error },
            };
            var n = Weather.Now;
            if (n != null)
            {
                var hours = new List<object>();
                foreach (var h in n.Hours) hours.Add(new Dictionary<string, object> { { "at", h.At }, { "temp", h.Temp }, { "code", h.Code }, { "rain", h.Rain }, { "day", h.Day } });
                d["now"] = new Dictionary<string, object>
                {
                    { "fetched", n.Fetched }, { "temp", n.Temp }, { "feels", n.Feels }, { "wind", n.Wind }, { "pressure", n.Pressure }, { "uv", n.Uv },
                    { "high", n.High }, { "low", n.Low }, { "humidity", n.Humidity }, { "code", n.Code }, { "describe", Weather.Describe(n.Code) },
                    { "rain", n.Rain }, { "day", n.Day }, { "sunrise", n.Sunrise }, { "sunset", n.Sunset }, { "hours", hours },
                };
            }
            return d;
        }

        static object CalendarState()
        {
            var l = new List<object>();
            foreach (var e in CalendarFeed.Events)
                l.Add(new Dictionary<string, object> { { "title", e.Title }, { "location", e.Location }, { "link", e.Link }, { "start", e.Start }, { "end", e.End }, { "allDay", e.AllDay } });
            return new Dictionary<string, object> { { "configured", CalendarFeed.Configured }, { "busy", CalendarFeed.Busy }, { "error", CalendarFeed.Error }, { "fetched", CalendarFeed.Fetched }, { "events", l } };
        }

        static object ClaudeState()
        {
            ClaudeUsage.Refresh();
            var limits = new List<object>();
            foreach (var l in ClaudeUsage.Limits) limits.Add(new Dictionary<string, object> { { "name", l.Name }, { "percent", l.Percent }, { "resets", l.Resets } });
            var w = ClaudeUsage.Week;
            var sessions = new List<object>();
            foreach (var e in ClaudeHistory.Recent())
                sessions.Add(new Dictionary<string, object>
                {
                    { "id", e.SessionId }, { "project", e.Project }, { "title", e.Title }, { "branch", e.Branch }, { "model", ClaudeHistory.ShortModel(e.Model) },
                    { "messages", e.Messages }, { "tokens", e.Tokens }, { "tokensText", ClaudeHistory.Tokens(e.Tokens) }, { "lastActive", e.LastActive },
                });
            return new Dictionary<string, object>
            {
                { "available", ClaudeHistory.Available }, { "loaded", ClaudeUsage.Loaded }, { "limits", limits }, { "limitsAt", ClaudeUsage.LimitsAt },
                { "limitsFrom", ClaudeUsage.LimitsFrom }, { "week", w.Week }, { "replies", w.Replies }, { "last5h", w.Last5h },
                { "sources", Pairs(w.Sources) }, { "models", Pairs(w.Models) }, { "sessions", sessions },
            };
        }

        static List<object> Pairs(List<KeyValuePair<string, long>> l)
        {
            var r = new List<object>();
            foreach (var kv in l) r.Add(new Dictionary<string, object> { { "name", kv.Key }, { "tokens", kv.Value } });
            return r;
        }

        object LyricsState()
        {
            Lyrics.Result res;
            var st = Lyrics.For(out res);
            var d = new Dictionary<string, object> { { "state", st.ToString() } };
            if (res != null)
            {
                d["instrumental"] = res.Instrumental;
                d["notFound"] = res.NotFound;
                d["failed"] = res.Failed;
                d["plain"] = res.Plain;
                if (res.Synced != null)
                {
                    var lines = new List<object>();
                    foreach (var l in res.Synced) lines.Add(new Dictionary<string, object> { { "at", l.At }, { "text", l.Text } });
                    d["synced"] = lines;
                }
            }
            return d;
        }

        // ------------------------------------------------------------------ Call

        /// <summary>Runs an action. 'onLoop' runs a step on the main loop; the reply is JSON ({"ok": true} or the new state).</summary>
        public async Task<string> CallAsync(string action, string argsJson, Func<Func<string>, Task<string>> onLoop)
        {
            var a = string.IsNullOrWhiteSpace(argsJson) ? new Dictionary<string, object>() : Json.Object(argsJson);
            if (a == null) throw new ArgumentException("args must be a JSON object");

            if (action == "weather.setCity")
            {
                // geocoding happens off the loop; the reply waits for it
                var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                await onLoop(() => { Weather.SetCity(Str(a, "name"), err => tcs.TrySetResult(err)); return ""; }).ConfigureAwait(false);
                string error = await tcs.Task.ConfigureAwait(false);
                return Json.Write(new Dictionary<string, object> { { "ok", error == null }, { "error", error } });
            }
            return await onLoop(() => Json.Write(Do(action, a))).ConfigureAwait(false);
        }

        static readonly Dictionary<string, object> Ok = new Dictionary<string, object> { { "ok", true } };

        object Do(string action, Dictionary<string, object> a)
        {
            switch (action)
            {
                // the extension: it draws live activities while it's here
                case "extension.hello": if (platform.Bus != null) platform.Bus.ExtensionListening = true; return Ok;
                case "extension.bye": if (platform.Bus != null) platform.Bus.ExtensionListening = false; return Ok;
                case "activity.click": { Action click; lock (clicks) clicks.TryGetValue(Str(a, "id"), out click); if (click != null) click(); return Ok; }

                // dock
                case "dock.pin": { string id = Str(a, "id"); if (id != "" && !Settings.Pinned.Contains(id)) { int at = Int(a, "index", Settings.Pinned.Count); Settings.Pinned.Insert(Clamp(at, 0, Settings.Pinned.Count), id); SaveSettings("dock"); } return Ok; }
                case "dock.unpin": if (Settings.Pinned.Remove(Str(a, "id"))) SaveSettings("dock"); return Ok;
                case "dock.move":
                    {
                        string id = Str(a, "id");
                        if (!Settings.Pinned.Remove(id)) return Ok;
                        Settings.Pinned.Insert(Clamp(Int(a, "index", 0), 0, Settings.Pinned.Count), id);
                        SaveSettings("dock");
                        return Ok;
                    }
                case "dock.hide": { string id = Str(a, "id"); if (id != "" && !Settings.HiddenApps.Contains(id)) { Settings.HiddenApps.Add(id); SaveSettings("dock"); } return Ok; }

                // settings
                case "settings.set": SetSetting(Str(a, "key"), a.ContainsKey("value") ? a["value"] : null); return Ok;
                case "settings.reload": Settings.Load(); Theme.Refresh(); changed("settings"); changed("dock"); return Ok;

                // reminders
                case "reminders.add": Reminders.Add(Str(a, "text"), Date(a, "due")); return Ok;
                case "reminders.toggle": { var i = Reminders.Find(Str(a, "id")); if (i != null) Reminders.Toggle(i); return Ok; }
                case "reminders.remove": { var i = Reminders.Find(Str(a, "id")); if (i != null) Reminders.Remove(i); return Ok; }

                // notes
                case "notes.add": Notes.Add(Str(a, "text")); return Ok;
                case "notes.remove": { var n = Notes.All.Find(x => x.Id == Str(a, "id")); if (n != null) Notes.Remove(n); return Ok; }
                case "notes.pin": { var n = Notes.All.Find(x => x.Id == Str(a, "id")); if (n != null) Notes.TogglePin(n); return Ok; }

                // timers, alarms, world clocks
                case "timers.start":
                    {
                        var len = a.ContainsKey("seconds") ? TimeSpan.FromSeconds(Num(a, "seconds")) : Timers.Parse(Str(a, "text"));
                        if (len <= TimeSpan.Zero) return Error("Type a length, e.g. 5, 1:30 or 1h 20m");
                        Timers.Start(len, Str(a, "label"));
                        return Ok;
                    }
                case "timers.pause": { var c = Timers.Running.Find(x => x.Id == Str(a, "id")); if (c != null) Timers.TogglePause(c); return Ok; }
                case "timers.remove": { var c = Timers.Running.Find(x => x.Id == Str(a, "id")); if (c != null) Timers.Remove(c); return Ok; }
                case "alarms.add": { string err = Timers.AddAlarm(Str(a, "text")); return err == null ? Ok : Error(err); }
                case "alarms.toggle": { var x = Timers.Alarms.Find(y => y.Id == Str(a, "id")); if (x != null) Timers.ToggleAlarm(x); return Ok; }
                case "alarms.remove": { var x = Timers.Alarms.Find(y => y.Id == Str(a, "id")); if (x != null) Timers.RemoveAlarm(x); return Ok; }
                case "clocks.add": { string err = Timers.AddClock(Str(a, "text")); return err == null ? Ok : Error(err); }
                case "clocks.remove": { var c = Timers.Clocks.Find(x => x.Label == Str(a, "label")); if (c != null) Timers.RemoveClock(c); return Ok; }

                // Pomodoro and Focus
                case "pomodoro.toggle": Pomodoro.Toggle(); return Ok;
                case "pomodoro.reset": Pomodoro.Reset(); return Ok;
                case "pomodoro.skip": Pomodoro.Skip(); return Ok;
                case "pomodoro.nextFocus": Pomodoro.NextFocus(); return Ok;
                case "pomodoro.select":
                    {
                        Pomodoro.Phase p;
                        if (!Enum.TryParse(Str(a, "phase"), true, out p)) return Error("phase is Work, ShortBreak or LongBreak");
                        Pomodoro.Select(p);
                        return Ok;
                    }
                case "focus.toggle": FocusMode.Toggle(); return Ok;

                // weather, calendar, lyrics, Claude, shelf
                case "weather.setUnit": Weather.SetUnit(Bool(a, "fahrenheit")); changed("weather"); return Ok;
                case "calendar.refresh": CalendarFeed.Refresh(); return Ok;
                case "calendar.join": Core.Open(Str(a, "link")); return Ok;
                case "lyrics.song":
                    platform.Playing = new Song
                    {
                        Has = Str(a, "title") != "", Artist = Str(a, "artist"), Title = Str(a, "title"), Album = Str(a, "album"),
                        Duration = TimeSpan.FromSeconds(Num(a, "duration")),
                    };
                    return LyricsState();
                case "lyrics.retry": Lyrics.Retry(); return Ok;
                case "claude.refresh": ClaudeUsage.Refresh(); ClaudeHistory.Recent(); return Ok;
                case "shelf.add":
                    {
                        var paths = new List<string>();
                        object v;
                        if (a.TryGetValue("paths", out v) && v is IList) foreach (var p in (IList)v) if (p is string) paths.Add((string)p);
                        Shelf.Add(paths);
                        return Ok;
                    }
                case "shelf.remove": Shelf.Remove(Str(a, "path")); return Ok;
                case "shelf.clear": Shelf.Clear(); return Ok;

                default: throw new ArgumentException("Unknown action \"" + action + "\"");
            }
        }

        /// <summary>settings.set: Harbor's own settings by name, anything else as an appearance (Look) key.</summary>
        void SetSetting(string key, object value)
        {
            string v = value is bool ? ((bool)value ? "1" : "0") : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            bool on = v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);
            int n;
            int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n);
            switch ((key ?? "").ToLowerInvariant())
            {
                case "iconsize": Settings.IconSize = Clamp(n, 28, 96); break;
                case "magnify": Settings.Magnify = on; break;
                case "magnifypercent": Settings.MagnifyPercent = Clamp(n, 110, 250); break;
                case "autohide": Settings.AutoHide = on; break;
                case "showrunning": Settings.ShowRunning = on; break;
                case "showtopbar": Settings.ShowTopBar = on; break;
                case "theme": Settings.ThemeMode = v == "dark" || v == "light" ? v : "auto"; break;
                default:
                    bool known = false;
                    foreach (var k in Look.Keys) if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) { known = true; key = k; break; }
                    if (!known) throw new ArgumentException("Unknown setting \"" + key + "\"");
                    Look.Set(key, v);
                    break;
            }
            Theme.Refresh();
            Breaks.Init();
            SaveSettings("settings");
            changed("dock");
        }

        void SaveSettings(string section)
        {
            Settings.Save();
            changed(section);
        }

        // ------------------------------------------------------------------ live activity clicks

        static readonly Dictionary<string, Action> clicks = new Dictionary<string, Action>();
        static readonly Queue<string> clickOrder = new Queue<string>();

        public static string RememberClick(Action a)
        {
            string id = Guid.NewGuid().ToString("N").Substring(0, 10);
            lock (clicks)
            {
                clicks[id] = a;
                clickOrder.Enqueue(id);
                while (clickOrder.Count > 20) clicks.Remove(clickOrder.Dequeue());
            }
            return id;
        }

        // ------------------------------------------------------------------ argument helpers

        static Dictionary<string, object> Error(string message) { return new Dictionary<string, object> { { "ok", false }, { "error", message } }; }
        static string Str(Dictionary<string, object> a, string k) { object v; return a.TryGetValue(k, out v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : ""; }
        static double Num(Dictionary<string, object> a, string k) { object v; try { return a.TryGetValue(k, out v) && v != null ? Convert.ToDouble(v, CultureInfo.InvariantCulture) : 0; } catch { return 0; } }
        static int Int(Dictionary<string, object> a, string k, int d) { return a.ContainsKey(k) ? (int)Num(a, k) : d; }
        static bool Bool(Dictionary<string, object> a, string k) { object v; return a.TryGetValue(k, out v) && (v is bool ? (bool)v : Str(a, k) == "1"); }
        static int Clamp(int v, int lo, int hi) { return v < lo ? lo : v > hi ? hi : v; }

        static DateTime? Date(Dictionary<string, object> a, string k)
        {
            string s = Str(a, k);
            DateTime d;
            if (s == "" || !DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out d)) return null;
            return d.Kind == DateTimeKind.Utc ? d.ToLocalTime() : d;
        }
    }
}
