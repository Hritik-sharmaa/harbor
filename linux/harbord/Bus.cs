using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tmds.DBus.Protocol;

namespace Harbor
{
    /// <summary>
    /// harbord on the D-Bus session bus. It owns io.github.hritik_sharmaa.Harbor and serves one object:
    ///
    ///   /io/github/hritik_sharmaa/Harbor   interface io.github.hritik_sharmaa.Harbor1
    ///     Version() → s                     harbord's version
    ///     Get(s section) → s                JSON state of "dock", "settings", "reminders", "notes", "timers",
    ///                                       "pomodoro", "focus", "weather", "calendar", "claude", "shelf" or "all"
    ///     Call(s action, s args) → s        does something ("reminders.add", {"text": ...}) and returns JSON
    ///     signal Changed(s section)         that section's state changed: Get it again
    ///     signal Activity(s json)           show a live activity (pill) from the island
    ///     signal OpenTab(s tab)             open the island on this tab
    ///
    /// Banners go straight to org.freedesktop.Notifications, so they work under any desktop.
    /// </summary>
    sealed class Bus : IPathMethodHandler
    {
        public const string Name = "io.github.hritik_sharmaa.Harbor";
        public const string ObjectPath = "/io/github/hritik_sharmaa/Harbor";
        public const string Interface = "io.github.hritik_sharmaa.Harbor1";

        static readonly ReadOnlyMemory<byte> IntrospectXml = Encoding.UTF8.GetBytes(
            "<interface name=\"" + Interface + "\">\n" +
            "  <method name=\"Version\"><arg type=\"s\" direction=\"out\" name=\"version\"/></method>\n" +
            "  <method name=\"Get\"><arg type=\"s\" direction=\"in\" name=\"section\"/><arg type=\"s\" direction=\"out\" name=\"json\"/></method>\n" +
            "  <method name=\"Call\"><arg type=\"s\" direction=\"in\" name=\"action\"/><arg type=\"s\" direction=\"in\" name=\"args\"/><arg type=\"s\" direction=\"out\" name=\"json\"/></method>\n" +
            "  <signal name=\"Changed\"><arg type=\"s\" name=\"section\"/></signal>\n" +
            "  <signal name=\"Activity\"><arg type=\"s\" name=\"json\"/></signal>\n" +
            "  <signal name=\"OpenTab\"><arg type=\"s\" name=\"tab\"/></signal>\n" +
            "</interface>\n");

        readonly DBusConnection conn;
        readonly MainLoop loop;
        readonly Api api;

        public Bus(DBusConnection conn, MainLoop loop, Api api) { this.conn = conn; this.loop = loop; this.api = api; }

        public string Path { get { return ObjectPath; } }
        public bool HandlesChildPaths { get { return false; } }

        /// <summary>Set when the GNOME extension says hello (it draws live activities); cleared when it says goodbye.</summary>
        public volatile bool ExtensionListening;

        /// <summary>Connects, serves the object and takes the bus name. False if another harbord already has it.</summary>
        public async Task<bool> StartAsync()
        {
            conn.AddMethodHandler(this);
            return await conn.TryRequestNameAsync(Name, RequestNameOptions.None).ConfigureAwait(false);
        }

        // ------------------------------------------------------------------ incoming calls

        public async ValueTask HandleMethodAsync(MethodContext ctx)
        {
            if (ctx.IsDBusIntrospectRequest)
            {
                ctx.ReplyIntrospectXml(new[] { IntrospectXml });
                return;
            }
            var req = ctx.Request;
            if (req.InterfaceAsString != Interface) { ctx.ReplyUnknownMethodError(); return; }
            string member = req.MemberAsString, section, action, args;
            if (!ReadArgs(req, member, out section, out action, out args)) { ctx.ReplyUnknownMethodError(); return; }

            string result;
            try
            {
                if (member == "Version") result = AppInfo.Version;
                else if (member == "Get") result = await OnLoop(() => api.Get(section)).ConfigureAwait(false);
                else result = await api.CallAsync(action, args, OnLoop).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (!(ex is ArgumentException)) Core.Log(ex);
                ctx.ReplyError(Interface + ".Error", ex.Message);
                return;
            }
            ReplyString(ctx, result);
        }

        static bool ReadArgs(Message req, string member, out string section, out string action, out string args)
        {
            section = action = args = null;
            string sig = req.SignatureAsString ?? "";
            var r = req.GetBodyReader();
            if (member == "Version" && sig == "") return true;
            if (member == "Get" && sig == "s") { section = r.ReadString(); return true; }
            if (member == "Call" && sig == "ss") { action = r.ReadString(); args = r.ReadString(); return true; }
            return false;
        }

        static void ReplyString(MethodContext ctx, string s)
        {
            using (var w = ctx.CreateReplyWriter("s"))
            {
                w.WriteString(s ?? "");
                ctx.Reply(w.CreateMessage());
            }
        }

