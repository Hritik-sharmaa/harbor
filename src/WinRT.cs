using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Devices.Radios;

namespace Harbor
{
    /// <summary>Minimal async glue for WinRT (the in-box C# 5 compiler has no WinRT awaiters).</summary>
    static class Async
    {
        public static void Then<T>(IAsyncOperation<T> op, Action<T> done)
        {
            if (op == null) return;
            op.Completed = (o, s) =>
            {
                T r = default(T);
                try { if (s == AsyncStatus.Completed) r = o.GetResults(); } catch { }
                App.Post(() => done(r));
            };
        }

        /// <summary>Loads a WinRT image stream (album art, app logos) into a square GDI+ bitmap.</summary>
        public static void LoadBitmap(Windows.Storage.Streams.IRandomAccessStreamReference src, int px, Action<Bitmap> done)
        {
            if (src == null) { done(null); return; }
            try
            {
                Then(src.OpenReadAsync(), stream =>
                {
                    if (stream == null) { done(null); return; }
                    var reader = new Windows.Storage.Streams.DataReader(stream);
                    Then(reader.LoadAsync((uint)Math.Min(stream.Size, 8UL * 1024 * 1024)), n =>
                    {
                        Bitmap result = null;
                        try
                        {
                            var buf = new byte[n];
                            reader.ReadBytes(buf);
                            using (var ms = new MemoryStream(buf))
                            using (var b = new Bitmap(ms))
                                result = Gfx.Scale(b, px);
                        }
                        catch { }
                        finally { reader.Dispose(); stream.Dispose(); }
                        done(result);
                    });
                });
            }
            catch { done(null); }
        }
    }

    /// <summary>Now-playing info from any app that reports to Windows (Spotify, browsers, Media Player...).</summary>
    static class Media
    {
        public static string Title, Artist, SourceApp;
        public static bool Playing;
        public static Bitmap Art;
        public static event Action Changed;
        public static bool Demo; // --snapshot --demo: sample data for README screenshots
        public static bool Has { get { return (session != null || Demo) && !string.IsNullOrEmpty(Title); } }

        static GlobalSystemMediaTransportControlsSessionManager mgr;
        static GlobalSystemMediaTransportControlsSession session;
        static TypedEventHandler<GlobalSystemMediaTransportControlsSession, MediaPropertiesChangedEventArgs> propHandler;
        static TypedEventHandler<GlobalSystemMediaTransportControlsSession, PlaybackInfoChangedEventArgs> playHandler;
        static int loadSeq;

        public static void Init()
        {
            try
            {
                Async.Then(GlobalSystemMediaTransportControlsSessionManager.RequestAsync(), m =>
                {
                    if (m == null) return;
                    mgr = m;
                    mgr.CurrentSessionChanged += (a, b) => App.Post(Bind);
                    Bind();
                });
            }
            catch (Exception ex) { App.Log(ex); }
        }

        static void Bind()
        {
            GlobalSystemMediaTransportControlsSession s = null;
            try { s = mgr.GetCurrentSession(); } catch { }
            if (s != session)
            {
                try
                {
                    if (session != null)
                    {
                        session.MediaPropertiesChanged -= propHandler;
                        session.PlaybackInfoChanged -= playHandler;
                    }
                }
                catch { }
                session = s;
                if (s != null)
                {
                    propHandler = (a, b) => App.Post(Load);
                    playHandler = (a, b) => App.Post(LoadPlayback);
                    s.MediaPropertiesChanged += propHandler;
                    s.PlaybackInfoChanged += playHandler;
                }
            }
            Load();
        }

        static void LoadPlayback()
        {
            try { Playing = session != null && session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing; }
            catch { Playing = false; }
            Fire();
        }

        static void Load()
        {
            int seq = ++loadSeq;
            if (Demo) return;
            if (session == null) { Title = Artist = SourceApp = null; Playing = false; SetArt(null); Fire(); return; }
            try { SourceApp = session.SourceAppUserModelId; } catch { }
            LoadPlayback();
            try
            {
                Async.Then(session.TryGetMediaPropertiesAsync(), p =>
                {
                    if (seq != loadSeq) return;
                    if (p == null) return;
                    Title = p.Title;
                    Artist = p.Artist;
                    Fire();
                    if (p.Thumbnail == null) { SetArt(null); Fire(); return; }
                    Async.Then(p.Thumbnail.OpenReadAsync(), stream =>
                    {
                        if (stream == null || seq != loadSeq) return;
                        var reader = new Windows.Storage.Streams.DataReader(stream);
                        Async.Then(reader.LoadAsync((uint)Math.Min(stream.Size, 8UL * 1024 * 1024)), n =>
                        {
                            try
                            {
                                if (seq != loadSeq) return;
                                var buf = new byte[n];
                                reader.ReadBytes(buf);
                                using (var ms = new MemoryStream(buf))
                                using (var b = new Bitmap(ms))
                                    SetArt(Gfx.Scale(b, 96));
                                Fire();
                            }
                            catch { }
                            finally { reader.Dispose(); stream.Dispose(); }
                        });
                    });
                });
            }
            catch (Exception ex) { App.Log(ex); }
        }

