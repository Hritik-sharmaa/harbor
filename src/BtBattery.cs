using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Harbor
{
    /// <summary>
    /// Battery level of Bluetooth headphones / earbuds. Windows stores it on the device's hands-free
    /// node (the same number shown in Settings › Bluetooth). Read off the UI thread, refreshed every minute.
    /// </summary>
    static class BtBattery
    {
        public static Dictionary<string, int> Levels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public static string ConnectedName;
        public static int ConnectedLevel = -1;
        public static event Action Changed;

        static System.Windows.Forms.Timer timer;
        static int busy;

        public static void Init()
        {
            timer = new System.Windows.Forms.Timer { Interval = 60000 };
            timer.Tick += delegate { Refresh(); };
            timer.Start();
            Refresh();
            WatchConnections();
        }

        static Windows.Devices.Enumeration.DeviceWatcher watcher;

        /// <summary>Reacts the moment headphones connect / disconnect (instead of waiting for the next minute).</summary>
        static void WatchConnections()
        {
            try
            {
                string sel = Windows.Devices.Bluetooth.BluetoothDevice.GetDeviceSelectorFromPairingState(true);
                watcher = Windows.Devices.Enumeration.DeviceInformation.CreateWatcher(sel, new[] { "System.Devices.Aep.IsConnected" },
                    Windows.Devices.Enumeration.DeviceInformationKind.AssociationEndpoint);
                watcher.Added += (s, d) => { };
                watcher.Removed += (s, d) => { };
                watcher.Updated += (s, u) => App.Post(() =>
                {
                    // the battery value appears a moment after the headset connects
                    Refresh();
                    var t = new System.Windows.Forms.Timer { Interval = 6000 };
                    t.Tick += delegate { t.Stop(); t.Dispose(); Refresh(); };
                    t.Start();
                });
                watcher.Start();
            }
            catch (Exception ex) { App.Log(ex); }
        }

        public static int LevelFor(string deviceName)
        {
            int v;
            return deviceName != null && Levels.TryGetValue(deviceName, out v) ? v : -1;
        }

        public static void Refresh()
        {
            if (!Look.Bool("BarHeadphones") && !Flyout.AnyOpen) return;
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                Dictionary<string, int> levels = null;
                try { levels = Read(); } catch { }
                App.Post(() =>
                {
                    Interlocked.Exchange(ref busy, 0);
                    if (levels == null) return;
                    Levels = levels;
                    // which of them is actually connected right now?
                    BtDevices.Refresh(() =>
                    {
                        string name = null;
                        int level = -1;
                        if (Radios.BtOn || !Radios.HasBt)
                            foreach (var d in BtDevices.List)
                            {
                                if (!d.Connected) continue;
                                int v = LevelFor(d.Name);
                                if (v >= 0) { name = d.Name; level = v; break; }
                            }
                        bool changed = name != ConnectedName || level != ConnectedLevel;
                        ConnectedName = name;
                        ConnectedLevel = level;
                        if (changed && Changed != null) Changed();
                    });
                });
            });
        }

        static readonly string[] suffixes = { " Hands-Free AG", " Hands-Free", " Avrcp Transport", " Stereo", " LE" };

        static Dictionary<string, int> Read()
        {
            var res = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            IntPtr set = N.SetupDiGetClassDevs(IntPtr.Zero, null, IntPtr.Zero, 0x2 | 0x4); // present, all classes
            if (set == IntPtr.Zero || set == new IntPtr(-1)) return res;
            try
            {
                var battKey = new DEVPROPKEY { fmtid = new Guid("104EA319-6EE2-4701-BD47-8DDBF425BBE5"), pid = 2 };
                var nameKey = new DEVPROPKEY { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };
                var buf = new byte[512];
                for (uint i = 0; ; i++)
                {
                    var d = new SP_DEVINFO_DATA();
                    d.cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(d);
                    if (!N.SetupDiEnumDeviceInfo(set, i, ref d)) break;
                    uint type, req;
                    if (!N.SetupDiGetDeviceProperty(set, ref d, ref battKey, out type, buf, (uint)buf.Length, out req, 0) || req < 1) continue;
                    int level = buf[0];
                    if (level > 100) continue;
                    if (!N.SetupDiGetDeviceProperty(set, ref d, ref nameKey, out type, buf, (uint)buf.Length, out req, 0) || req < 2) continue;
                    string name = Encoding.Unicode.GetString(buf, 0, (int)req).TrimEnd('\0');
                    foreach (var s in suffixes)
                        if (name.EndsWith(s, StringComparison.OrdinalIgnoreCase)) { name = name.Substring(0, name.Length - s.Length); break; }
                    res[name] = level;
                }
            }
            finally { N.SetupDiDestroyDeviceInfoList(set); }
            return res;
        }
    }
}
