using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Harbor
{
    /// <summary>Master volume through Core Audio, with change notifications (no polling).</summary>
    static class Volume
    {
        sealed class Callback : IAudioEndpointVolumeCallback
        {
            public int OnNotify(IntPtr data)
            {
                try
                {
                    Muted = Marshal.ReadInt32(data, 16) != 0;
                    Level = BitConverter.ToSingle(BitConverter.GetBytes(Marshal.ReadInt32(data, 20)), 0);
                    App.Post(() => { if (Changed != null) Changed(); });
                }
                catch { }
                return 0;
            }
        }

        public static float Level;
        public static bool Muted;
        public static bool Available;
        public static event Action Changed;
        static IAudioEndpointVolume ep;
        static readonly Callback cb = new Callback();
        static Guid ctx = Guid.Empty;

        /// <summary>(Re)binds to the default output device.</summary>
        public static void Init()
        {
            try
            {
                if (ep != null) { try { ep.UnregisterControlChangeNotify(cb); } catch { } Marshal.ReleaseComObject(ep); ep = null; }
                var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCo();
                IMMDevice dev;
                if (en.GetDefaultAudioEndpoint(0, 1, out dev) != 0 || dev == null) { Available = false; return; }
                Guid iid = typeof(IAudioEndpointVolume).GUID;
                object o;
                dev.Activate(ref iid, 23, IntPtr.Zero, out o);
                ep = (IAudioEndpointVolume)o;
                ep.RegisterControlChangeNotify(cb);
                DeviceName = ReadName(dev);
                if (device != null) Marshal.ReleaseComObject(device);
                device = dev;
                Marshal.ReleaseComObject(en);
                Read();
                Available = true;
            }
            catch { Available = false; }
        }

        static IMMDevice device;
        public static string DeviceName = "";

        internal static string ReadName(IMMDevice dev)
        {
            IPropertyStore ps = null;
            try
            {
                if (dev.OpenPropertyStore(0, out ps) != 0 || ps == null) return "";
                var key = new PROPERTYKEY { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };
                PROPVARIANT pv;
                if (ps.GetValue(ref key, out pv) != 0) return "";
                string s = pv.vt == 31 ? Marshal.PtrToStringUni(pv.p) : "";
                N.PropVariantClear(ref pv);
                return s ?? "";
            }
            catch { return ""; }
            finally { if (ps != null) Marshal.ReleaseComObject(ps); }
        }

        /// <summary>Apps currently playing (or able to play) sound, with their own volume.</summary>
        public sealed class AppSession
        {
            public uint Pid;
            public string Name, Exe;
            public ISimpleAudioVolume Vol;
            public float Level;
            public bool Muted;
        }

        public static List<AppSession> Sessions()
        {
            var list = new List<AppSession>();
            if (device == null) return list;
            try
            {
                Guid iid = typeof(IAudioSessionManager2).GUID;
                object o;
                if (device.Activate(ref iid, 23, IntPtr.Zero, out o) != 0) return list;
                var mgr = (IAudioSessionManager2)o;
                IAudioSessionEnumerator en;
                if (mgr.GetSessionEnumerator(out en) != 0) return list;
                int n;
                en.GetCount(out n);
                var seen = new HashSet<uint>();
                for (int i = 0; i < n; i++)
                {
                    IAudioSessionControl2 s;
                    if (en.GetSession(i, out s) != 0 || s == null) continue;
                    int state;
                    s.GetState(out state);
                    uint pid;
                    s.GetProcessId(out pid);
                    if (state == 2 || pid == 0 || s.IsSystemSoundsSession() == 0 || !seen.Add(pid)) { Marshal.ReleaseComObject(s); continue; }
                    var a = new AppSession { Pid = pid, Vol = (ISimpleAudioVolume)s };
                    try
                    {
                        using (var p = System.Diagnostics.Process.GetProcessById((int)pid))
                        {
                            try { a.Exe = p.MainModule.FileName; } catch { }
                            a.Name = a.Exe != null ? ShellIcons.DisplayName(a.Exe) : p.ProcessName;
                        }
                    }
                    catch { continue; }
                    a.Vol.GetMasterVolume(out a.Level);
                    a.Vol.GetMute(out a.Muted);
                    list.Add(a);
                }
            }
            catch (Exception ex) { App.Log(ex); }
            list.Sort((x, y) => string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        public static void SetApp(AppSession a, float v)
        {
            a.Level = Math.Max(0, Math.Min(1, v));
            try { a.Vol.SetMasterVolume(a.Level, ref ctx); if (a.Muted && v > 0) { a.Vol.SetMute(false, ref ctx); a.Muted = false; } } catch { }
        }

        public static void Read()
        {
            if (ep == null) return;
            try { float v; ep.GetMasterVolumeLevelScalar(out v); Level = v; bool m; ep.GetMute(out m); Muted = m; }
            catch { }
        }

        public static void Set(float v)
        {
            if (ep == null) return;
            v = Math.Max(0, Math.Min(1, v));
            Level = v;
            try
            {
                ep.SetMasterVolumeLevelScalar(v, ref ctx);
                if (Muted && v > 0) { ep.SetMute(false, ref ctx); Muted = false; }
            }
            catch { }
        }

        public static void ToggleMute()
        {
            if (ep == null) return;
            try { Muted = !Muted; ep.SetMute(Muted, ref ctx); } catch { }
        }

        public static string Glyph
        {
            get
            {
                if (!Available) return "";
                if (Muted || Level <= 0.001f) return "";
                if (Level < 0.34f) return "";
                if (Level < 0.67f) return "";
                return "";
            }
        }
    }

    /// <summary>Connected Wi-Fi name and signal via the native WLAN API.</summary>
    static class Wifi
    {
        public static string Ssid;
        public static int Signal; // 0..100, -1 = not on wifi

        public static bool Demo; // --snapshot --demo: keep the sample network, never read the real one

        public static void Query()
        {
            if (Demo) return;
            Ssid = null;
            Signal = -1;
            IntPtr h = IntPtr.Zero, list = IntPtr.Zero;
            try
            {
                uint ver;
                if (N.WlanOpenHandle(2, IntPtr.Zero, out ver, out h) != 0) return;
                if (N.WlanEnumInterfaces(h, IntPtr.Zero, out list) != 0) return;
                int count = Marshal.ReadInt32(list, 0);
                for (int i = 0; i < count; i++)
                {
                    IntPtr item = list + 8 + i * 532;
                    Guid g = (Guid)Marshal.PtrToStructure(item, typeof(Guid));
                    int state = Marshal.ReadInt32(item, 16 + 512);
                    if (state != 1) continue; // wlan_interface_state_connected
                    int size;
                    IntPtr data;
                    if (N.WlanQueryInterface(h, ref g, 7, IntPtr.Zero, out size, out data, IntPtr.Zero) != 0) continue;
                    try
                    {
                        int len = Math.Min(32, Marshal.ReadInt32(data, 520));
                        var bytes = new byte[len];
                        Marshal.Copy(data + 524, bytes, 0, len);
                        Ssid = Encoding.UTF8.GetString(bytes);
                        Signal = Marshal.ReadInt32(data, 576);
                    }
                    finally { N.WlanFreeMemory(data); }
                    return;
                }
            }
            catch { }
            finally
            {
                if (list != IntPtr.Zero) N.WlanFreeMemory(list);
                if (h != IntPtr.Zero) N.WlanCloseHandle(h, IntPtr.Zero);
            }
        }

        public sealed class Net
        {
            public string Ssid, Profile;
            public int Signal, Auth, Cipher;
            public bool Secure, Connected, HasProfile;
        }

        public static List<Net> Networks = new List<Net>();

        /// <summary>Runs f on the first Wi-Fi adapter. Returns false if there is none.</summary>
        static bool WithAdapter(Action<IntPtr, Guid> f)
        {
            IntPtr h = IntPtr.Zero, list = IntPtr.Zero;
            try
            {
                uint ver;
                if (N.WlanOpenHandle(2, IntPtr.Zero, out ver, out h) != 0) return false;
                if (N.WlanEnumInterfaces(h, IntPtr.Zero, out list) != 0) return false;
                if (Marshal.ReadInt32(list, 0) < 1) return false;
                Guid g = (Guid)Marshal.PtrToStructure(list + 8, typeof(Guid));
                f(h, g);
                return true;
            }
            catch (Exception ex) { App.Log(ex); return false; }
            finally
            {
                if (list != IntPtr.Zero) N.WlanFreeMemory(list);
                if (h != IntPtr.Zero) N.WlanCloseHandle(h, IntPtr.Zero);
            }
        }

        public static void Scan()
        {
            if (Demo) return;
            WithAdapter((h, g) => N.WlanScan(h, ref g, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero));
        }

        public static void LoadNetworks()
        {
            if (Demo)
            {
                Networks = new List<Net>
                {
                    new Net { Ssid = "Home Wi-Fi", Signal = 92, Secure = true, Connected = true, HasProfile = true },
                    new Net { Ssid = "Studio 5G", Signal = 74, Secure = true },
                    new Net { Ssid = "Cafe Guest", Signal = 48 },
                };
                return;
            }
            var result = new List<Net>();
            WithAdapter((h, g) =>
            {
                IntPtr list;
                if (N.WlanGetAvailableNetworkList(h, ref g, 0, IntPtr.Zero, out list) != 0) return;
                try
                {
                    int n = Marshal.ReadInt32(list, 0);
                    var bySsid = new Dictionary<string, Net>();
                    for (int i = 0; i < n; i++)
                    {
                        IntPtr p = list + 8 + i * 628;
                        int len = Math.Min(32, Marshal.ReadInt32(p, 512));
                        if (len == 0) continue; // hidden network
                        var bytes = new byte[len];
                        Marshal.Copy(p + 516, bytes, 0, len);
                        var net = new Net
                        {
                            Ssid = Encoding.UTF8.GetString(bytes),
                            Profile = Marshal.PtrToStringUni(p),
                            Signal = Marshal.ReadInt32(p, 604),
                            Secure = Marshal.ReadInt32(p, 608) != 0,
                            Auth = Marshal.ReadInt32(p, 612),
                            Cipher = Marshal.ReadInt32(p, 616),
                        };
                        int flags = Marshal.ReadInt32(p, 620);
                        net.Connected = (flags & 1) != 0;
                        net.HasProfile = (flags & 2) != 0 && !string.IsNullOrEmpty(net.Profile);
                        Net prev;
                        if (bySsid.TryGetValue(net.Ssid, out prev))
                        {
                            // the list repeats SSIDs (with/without profile): merge
                            prev.Connected |= net.Connected;
                            prev.Signal = Math.Max(prev.Signal, net.Signal);
                            if (net.HasProfile && !prev.HasProfile) { prev.HasProfile = true; prev.Profile = net.Profile; }
                            continue;
                        }
                        bySsid[net.Ssid] = net;
                        result.Add(net);
                    }
                }
                finally { N.WlanFreeMemory(list); }
            });
            result.Sort((a, b) => a.Connected != b.Connected ? b.Connected.CompareTo(a.Connected) : b.Signal.CompareTo(a.Signal));
            Networks = result;
        }

        static string Xml(string s)
        {
            return System.Security.SecurityElement.Escape(s);
        }

        /// <summary>Connects using a saved profile, or creates one (open / WPA2 / WPA3 personal) with the password.</summary>
        public static string Connect(Net net, string password)
        {
            string error = null;
            bool ok = WithAdapter((h, g) =>
            {
                string profile = net.HasProfile ? net.Profile : null;
                if (profile == null)
                {
                    string auth, enc;
                    switch (net.Auth)
                    {
                        case 1: auth = "open"; enc = "none"; break;          // 802.11 open
                        case 4: auth = "WPAPSK"; enc = net.Cipher == 2 ? "TKIP" : "AES"; break;
                        case 9: auth = "WPA3SAE"; enc = "AES"; break;
                        default: auth = "WPA2PSK"; enc = net.Cipher == 2 ? "TKIP" : "AES"; break;
                    }
                    if (net.Secure && string.IsNullOrEmpty(password)) { error = "Password required"; return; }
                    string hex = BitConverter.ToString(Encoding.UTF8.GetBytes(net.Ssid)).Replace("-", "");
                    var sb = new StringBuilder();
                    sb.Append("<?xml version=\"1.0\"?><WLANProfile xmlns=\"http://www.microsoft.com/networking/WLAN/profile/v1\">");
                    sb.Append("<name>").Append(Xml(net.Ssid)).Append("</name><SSIDConfig><SSID><hex>").Append(hex).Append("</hex><name>")
                      .Append(Xml(net.Ssid)).Append("</name></SSID></SSIDConfig><connectionType>ESS</connectionType><connectionMode>auto</connectionMode>");
                    sb.Append("<MSM><security><authEncryption><authentication>").Append(auth).Append("</authentication><encryption>").Append(enc)
                      .Append("</encryption><useOneX>false</useOneX></authEncryption>");
                    if (net.Secure)
                        sb.Append("<sharedKey><keyType>passPhrase</keyType><protected>false</protected><keyMaterial>").Append(Xml(password))
                          .Append("</keyMaterial></sharedKey>");
                    sb.Append("</security></MSM></WLANProfile>");
                    uint reason;
                    int rc = N.WlanSetProfile(h, ref g, 0, sb.ToString(), null, true, IntPtr.Zero, out reason);
                    if (rc != 0) { error = net.Secure ? "That password doesn't look right" : "Couldn't save this network"; return; }
                    profile = net.Ssid;
                }
                var cp = new WLAN_CONNECTION_PARAMETERS { wlanConnectionMode = 0, strProfile = profile, dot11BssType = 1 };
                if (N.WlanConnect(h, ref g, ref cp, IntPtr.Zero) != 0) error = "Couldn't connect";
            });
            if (!ok) return "No Wi-Fi adapter";
            return error;
        }

        public static void Disconnect()
        {
            WithAdapter((h, g) => N.WlanDisconnect(h, ref g, IntPtr.Zero));
        }

        public static string SignalGlyph(int signal)
        {
            if (signal >= 75) return "";
            if (signal >= 50) return "";
            if (signal >= 25) return "";
            return "";
        }

        /// <summary>Glyph showing signal strength (drawn over a dim full-strength glyph).</summary>
        public static string BarsGlyph
        {
            get
            {
                if (Signal < 0 || Signal >= 75) return "";
                if (Signal >= 50) return "";
                if (Signal >= 25) return "";
                return "";
            }
        }
    }

    static class Bluetooth
    {
        public static bool On()
        {
            try
            {
                int p = 4;
                IntPtr radio;
                IntPtr find = N.BluetoothFindFirstRadio(ref p, out radio);
                if (find == IntPtr.Zero) return false;
                N.CloseHandle(radio);
                N.BluetoothFindRadioClose(find);
                return true;
            }
            catch { return false; }
        }
    }

    /// <summary>Software dimming via the display's gamma ramp: works on any screen (used when DDC/WMI can't).</summary>
    static class Gamma
    {
        static readonly Dictionary<string, int> levels = new Dictionary<string, int>();

        public static int Get(string device) { int v; return levels.TryGetValue(device, out v) ? v : 100; }

        public static void Set(string device, int v)
        {
            v = Math.Max(0, Math.Min(100, v));
            IntPtr dc = N.CreateDC(null, device, null, IntPtr.Zero);
            if (dc == IntPtr.Zero) return;
            try
            {
                // Windows rejects extreme ramps, so 0% maps to ~40% light output
                double k = 0.40 + 0.60 * v / 100.0;
                var ramp = new GAMMARAMP { Values = new ushort[768] };
                for (int i = 0; i < 256; i++)
                {
                    ushort val = (ushort)Math.Min(65535, i * 256 * k);
                    ramp.Values[i] = ramp.Values[i + 256] = ramp.Values[i + 512] = val;
                }
                if (N.SetDeviceGammaRamp(dc, ref ramp)) levels[device] = v;
            }
            finally { N.DeleteDC(dc); }
        }

        public static void ResetAll()
        {
            foreach (var d in new List<string>(levels.Keys)) Set(d, 100);
        }
    }

    /// <summary>
    /// Display brightness: DDC/CI for external monitors, WMI for the laptop panel, gamma dimming as a fallback
    /// so every screen gets a working slider. All hardware I/O happens off the UI thread.
    /// </summary>
    sealed class Brightness
    {
        public int Value = -1;   // -1 loading, else 0..100
        readonly IntPtr monitor;
        public readonly string Device;
        int mode;                // 1 ddc, 2 wmi, 3 gamma
        int pending = -1, busy;

        public Brightness(IntPtr hmon, string device = null) { monitor = hmon; Device = device; }

        public string Kind { get { return mode == 2 ? "Built-in display" : (mode == 3 ? "Software dimming" : "Display"); } }

        /// <summary>Resolves every display at once: DDC where possible; WMI goes to the single screen DDC can't reach (the laptop panel); the rest dim via gamma.</summary>
        public static bool Demo; // --snapshot --demo: fixed levels, no hardware I/O

        public static void LoadAll(List<Brightness> all, Action done)
        {
            if (Demo)
            {
                for (int i = 0; i < all.Count; i++) { all[i].mode = i == 0 ? 2 : 1; all[i].Value = i == 0 ? 72 : 55; }
                done();
                return;
            }
            ThreadPool.QueueUserWorkItem(delegate
            {
                var failed = new List<Brightness>();
                foreach (var b in all)
                {
                    int v = b.DdcGet();
                    if (v >= 0) { b.mode = 1; b.Value = v; } else failed.Add(b);
                }
                int w = failed.Count == 1 ? WmiGet() : -1;
                foreach (var b in failed)
                {
                    if (w >= 0) { b.mode = 2; b.Value = w; w = -1; }
                    else if (b.Device != null) { b.mode = 3; b.Value = Gamma.Get(b.Device); }
                }
                App.Post(done);
            });
        }

        public void Load(Action done)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                int v = DdcGet();
                if (v >= 0) mode = 1;
                else
                {
                    v = WmiGet();
                    if (v >= 0) mode = 2;
                }
                Value = v >= 0 ? v : -2;
                App.Post(done);
            });
        }

        public void Set(int v)
        {
            if (mode == 0) return;
            Value = Math.Max(0, Math.Min(100, v));
            if (mode == 3) { Gamma.Set(Device, Value); return; }
            pending = Value;
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                // apply the latest requested value until the slider stops moving
                int last = -1;
                while (true)
                {
                    int want = pending;
                    if (want == last) break;
                    last = want;
                    try { if (mode == 1) DdcSet(want); else WmiSet(want); } catch { }
                }
                Interlocked.Exchange(ref busy, 0);
            });
        }

        PHYSICAL_MONITOR[] Physical()
        {
            uint n;
            if (!N.GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out n) || n == 0) return null;
            var arr = new PHYSICAL_MONITOR[n];
            return N.GetPhysicalMonitorsFromHMONITOR(monitor, n, arr) ? arr : null;
        }

        int DdcGet()
        {
            try
            {
                var arr = Physical();
                if (arr == null) return -1;
                try
                {
                    uint min, cur, max;
                    if (N.GetMonitorBrightness(arr[0].hPhysicalMonitor, out min, out cur, out max) && max > min)
                        return (int)Math.Round((cur - min) * 100.0 / (max - min));
                }
                finally { N.DestroyPhysicalMonitors((uint)arr.Length, arr); }
            }
            catch { }
            return -1;
        }

        void DdcSet(int v)
        {
            var arr = Physical();
            if (arr == null) return;
            try
            {
                uint min, cur, max;
                if (!N.GetMonitorBrightness(arr[0].hPhysicalMonitor, out min, out cur, out max)) { min = 0; max = 100; }
                N.SetMonitorBrightness(arr[0].hPhysicalMonitor, (uint)(min + (max - min) * v / 100));
            }
            finally { N.DestroyPhysicalMonitors((uint)arr.Length, arr); }
        }

        // WMI lives in separate methods so System.Management only loads when it's actually needed
        static int WmiGet()
        {
            try { return WmiGetCore(); } catch { return -1; }
        }

        static int WmiGetCore()
        {
            using (var s = new System.Management.ManagementObjectSearcher("root\\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness"))
                foreach (System.Management.ManagementObject o in s.Get())
                    return Convert.ToInt32(o["CurrentBrightness"]);
            return -1;
        }

        static void WmiSet(int v)
        {
            using (var c = new System.Management.ManagementClass("root\\WMI", "WmiMonitorBrightnessMethods", null))
                foreach (System.Management.ManagementObject o in c.GetInstances())
                {
                    o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)v });
                    break;
                }
        }
    }
}