        static void SetArt(Bitmap b)
        {
            var old = Art;
            Art = b;
            if (old != null) old.Dispose();
        }

        static void Fire() { if (Changed != null) Changed(); }

        public static void PlayPause()
        {
            try { if (session != null) { session.TryTogglePlayPauseAsync(); return; } } catch { }
            N.SendKey(0xB3);
        }

        public static void Next()
        {
            try { if (session != null) { session.TrySkipNextAsync(); return; } } catch { }
            N.SendKey(0xB0);
        }

        public static void Previous()
        {
            try { if (session != null) { session.TrySkipPreviousAsync(); return; } } catch { }
            N.SendKey(0xB1);
        }

        public static string AppName
        {
            get
            {
                if (string.IsNullOrEmpty(SourceApp)) return "";
                if (SourceApp.Contains("!")) return ShellIcons.DisplayName(@"shell:AppsFolder\" + SourceApp);
                string s = SourceApp;
                if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 4);
                return s.Length > 0 ? char.ToUpper(s[0]) + s.Substring(1) : s;
            }
        }
    }

    /// <summary>Wi-Fi / Bluetooth radios on-off (Windows.Devices.Radios).</summary>
    static class Radios
    {
        static Radio wifi, bt;
        public static bool WifiOn = true, BtOn, HasWifi, HasBt;
        public static event Action Changed;

        public static void Init()
        {
            try
            {
                Async.Then(Radio.RequestAccessAsync(), access =>
                {
                    Async.Then(Radio.GetRadiosAsync(), list =>
                    {
                        if (list == null) return;
                        foreach (var r in list)
                        {
                            if (r.Kind == RadioKind.WiFi && wifi == null) wifi = r;
                            else if (r.Kind == RadioKind.Bluetooth && bt == null) bt = r;
                            else continue;
                            r.StateChanged += (a, b) => App.Post(Update);
                        }
                        Update();
                    });
                });
            }
            catch (Exception ex) { App.Log(ex); }
        }

        static void Update()
        {
            HasWifi = wifi != null;
            HasBt = bt != null;
            try { WifiOn = wifi == null || wifi.State == RadioState.On; } catch { }
            try { BtOn = bt != null && bt.State == RadioState.On; } catch { }
            if (Changed != null) Changed();
        }

        public static void SetWifi(bool on) { Set(wifi, on); }
        public static void SetBluetooth(bool on) { Set(bt, on); }

        static void Set(Radio r, bool on)
        {
            if (r == null) return;
            try { Async.Then(r.SetStateAsync(on ? RadioState.On : RadioState.Off), res => Update()); }
            catch (Exception ex) { App.Log(ex); }
        }
    }

    sealed class BtDevice { public string Id, Name; public bool Connected; }

    static class BtDevices
    {
        public static List<BtDevice> List = new List<BtDevice>();
        public static bool Loading;

        public static void Refresh(Action done)
        {
            try
            {
                Loading = true;
                string sel = Windows.Devices.Bluetooth.BluetoothDevice.GetDeviceSelectorFromPairingState(true);
                Async.Then(Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(sel, new[] { "System.Devices.Aep.IsConnected" }), coll =>
                {
                    Loading = false;
                    var l = new List<BtDevice>();
                    if (coll != null)
                        foreach (var d in coll)
                        {
                            object c;
                            d.Properties.TryGetValue("System.Devices.Aep.IsConnected", out c);
                            l.Add(new BtDevice { Id = d.Id, Name = d.Name, Connected = c is bool && (bool)c });
                        }
                    l.Sort((a, b) => b.Connected.CompareTo(a.Connected));
                    List = l;
                    if (done != null) done();
                });
            }
            catch (Exception ex) { Loading = false; App.Log(ex); }
        }
    }
}