        /// <summary>Runs 'f' on the main loop (where all of Core lives) and hands back its result.</summary>
        public Task<T> OnLoop<T>(Func<T> f)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            loop.Enqueue(() => { try { tcs.SetResult(f()); } catch (Exception ex) { tcs.SetException(ex); } });
            return tcs.Task;
        }

        // ------------------------------------------------------------------ signals

        public void EmitChanged(string section) { Signal("Changed", section); }
        public void EmitActivity(string json) { Signal("Activity", json); }
        public void EmitOpenTab(string tab) { Signal("OpenTab", tab); }

        void Signal(string member, string arg)
        {
            try
            {
                using (var w = conn.GetMessageWriter())
                {
                    w.WriteSignalHeader(null, ObjectPath, Interface, member, "s");
                    w.WriteString(arg ?? "");
                    conn.TrySendMessage(w.CreateMessage());
                }
            }
            catch (Exception ex) { Core.Log(ex); }
        }

        // ------------------------------------------------------------------ talking to the desktop

        int notifyFailed;

        /// <summary>A banner through the desktop's notification server (GNOME Shell, Plasma, mako...).</summary>
        public void Notify(string app, string title, string body)
        {
            try
            {
                MessageBuffer msg;
                using (var w = conn.GetMessageWriter())
                {
                    w.WriteMethodCallHeader("org.freedesktop.Notifications", "/org/freedesktop/Notifications",
                        "org.freedesktop.Notifications", "Notify", "susssasa{sv}i");
                    w.WriteString(app ?? "Harbor");
                    w.WriteUInt32(0);                 // replaces_id
                    w.WriteString("harbor");          // app_icon (installed by the package)
                    w.WriteString(title ?? "");
                    w.WriteString(body ?? "");
                    w.WriteArray(new string[0]);      // actions
                    var hints = w.WriteDictionaryStart();
                    w.WriteDictionaryEnd(hints);
                    w.WriteInt32(-1);                 // expire_timeout: the server's default
                    msg = w.CreateMessage();
                }
                conn.CallMethodAsync(msg).ContinueWith(t =>
                {
                    // no notification server (a bare session, some tiling setups): say so once, not on every banner
                    if (Interlocked.Exchange(ref notifyFailed, 1) == 0)
                        Console.Error.WriteLine("harbord: banners can't be shown: " + t.Exception.InnerException.Message);
                }, TaskContinuationOptions.OnlyOnFaulted);
            }
            catch (Exception ex) { Core.Log(ex); }
        }

        /// <summary>Milliseconds since the last input: GNOME's idle monitor, else the freedesktop screensaver (KDE). 0 if neither answers.</summary>
        public double IdleMs()
        {
            ulong ms;
            if (TryCall("org.gnome.Mutter.IdleMonitor", "/org/gnome/Mutter/IdleMonitor/Core", "org.gnome.Mutter.IdleMonitor", "GetIdletime",
                (m, s) => m.GetBodyReader().ReadUInt64(), out ms)) return ms;
            uint kms;
            if (TryCall("org.freedesktop.ScreenSaver", "/org/freedesktop/ScreenSaver", "org.freedesktop.ScreenSaver", "GetSessionIdleTime",
                (m, s) => m.GetBodyReader().ReadUInt32(), out kms)) return kms;
            return 0;
        }

        /// <summary>The desktop's light / dark choice through the settings portal (GNOME 42+, KDE). Dark if it can't be read.</summary>
        public bool PrefersDark()
        {
            uint scheme;
            // 0 = no preference (GNOME's "Default", which is light), 1 = dark, 2 = light
            if (TryCall("org.freedesktop.portal.Desktop", "/org/freedesktop/portal/desktop", "org.freedesktop.portal.Settings", "ReadOne",
                (m, s) => m.GetBodyReader().ReadVariantValue().GetUInt32(), out scheme, "ss", "org.freedesktop.appearance", "color-scheme"))
                return scheme == 1;
            return true;
        }

        /// <summary>A blocking call with a short timeout, for the few synchronous questions Core asks.</summary>
        bool TryCall<T>(string dest, string path, string iface, string member, MessageValueReader<T> read, out T value,
            string sig = null, params string[] args)
        {
            value = default(T);
            try
            {
                MessageBuffer msg;
                using (var w = conn.GetMessageWriter())
                {
                    w.WriteMethodCallHeader(dest, path, iface, member, sig, MessageFlags.NoAutoStart);
                    foreach (var a in args) w.WriteString(a);
                    msg = w.CreateMessage();
                }
                var t = conn.CallMethodAsync(msg, read);
                if (!t.Wait(1500)) return false;
                value = t.Result;
                return true;
            }
            catch { return false; }
        }
    }
}
