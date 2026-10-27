using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace Harbor
{
    /// <summary>
    /// Claude Code in the island. Claude Code runs "Harbor.exe --claude-hook" on a few hook events
    /// (added to %USERPROFILE%\.claude\settings.json only when you switch this on). That short-lived process passes
    /// the event to the running Harbor over a private named pipe:
    ///  - session start / prompt / notification / stop / end: fire-and-forget, they drive the live Running / Waiting status;
    ///  - PermissionRequest: the island shows Allow / Always allow / Deny and the answer goes back to Claude Code.
    /// If Harbor isn't running, or you don't answer, the hook prints nothing and Claude Code asks you itself as usual.
    /// </summary>
    static class ClaudeBridge
    {
        public sealed class Session
        {
            public string Id, Cwd, Model, Status = "Idle", LastMessage, Title;
            public DateTime Updated = DateTime.Now, Started = DateTime.Now;
            public string Project { get { return string.IsNullOrEmpty(Cwd) ? "Claude Code" : Path.GetFileName(Cwd.TrimEnd('\\', '/')); } }
        }

        public sealed class Request
        {
            public string SessionId, Cwd, Tool, Summary, Detail;
            public DateTime At = DateTime.Now;
            public Action<string> Answer;   // "allow", "always", "deny" (null: let Claude Code ask)
            public bool Done;
            public string Project { get { return string.IsNullOrEmpty(Cwd) ? "Claude Code" : Path.GetFileName(Cwd.TrimEnd('\\', '/')); } }
        }

        public static readonly Dictionary<string, Session> Sessions = new Dictionary<string, Session>();
        public static readonly List<Request> Pending = new List<Request>();
        public static event Action Changed;
        /// <summary>Raised when Claude asks permission, so the island can pop up.</summary>
        public static event Action Asked;

        static string PipeName { get { return "Harbor.Claude." + Environment.UserName; } }

        // ------------------------------------------------------------------ server (inside Harbor)

        /// <summary>--snapshot --demo: two live sessions and one permission request (nothing is listened to or answered).</summary>
        public static void UseDemo()
        {
            HooksOn = true;
            Sessions["demo-1"] = new Session { Id = "demo-1", Cwd = @"C:\code\aurora-shop", Model = "claude-opus-5-5", Status = "Waiting", Updated = DateTime.Now.AddSeconds(-20) };
            Sessions["demo-3"] = new Session { Id = "demo-3", Cwd = @"C:\code\orbit-app", Model = "claude-sonnet-5-5", Status = "Running", Updated = DateTime.Now.AddMinutes(-1) };
            Pending.Add(new Request { SessionId = "demo-1", Cwd = @"C:\code\aurora-shop", Tool = "Bash", Detail = "Run the checkout tests", Summary = "npm test -- checkout", At = DateTime.Now.AddSeconds(-12), Answer = a => { } });
        }

        static Thread listener;
        static volatile bool running;

        /// <summary>Whether Harbor's hook is in Claude Code's settings (read at start, updated when you switch it).</summary>
        public static bool HooksOn;

        public static void Start()
        {
            if (listener != null) return;
            HooksOn = HooksInstalled();
            running = true;
            listener = new Thread(Listen) { IsBackground = true, Name = "Claude hook listener" };
            listener.Start();
        }

        public static void Stop() { running = false; }

        static void Listen()
        {
            while (running)
            {
                NamedPipeServerStream pipe = null;
                try
                {
                    pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    pipe.WaitForConnection();
                    var p = pipe;
                    pipe = null;
                    ThreadPool.QueueUserWorkItem(delegate { Serve(p); });
                }
                catch (Exception ex) { if (pipe != null) pipe.Dispose(); App.Log(ex); Thread.Sleep(1000); }
            }
        }

        static void Serve(NamedPipeServerStream pipe)
        {
            try
            {
                var reader = new StreamReader(pipe, Encoding.UTF8);
                string line = reader.ReadLine();
                if (string.IsNullOrEmpty(line)) { pipe.Dispose(); return; }
                var ev = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<Dictionary<string, object>>(line);
                string name = Str(ev, "hook_event_name");
                if (name != "PermissionRequest") { App.Post(() => OnEvent(name, ev)); pipe.Dispose(); return; }

                // a permission request: wait for the answer from the island, or for Claude Code to give up on us
                var answered = new ManualResetEvent(false);
                string answer = null;
                var req = MakeRequest(ev);
                req.Answer = a => { answer = a; answered.Set(); };
                App.Post(() => { Pending.Add(req); Touch(req.SessionId, req.Cwd, "Waiting", null); Fire(); if (Asked != null) Asked(); });
                // the hook process closes the pipe if Claude Code decided elsewhere (the editor's own prompt) or timed out
                var gone = new ManualResetEvent(false);
                ThreadPool.QueueUserWorkItem(delegate { try { pipe.Read(new byte[1], 0, 1); } catch { } gone.Set(); });
                WaitHandle.WaitAny(new WaitHandle[] { answered, gone }, TimeSpan.FromSeconds(115));
                if (answer != null)
                {
                    try { var w = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true }; w.WriteLine(answer); } catch { }
                }
                App.Post(() => { req.Done = true; Pending.Remove(req); Fire(); });
                pipe.Dispose();
            }
            catch (Exception ex) { App.Log(ex); try { pipe.Dispose(); } catch { } }
        }

        static Request MakeRequest(Dictionary<string, object> ev)
        {
            var r = new Request { SessionId = Str(ev, "session_id"), Cwd = Str(ev, "cwd"), Tool = Str(ev, "tool_name") ?? "a tool" };
            object ti;
            var input = ev.TryGetValue("tool_input", out ti) ? ti as Dictionary<string, object> : null;
            if (input != null)
            {
                // the one line that says what Claude wants to do
                foreach (var k in new[] { "command", "file_path", "path", "url", "pattern", "query", "description", "prompt" })
                {
                    string v = Str(input, k);
                    if (!string.IsNullOrEmpty(v)) { r.Summary = v; break; }
                }
                if (r.Tool == "Bash") r.Detail = Str(input, "description");
            }
            if (r.Summary != null && r.Summary.Length > 400) r.Summary = r.Summary.Substring(0, 400) + "…";
            return r;
        }

        static void OnEvent(string name, Dictionary<string, object> ev)
        {
            string id = Str(ev, "session_id"), cwd = Str(ev, "cwd");
            switch (name)
            {
                case "SessionStart": Touch(id, cwd, "Idle", Str(ev, "model")); break;
                case "UserPromptSubmit": Touch(id, cwd, "Running", null); break;
                case "Notification":
                    // "permission_prompt": Claude is waiting on you in the editor; "idle_prompt": it's done and waiting
                    string type = Str(ev, "notification_type");
                    Touch(id, cwd, type == "idle_prompt" ? "Done" : "Waiting", null);
                    break;
                case "Stop":
                    {
                        var s = Touch(id, cwd, "Done", null);
                        string msg = Str(ev, "last_assistant_message");
                        if (!string.IsNullOrEmpty(msg)) s.LastMessage = OneLine(msg, 160);
                        Pending.RemoveAll(p => p.SessionId == id);
                        if (Look.Bool("ClaudeBanners")) Banners.ShowLocal("Claude Code", "Done in " + s.Project, s.LastMessage ?? "Claude finished and is waiting for you.");
                        break;
                    }
                case "SessionEnd": Sessions.Remove(id ?? ""); Pending.RemoveAll(p => p.SessionId == id); break;
            }
            Fire();
        }

        static Session Touch(string id, string cwd, string status, string model)
        {
            Session s;
            if (!Sessions.TryGetValue(id ?? "", out s)) Sessions[id ?? ""] = s = new Session { Id = id };
            if (!string.IsNullOrEmpty(cwd)) s.Cwd = cwd;
            if (!string.IsNullOrEmpty(model)) s.Model = model;
            if (status != null) s.Status = status;
            s.Updated = DateTime.Now;
            return s;
        }

        static void Fire() { if (Changed != null) Changed(); }

        public static void Answer(Request r, string answer)
        {
            if (r.Done) return;
            r.Done = true;
            Pending.Remove(r);
            Touch(r.SessionId, r.Cwd, answer == "deny" ? "Done" : "Running", null);
            r.Answer(answer);
            Fire();
        }

        static string Str(Dictionary<string, object> d, string k)
        {
            object v;
            return d != null && d.TryGetValue(k, out v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : null;
        }

        static string OneLine(string s, int max)
        {
            s = Regex.Replace(s ?? "", @"\s+", " ").Trim();
            return s.Length > max ? s.Substring(0, max - 1) + "…" : s;
        }

        // ------------------------------------------------------------------ hook process ("Harbor.exe --claude-hook")

        /// <summary>
        /// Runs as the hook Claude Code launches: forwards stdin to Harbor and, for a permission request, prints Harbor's
        /// answer as Claude Code's hook JSON. Prints nothing (= "ask as usual") if Harbor isn't running or there's no answer.
        /// </summary>
        public static void RunHook()
        {
            try
            {
                string input;
                using (var stdin = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8)) input = stdin.ReadToEnd();
                if (string.IsNullOrWhiteSpace(input)) return;
                var ev = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<Dictionary<string, object>>(input);
                bool ask = Str(ev, "hook_event_name") == "PermissionRequest";
                using (var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut))
                {
                    try { pipe.Connect(300); } catch { return; } // Harbor isn't running: stay out of the way
                    var w = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
                    // newlines between JSON tokens are just whitespace; inside strings they're already escaped
                    w.WriteLine(input.Replace("\r", " ").Replace("\n", " "));
                    if (!ask) return;
                    string answer = null;
                    var t = new Thread(() => { try { answer = new StreamReader(pipe, Encoding.UTF8).ReadLine(); } catch { } }) { IsBackground = true };
                    t.Start();
                    t.Join(TimeSpan.FromSeconds(110));
                    string json = Decision(answer, ev);
                    if (json != null) { var o = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)); o.Write(json); o.Flush(); }
                }
            }
            catch { /* never break Claude Code: no output means "ask as usual" */ }
        }

        static string Decision(string answer, Dictionary<string, object> ev)
        {
            var js = new JavaScriptSerializer();
            var decision = new Dictionary<string, object>();
            if (answer == "allow" || answer == "always")
            {
                decision["behavior"] = "allow";
                object sugg;
                // "Always allow" applies the rule Claude Code itself suggested for this request
                if (answer == "always" && ev.TryGetValue("permission_suggestions", out sugg) && sugg is IList && ((IList)sugg).Count > 0)
                    decision["updatedPermissions"] = sugg;
            }
            else if (answer == "deny") { decision["behavior"] = "deny"; decision["message"] = "Denied from Harbor."; }
            else return null;
            var output = new Dictionary<string, object> { { "hookSpecificOutput", new Dictionary<string, object> { { "hookEventName", "PermissionRequest" }, { "decision", decision } } } };
            return js.Serialize(output);
        }

        // ------------------------------------------------------------------ settings.json (only when you switch it on)

        static string SettingsPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json"); } }
        static readonly string[] events = { "SessionStart", "UserPromptSubmit", "Notification", "Stop", "SessionEnd", "PermissionRequest" };

        /// <summary>The hook command for this Harbor.exe: a plain path when it has no spaces (works in Git Bash and PowerShell).</summary>
        static Dictionary<string, object> HookEntry(string exe, string ev)
        {
            var h = new Dictionary<string, object> { { "type", "command" } };
            string fwd = exe.Replace('\\', '/');
            if (fwd.IndexOf(' ') < 0) h["command"] = fwd + " --claude-hook";
            else { h["command"] = exe; h["args"] = new ArrayList { "--claude-hook" }; } // exec form: no shell quoting to get wrong
            if (ev == "PermissionRequest") h["timeout"] = 120;
            else { h["timeout"] = 10; h["async"] = true; } // never make Claude Code wait for Harbor
            return h;
        }

        static bool IsOurs(object hook)
        {
            var h = hook as Dictionary<string, object>;
            if (h == null) return false;
            string c = Str(h, "command") ?? "";
            object a;
            bool argMatch = h.TryGetValue("args", out a) && a is IList && ((IList)a).Contains("--claude-hook");
            return (c.IndexOf("--claude-hook", StringComparison.OrdinalIgnoreCase) >= 0 || argMatch) && c.IndexOf("Harbor", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool HooksInstalled()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return false;
                var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(SettingsPath));
                object hooks;
                if (root == null || !root.TryGetValue("hooks", out hooks) || !(hooks is Dictionary<string, object>)) return false;
                object list;
                if (!((Dictionary<string, object>)hooks).TryGetValue("PermissionRequest", out list) || !(list is IList)) return false;
                foreach (var grp in (IList)list)
                {
                    var g = grp as Dictionary<string, object>;
                    object hs;
                    if (g != null && g.TryGetValue("hooks", out hs) && hs is IList)
                        foreach (var h in (IList)hs) if (IsOurs(h)) return true;
                }
            }
            catch { }
            return false;
        }

        /// <summary>Adds (install = true) or removes Harbor's hooks, keeping everything else in the file exactly as it was. Returns an error or null.</summary>
        public static string SetHooks(bool install, string exe, string target = null)
        {
            try
            {
                string path = target ?? SettingsPath;
                var js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                Dictionary<string, object> root = File.Exists(path) ? js.Deserialize<Dictionary<string, object>>(File.ReadAllText(path)) : null;
                if (root == null) { if (!install) return null; root = new Dictionary<string, object>(); }
                if (File.Exists(path) && !File.Exists(path + ".harbor-backup")) File.Copy(path, path + ".harbor-backup");

                object h;
                var hooks = root.TryGetValue("hooks", out h) ? h as Dictionary<string, object> : null;
                if (hooks == null) { if (!install) return null; hooks = new Dictionary<string, object>(); root["hooks"] = hooks; }

                // take out any Harbor entries first (also from an older install path), then add fresh ones
                foreach (var ev in new List<string>(hooks.Keys))
                {
                    var groups = hooks[ev] as IList;
                    if (groups == null) continue;
                    for (int i = groups.Count - 1; i >= 0; i--)
                    {
                        var g = groups[i] as Dictionary<string, object>;
                        object hs;
                        if (g == null || !g.TryGetValue("hooks", out hs) || !(hs is IList)) continue;
                        var list = (IList)hs;
                        for (int j = list.Count - 1; j >= 0; j--) if (IsOurs(list[j])) list.RemoveAt(j);
                        if (list.Count == 0) groups.RemoveAt(i);
                    }
                    if (groups.Count == 0) hooks.Remove(ev);
                }
                if (install)
                    foreach (var ev in events)
                    {
                        object existing;
                        var groups = hooks.TryGetValue(ev, out existing) && existing is ArrayList ? (ArrayList)existing : null;
                        if (groups == null) { groups = new ArrayList(); hooks[ev] = groups; }
                        groups.Add(new Dictionary<string, object> { { "matcher", "" }, { "hooks", new ArrayList { HookEntry(exe, ev) } } });
                    }
                if (hooks.Count == 0) root.Remove("hooks");

                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string tmp = path + ".harbor-tmp";
                File.WriteAllText(tmp, Pretty(root, 0) + "\n", new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);
                if (target == null) HooksOn = install;
                return null;
            }
            catch (Exception ex) { App.Log(ex); return ex.Message; }
        }

        /// <summary>JSON with 2-space indentation, like Claude Code writes it.</summary>
        static string Pretty(object o, int depth)
        {
            string pad = new string(' ', depth * 2), inner = new string(' ', (depth + 1) * 2);
            var d = o as IDictionary;
            if (d != null)
            {
                if (d.Count == 0) return "{}";
                var parts = new List<string>();
                foreach (DictionaryEntry e in d) parts.Add(inner + Quote(e.Key.ToString()) + ": " + Pretty(e.Value, depth + 1));
                return "{\n" + string.Join(",\n", parts.ToArray()) + "\n" + pad + "}";
            }
            var l = o as IList;
            if (l != null && !(o is string))
            {
                if (l.Count == 0) return "[]";
                var parts = new List<string>();
                foreach (var v in l) parts.Add(inner + Pretty(v, depth + 1));
                return "[\n" + string.Join(",\n", parts.ToArray()) + "\n" + pad + "]";
            }
            if (o is bool) return (bool)o ? "true" : "false";
            if (o == null) return "null";
            if (o is int || o is long || o is decimal || o is double || o is float) return Convert.ToString(o, CultureInfo.InvariantCulture);
            return Quote(Convert.ToString(o, CultureInfo.InvariantCulture));
        }

        /// <summary>A JSON string escaping only what JSON requires (the stock serializer also turns &amp; &lt; &gt; ' into \u00xx).</summary>
        static string Quote(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char ch in s)
            {
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (ch < 0x20) sb.Append("\\u").Append(((int)ch).ToString("x4"));
                        else sb.Append(ch);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }
    }
}
