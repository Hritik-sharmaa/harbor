using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Harbor
{
    /// <summary>
    /// Sound outputs (speakers, headphones, HDMI...): lists the active ones and makes one the default, the way the
    /// Windows sound settings do. Listed only when a panel asks, nothing runs in the background.
    /// </summary>
    static class AudioOutputs
    {
        public sealed class Device
        {
            public string Id, Name;
            public bool Default;
        }

        [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceCollection
        {
            [PreserveSig] int GetCount(out uint n);
            [PreserveSig] int Item(uint i, out IMMDevice dev);
        }

        // The interface Windows' own sound settings use to change the default device. It isn't in the SDK headers,
        // but it has been the same since Windows 7 (EarTrumpet, SoundSwitch and others rely on it too).
        [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
        class PolicyConfigClient { }

        [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPolicyConfig
        {
            [PreserveSig] int GetMixFormat(IntPtr a, IntPtr b);
            [PreserveSig] int GetDeviceFormat(IntPtr a, int b, IntPtr c);
            [PreserveSig] int ResetDeviceFormat(IntPtr a);
            [PreserveSig] int SetDeviceFormat(IntPtr a, IntPtr b, IntPtr c);
            [PreserveSig] int GetProcessingPeriod(IntPtr a, int b, IntPtr c, IntPtr d);
            [PreserveSig] int SetProcessingPeriod(IntPtr a, IntPtr b);
            [PreserveSig] int GetShareMode(IntPtr a, IntPtr b);
            [PreserveSig] int SetShareMode(IntPtr a, IntPtr b);
            [PreserveSig] int GetPropertyValue(IntPtr a, IntPtr b, IntPtr c);
            [PreserveSig] int SetPropertyValue(IntPtr a, IntPtr b, IntPtr c);
            [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
        }

        public static bool Demo;

        /// <summary>The active outputs, the default one marked.</summary>
        public static List<Device> List()
        {
            var list = new List<Device>();
            if (Demo)
            {
                list.Add(new Device { Id = "1", Name = "Speakers", Default = true });
                list.Add(new Device { Id = "2", Name = "Headphones" });
                return list;
            }
            IMMDeviceEnumerator en = null;
            try
            {
                en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCo();
                string def = null;
                IMMDevice d;
                if (en.GetDefaultAudioEndpoint(0, 1, out d) == 0 && d != null) { d.GetId(out def); Marshal.ReleaseComObject(d); }
                IntPtr p;
                if (en.EnumAudioEndpoints(0 /*render*/, 1 /*active*/, out p) != 0 || p == IntPtr.Zero) return list;
                var col = (IMMDeviceCollection)Marshal.GetObjectForIUnknown(p);
                Marshal.Release(p);
                uint n;
                col.GetCount(out n);
                for (uint i = 0; i < n; i++)
                {
                    IMMDevice dev;
                    if (col.Item(i, out dev) != 0 || dev == null) continue;
                    string id;
                    dev.GetId(out id);
                    string name = Volume.ReadName(dev);
                    list.Add(new Device { Id = id, Name = string.IsNullOrEmpty(name) ? "Output " + (i + 1) : Short(name), Default = id == def });
                    Marshal.ReleaseComObject(dev);
                }
                Marshal.ReleaseComObject(col);
            }
            catch (Exception ex) { App.Log(ex); }
            finally { if (en != null) Marshal.ReleaseComObject(en); }
            return list;
        }

        /// <summary>"Speakers (Realtek(R) Audio)" → "Speakers (Realtek Audio)": names are shown in small spaces.</summary>
        static string Short(string s)
        {
            return s.Replace("(R)", "").Replace("(TM)", "").Replace("  ", " ").Trim();
        }

        /// <summary>Makes it the default output for everything (music, calls, system sounds).</summary>
        public static bool SetDefault(Device d)
        {
            if (Demo) return true;
            object o = null;
            try
            {
                o = new PolicyConfigClient();
                var pc = (IPolicyConfig)o;
                bool ok = true;
                for (int role = 0; role < 3; role++) ok &= pc.SetDefaultEndpoint(d.Id, role) == 0; // console, multimedia, communications
                Volume.Init(); // the volume slider follows the new device
                return ok;
            }
            catch (Exception ex) { App.Log(ex); return false; }
            finally { if (o != null) Marshal.ReleaseComObject(o); }
        }

        /// <summary>Switches to the next output (for one-click switching in small places).</summary>
        public static Device Next()
        {
            var l = List();
            if (l.Count < 2) return null;
            int i = l.FindIndex(x => x.Default);
            var next = l[(i + 1) % l.Count];
            return SetDefault(next) ? next : null;
        }
    }
}
