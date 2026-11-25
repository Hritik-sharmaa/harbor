using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Tmds.DBus.Protocol;

namespace Harbor
{
    static class Program
    {
        const string Usage =
@"harbord - Harbor's Linux helper. It keeps reminders, timers, Pomodoro, Focus, weather, lyrics, calendar and
Claude usage running, and serves them on the D-Bus session bus (io.github.hritik_sharmaa.Harbor) to Harbor's
GNOME Shell extension. You don't normally start it yourself: D-Bus starts it the first time the extension asks.

  harbord                 run (exits if another harbord already runs)
  harbord --config DIR    keep settings in DIR instead of ~/.config/harbor
  harbord --selftest      check the shared code on this machine, without D-Bus (exit code = failures)
  harbord --version

Try it:  gdbus call --session --dest io.github.hritik_sharmaa.Harbor --object-path /io/github/hritik_sharmaa/Harbor \
           --method io.github.hritik_sharmaa.Harbor1.Get all";

        static int Main(string[] args)
        {
            if (Has(args, "--help") || Has(args, "-h")) { Console.WriteLine(Usage); return 0; }
            if (Has(args, "--version")) { Console.WriteLine("harbord " + AppInfo.Version); return 0; }

            var loop = new MainLoop();
            var platform = new LinuxPlatform(loop);
            Core.Platform = platform;
            string cfg = Arg(args, "--config");
            if (cfg != null) platform.ConfigDir = Path.GetFullPath(cfg);

            if (Has(args, "--selftest")) return SelfTest.Run(loop, platform);

            Bus bus = null;
            var api = new Api(platform, section => { if (bus != null) bus.EmitChanged(section); });
            DBusConnection conn;
            try
            {
                conn = new DBusConnection(DBusAddress.Session);
                conn.ConnectAsync().AsTask().GetAwaiter().GetResult();
                bus = new Bus(conn, loop, api);
                if (!bus.StartAsync().GetAwaiter().GetResult())
                {
                    Console.Error.WriteLine("harbord: already running (" + Bus.Name + " is taken)");
                    return 0;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("harbord: couldn't connect to the D-Bus session bus: " + ex.Message);
                return 1;
            }
            platform.Bus = bus;

            // the session ending, or a polite kill, stops the loop
            conn.DisconnectedAsync().ContinueWith(_ => loop.Stop());
            using (PosixSignalRegistration.Create(PosixSignal.SIGTERM, c => { c.Cancel = true; loop.Stop(); }))
            using (PosixSignalRegistration.Create(PosixSignal.SIGINT, c => { c.Cancel = true; loop.Stop(); }))
            {
                loop.Enqueue(() =>
                {
                    Settings.Load();
                    api.Start();
                    Console.Error.WriteLine("harbord " + AppInfo.Version + ": serving " + Bus.Name + ", settings in " + Settings.Dir);
                });
                loop.Run();
            }
            conn.Dispose();
            return 0;
        }

        static bool Has(string[] args, string flag) { return Array.IndexOf(args, flag) >= 0; }
        static string Arg(string[] args, string flag) { int i = Array.IndexOf(args, flag); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
    }
}
