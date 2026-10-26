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
        public static string Title, Artist, SourceApp, Album;
        public static bool Playing;
        public static TimeSpan Duration;                 // zero when the app doesn't report a timeline
        static TimeSpan position;
        static DateTimeOffset positionAt;
        public static Bitmap Art;
        public static event Action Changed;
        public static bool Demo; // --snapshot --demo: sample data for README screenshots
        public static bool Has { get { return (session != null || Demo) && !string.IsNullOrEmpty(Title); } }

        static GlobalSystemMediaTransportControlsSessionManager mgr;
        static GlobalSystemMediaTransportControlsSession session;
        static TypedEventHandler<GlobalSystemMediaTransportControlsSession, MediaPropertiesChangedEventArgs> propHandler;
        static TypedEventHandler<GlobalSystemMediaTransportControlsSession, PlaybackInfoChangedEventArgs> playHandler;
        static TypedEventHandler<GlobalSystemMediaTransportControlsSession, TimelinePropertiesChangedEventArgs> timeHandler;
        static int loadSeq;

        /// <summary>Where the song is right now: the last reported position plus the time it has been playing since.</summary>
        public static TimeSpan Position
        {
            get
            {
                if (Duration <= TimeSpan.Zero) return TimeSpan.Zero;
                var p = position;
                if (Playing && positionAt != default(DateTimeOffset)) p += DateTimeOffset.Now - positionAt;
                return p < TimeSpan.Zero ? TimeSpan.Zero : (p > Duration ? Duration : p);
            }
        }

        /// <summary>Reads the app's timeline. A report older than what we already know (some apps don't re-stamp on pause) is ignored unless 'fresh' (new song).</summary>
        static void LoadTimeline(bool fresh = false)
        {
            if (Demo) return;
            try
            {
                if (session == null) { Duration = TimeSpan.Zero; return; }
                var t = session.GetTimelineProperties();
                Duration = t.EndTime - t.StartTime;
                var stamp = t.LastUpdatedTime;
                bool unstamped = stamp.Year < 2000 || stamp > DateTimeOffset.Now;
                if (unstamped) stamp = DateTimeOffset.Now;
                if (fresh || unstamped || stamp >= positionAt)
                {
                    position = t.Position - t.StartTime;
                    positionAt = stamp;
                }
            }
            catch { Duration = TimeSpan.Zero; }
        }

        /// <summary>True while the Now Playing panel is open: only then is the app's stream of position updates worth reading.</summary>
        public static bool WantTimeline;

        public static void RefreshTimeline() { LoadTimeline(); }

        /// <summary>--snapshot --demo: a sample position in a sample song.</summary>
        public static void DemoTimeline(TimeSpan at, TimeSpan length)
        {
            Duration = length;
            position = at;
            positionAt = DateTimeOffset.Now;
        }

        public static void Seek(TimeSpan to)
        {
            if (session == null || Duration <= TimeSpan.Zero) return;
            try
            {
                var t = session.GetTimelineProperties();
                session.TryChangePlaybackPositionAsync((t.StartTime + to).Ticks);
                position = to;           // show it straight away; the app confirms with a timeline event
                positionAt = DateTimeOffset.Now;
            }
            catch { }
        }

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
                        session.TimelinePropertiesChanged -= timeHandler;
                    }
                }
                catch { }
                session = s;
                if (s != null)
                {
                    propHandler = (a, b) => App.Post(Load);
                    playHandler = (a, b) => App.Post(LoadPlayback);
                    // apps report the position very often while playing: just note it. Nothing in the menu bar shows it,
                    // and the Now Playing panel redraws on its own clock while it's open.
                    timeHandler = (a, b) => { if (WantTimeline) App.Post(() => LoadTimeline()); };
                    s.MediaPropertiesChanged += propHandler;
                    s.PlaybackInfoChanged += playHandler;
                    try { s.TimelinePropertiesChanged += timeHandler; } catch { }
                }
            }
            Load();
        }

        static void LoadPlayback()
        {
            if (Demo) return;
            var keep = Position; // pausing freezes the clock where it is
            try { Playing = session != null && session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing; }
            catch { Playing = false; }
            if (Duration > TimeSpan.Zero) { position = keep; positionAt = DateTimeOffset.Now; }
            LoadTimeline();
            Fire();
        }

        static void Load()
        {
            int seq = ++loadSeq;
            if (Demo) return;
            if (session == null) { Title = Artist = SourceApp = Album = null; Playing = false; Duration = TimeSpan.Zero; SetArt(null); Fire(); return; }
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
                    Album = p.AlbumTitle;
                    LoadTimeline(true);
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
                                    SetArt(Gfx.Scale(b, 240)); // sharp in the bigger Now Playing cover
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
