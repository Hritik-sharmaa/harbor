using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Web.Script.Serialization;

namespace Harbor
{
    /// <summary>
    /// Weather from Open-Meteo (open-meteo.com: free, no account or key) for the city you type in Harbor → General.
    /// Only the city name (once, to find it) and its coordinates are sent. Refreshed at most every 20 minutes,
    /// and only while the island's Weather tab is being looked at.
    /// </summary>
    static class Weather
    {
        public sealed class Hour { public DateTime At; public double Temp; public int Code, Rain; public bool Day; }

        public sealed class Data
        {
            public DateTime Fetched;
            public double Temp, Feels, Wind, Pressure, Uv, High, Low;
            public int Humidity, Code, Rain;
            public bool Day;
            public DateTime Sunrise, Sunset;
            public List<Hour> Hours = new List<Hour>();
        }

        public static Data Now;
        public static bool Loading, Failed;
        public static string Error;
        public static event Action Changed;

        static bool demo;
        public static bool HasCity { get { return demo || (Look.Get("WeatherLat") != "" && Look.Get("WeatherLon") != ""); } }
        public static string Place { get { return demo ? "Lisbon, Portugal" : Look.Get("WeatherPlace"); } }

        /// <summary>--snapshot --demo: sample weather, no network.</summary>
        public static void UseDemo()
        {
            demo = true;
            var t = DateTime.Today;
            Now = new Data
            {
                Fetched = DateTime.Now, Temp = 21, Feels = 20, Humidity = 58, Wind = 12, Pressure = 1016, Uv = 4, High = 24, Low = 15,
                Code = 1, Day = true, Rain = 10, Sunrise = t.AddHours(7.4), Sunset = t.AddHours(18.6),
            };
            int[] codes = { 1, 1, 2, 2, 3, 3, 61, 80, 2, 1, 0, 0 };
            var h0 = DateTime.Now.Date.AddHours(DateTime.Now.Hour);
            for (int i = 0; i < 12; i++)
            {
                var at = h0.AddHours(i);
                Now.Hours.Add(new Hour { At = at, Temp = 21 + Math.Sin(i / 3.0) * 3 - i * 0.3, Code = codes[i], Day = at.Hour >= 7 && at.Hour < 19 });
            }
        }
        public static bool Fahrenheit { get { return Look.Get("WeatherUnit") == "F"; } }

        /// <summary>Fetch if there's nothing yet or it's older than 20 minutes (cheap to call every frame).</summary>
        public static void Ensure()
        {
            if (demo || Loading || !HasCity) return;
            if (Now != null && (DateTime.Now - Now.Fetched).TotalMinutes < 20) return;
            if (Failed && lastTry != DateTime.MinValue && (DateTime.Now - lastTry).TotalMinutes < 2) return; // don't hammer when offline
            Fetch();
        }

        static DateTime lastTry = DateTime.MinValue;

        static void Fetch()
        {
            Loading = true;
            lastTry = DateTime.Now;
            string lat = Look.Get("WeatherLat"), lon = Look.Get("WeatherLon");
            bool f = Fahrenheit;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                string err;
                Data d = Forecast(lat, lon, f, out err);
                App.Post(() =>
                {
                    Loading = false;
                    Failed = d == null;
                    Error = err;
                    if (d != null) Now = d;
                    if (Changed != null) Changed();
                });
            });
        }

        /// <summary>The forecast for a place (blocking: call off the UI thread). Null with an error message if it failed.</summary>
        static Data Forecast(string lat, string lon, bool fahrenheit, out string err)
        {
            err = null;
            try
            {
                int status;
                string url = "https://api.open-meteo.com/v1/forecast?latitude=" + lat + "&longitude=" + lon
                    + "&current=temperature_2m,relative_humidity_2m,apparent_temperature,is_day,weather_code,wind_speed_10m,pressure_msl"
                    + "&hourly=temperature_2m,weather_code,precipitation_probability,uv_index,is_day"
                    + "&daily=temperature_2m_max,temperature_2m_min,sunrise,sunset&timezone=auto&forecast_days=2"
                    + (fahrenheit ? "&temperature_unit=fahrenheit&wind_speed_unit=mph" : "");
                string json = Web.Get(url, out status);
                if (status == 200) return Parse(new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json));
                err = status == 0 ? "Couldn't reach the weather service" : "Weather service error (" + status + ")";
            }
            catch (Exception ex) { App.Log(ex); err = "Couldn't read the weather"; }
            return null;
        }

        /// <summary>Finds a city (blocking). Returns an error message, or null and the place's name and coordinates.</summary>
        static string Geocode(string name, out string place, out string lat, out string lon)
        {
            place = lat = lon = null;
            try
            {
                int status;
                string json = Web.Get("https://geocoding-api.open-meteo.com/v1/search?count=1&language=en&format=json&name=" + Uri.EscapeDataString(name), out status);
                if (status != 200) return "Couldn't reach the weather service";
                var j = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                object res;
                var list = j.TryGetValue("results", out res) ? res as IList : null;
                if (list == null || list.Count == 0) return "Couldn't find \"" + name + "\"";
                var r = (Dictionary<string, object>)list[0];
                object country;
                place = r["name"] as string;
                if (r.TryGetValue("country", out country) && country is string) place += ", " + country;
                lat = Num(r["latitude"]).ToString("0.####", CultureInfo.InvariantCulture);
                lon = Num(r["longitude"]).ToString("0.####", CultureInfo.InvariantCulture);
                return null;
            }
            catch (Exception ex) { App.Log(ex); return "Couldn't look that city up"; }
        }

        /// <summary>Developer check: Harbor.exe --weather-test &lt;out file&gt; &lt;city&gt; (nothing is saved).</summary>
        public static void Test(string outFile, string city)
        {
            string place, lat, lon, err = Geocode(city, out place, out lat, out lon);
            Data d = err == null ? Forecast(lat, lon, false, out err) : null;
            System.IO.File.WriteAllText(outFile, err != null ? "ERROR " + err
                : place + " (" + lat + ", " + lon + "): " + Math.Round(d.Temp) + "C feels " + Math.Round(d.Feels) + "C, " + Describe(d.Code)
                  + ", humidity " + d.Humidity + "%, wind " + Math.Round(d.Wind) + " km/h, UV " + Math.Round(d.Uv) + ", rain " + d.Rain + "%, "
                  + d.Hours.Count + " hours ahead, sunrise " + d.Sunrise.ToString("HH:mm") + ", sunset " + d.Sunset.ToString("HH:mm"));
        }

        static Data Parse(Dictionary<string, object> j)
        {
            var cur = (Dictionary<string, object>)j["current"];
            var hourly = (Dictionary<string, object>)j["hourly"];
            var daily = (Dictionary<string, object>)j["daily"];
            var d = new Data
            {
                Fetched = DateTime.Now,
                Temp = Num(cur["temperature_2m"]),
                Feels = Num(cur["apparent_temperature"]),
                Humidity = (int)Num(cur["relative_humidity_2m"]),
                Wind = Num(cur["wind_speed_10m"]),
                Pressure = Num(cur["pressure_msl"]),
                Code = (int)Num(cur["weather_code"]),
                Day = Num(cur["is_day"]) > 0,
                High = Num(((IList)daily["temperature_2m_max"])[0]),
                Low = Num(((IList)daily["temperature_2m_min"])[0]),
                Sunrise = Time(((IList)daily["sunrise"])[0]),
                Sunset = Time(((IList)daily["sunset"])[0]),
            };
            var times = (IList)hourly["time"];
            var temps = (IList)hourly["temperature_2m"];
            var codes = (IList)hourly["weather_code"];
            var rain = (IList)hourly["precipitation_probability"];
            var uv = (IList)hourly["uv_index"];
            var day = (IList)hourly["is_day"];
            var thisHour = DateTime.Now.Date.AddHours(DateTime.Now.Hour);
            for (int i = 0; i < times.Count && d.Hours.Count < 12; i++)
            {
                var at = Time(times[i]);
                if (at < thisHour) continue;
                if (at == thisHour) { d.Uv = Num(uv[i]); d.Rain = (int)Num(rain[i]); }
                d.Hours.Add(new Hour { At = at, Temp = Num(temps[i]), Code = (int)Num(codes[i]), Rain = (int)Num(rain[i]), Day = Num(day[i]) > 0 });
            }
            return d;
        }

        static double Num(object o)
        {
            if (o == null) return 0;
            try { return Convert.ToDouble(o, CultureInfo.InvariantCulture); } catch { return 0; }
        }

        static DateTime Time(object o)
        {
            DateTime t;
            return DateTime.TryParse(o as string, CultureInfo.InvariantCulture, DateTimeStyles.None, out t) ? t : DateTime.MinValue;
        }

        /// <summary>Finds a city by name (Open-Meteo geocoding) and remembers it. 'done' gets an error message or null.</summary>
        public static void SetCity(string name, Action<string> done)
        {
            name = (name ?? "").Trim();
            if (name == "")
            {
                foreach (var k in new[] { "WeatherCity", "WeatherPlace", "WeatherLat", "WeatherLon" }) Look.Set(k, "");
                Settings.Save();
                Now = null;
                done(null);
                return;
            }
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                string place, lat, lon;
                string err = Geocode(name, out place, out lat, out lon);
                App.Post(() =>
                {
                    if (err == null)
                    {
                        Look.Set("WeatherCity", name);
                        Look.Set("WeatherPlace", place);
                        Look.Set("WeatherLat", lat);
                        Look.Set("WeatherLon", lon);
                        Settings.Save();
                        Now = null;
                        Failed = false;
                        Ensure();
                    }
                    done(err);
                });
            });
        }

        public static void SetUnit(bool fahrenheit)
        {
            Look.Set("WeatherUnit", fahrenheit ? "F" : "C");
            Settings.Save();
            Now = null;
            Ensure();
        }

        public static string Describe(int code)
        {
            switch (code)
            {
                case 0: return "Clear sky";
                case 1: return "Mainly clear";
                case 2: return "Partly cloudy";
                case 3: return "Overcast";
                case 45: case 48: return "Fog";
                case 51: case 53: case 55: return "Drizzle";
                case 56: case 57: return "Freezing drizzle";
                case 61: case 63: return "Rain";
                case 65: return "Heavy rain";
                case 66: case 67: return "Freezing rain";
                case 71: case 73: case 75: case 77: return "Snow";
                case 80: case 81: case 82: return "Rain showers";
                case 85: case 86: return "Snow showers";
                case 95: return "Thunderstorm";
                case 96: case 99: return "Thunderstorm, hail";
                default: return "—";
            }
        }
    }

    /// <summary>Colour weather icons (sun, moon, clouds, rain, snow, storm, fog), drawn once at 4x and scaled down smooth.</summary>
    static class WeatherIcons
    {
        static readonly Dictionary<string, Bitmap> cache = new Dictionary<string, Bitmap>();

        public static void Draw(Graphics g, RectangleF r, int code, bool day)
        {
            int px = (int)Math.Ceiling(Math.Max(r.Width, r.Height));
            string key = code + "|" + day + "|" + px;
            Bitmap b;
            if (!cache.TryGetValue(key, out b))
            {
                if (cache.Count > 80) { foreach (var v in cache.Values) v.Dispose(); cache.Clear(); }
                using (var big = Render(code, day, px * 4)) b = Gfx.Scale(big, px);
                cache[key] = b;
            }
            g.DrawImage(b, r.X + (r.Width - px) / 2, r.Y + (r.Height - px) / 2, px, px);
        }

        static Bitmap Render(int code, bool day, int s)
        {
            var b = new Bitmap(s, s, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                bool cloudy = code == 2 || code == 3 || code >= 45;
                bool partly = code == 1 || code == 2;
                if (!cloudy || partly)
                {
                    // sun or moon: whole when clear, small in the top-left corner behind a cloud otherwise
                    var c = partly && code == 2 ? new RectangleF(s * 0.06f, s * 0.08f, s * 0.5f, s * 0.5f)
                          : partly ? new RectangleF(s * 0.12f, s * 0.12f, s * 0.6f, s * 0.6f)
                          : new RectangleF(s * 0.2f, s * 0.2f, s * 0.6f, s * 0.6f);
                    if (day) Sun(g, c); else Moon(g, c);
                }
                if (cloudy)
                {
                    bool dark = code == 3 || code >= 61;
                    var cr = code == 2 ? new RectangleF(s * 0.22f, s * 0.36f, s * 0.74f, s * 0.46f) : new RectangleF(s * 0.1f, s * 0.2f, s * 0.82f, s * 0.52f);
                    Cloud(g, cr, dark ? Color.FromArgb(255, 196, 202, 214) : Color.FromArgb(255, 244, 246, 250));
                    float y = cr.Bottom + s * 0.04f;
                    if ((code >= 51 && code <= 67) || (code >= 80 && code <= 82))
                        using (var pen = new Pen(Color.FromArgb(255, 90, 170, 255), s * 0.055f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                            for (int i = 0; i < 3; i++) { float x = cr.X + cr.Width * (0.28f + i * 0.22f); g.DrawLine(pen, x, y, x - s * 0.06f, y + s * 0.16f); }
                    else if ((code >= 71 && code <= 77) || code == 85 || code == 86)
                        using (var br = new SolidBrush(Color.White))
                            for (int i = 0; i < 3; i++) { float x = cr.X + cr.Width * (0.28f + i * 0.22f); g.FillEllipse(br, x - s * 0.04f, y + (i % 2) * s * 0.06f, s * 0.08f, s * 0.08f); }
                    else if (code >= 95)
                        using (var br = new SolidBrush(Color.FromArgb(255, 255, 204, 0)))
                            g.FillPolygon(br, new[] { new PointF(s * 0.52f, y - s * 0.02f), new PointF(s * 0.38f, y + s * 0.13f), new PointF(s * 0.48f, y + s * 0.13f), new PointF(s * 0.42f, y + s * 0.24f), new PointF(s * 0.6f, y + s * 0.07f), new PointF(s * 0.5f, y + s * 0.07f) });
                    else if (code == 45 || code == 48)
                        using (var pen = new Pen(Color.FromArgb(220, 210, 214, 222), s * 0.05f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                            for (int i = 0; i < 2; i++) g.DrawLine(pen, s * (0.2f + i * 0.08f), y + i * s * 0.09f, s * (0.8f - i * 0.04f), y + i * s * 0.09f);
                }
            }
            return b;
        }

        static void Sun(Graphics g, RectangleF r)
        {
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, rad = r.Width * 0.26f;
            using (var pen = new Pen(Color.FromArgb(255, 255, 196, 0), r.Width * 0.07f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4;
                    g.DrawLine(pen, cx + (float)Math.Cos(a) * rad * 1.45f, cy + (float)Math.Sin(a) * rad * 1.45f, cx + (float)Math.Cos(a) * rad * 1.9f, cy + (float)Math.Sin(a) * rad * 1.9f);
                }
            using (var br = new LinearGradientBrush(new RectangleF(cx - rad, cy - rad, rad * 2, rad * 2), Color.FromArgb(255, 255, 220, 60), Color.FromArgb(255, 255, 170, 0), 60f))
                g.FillEllipse(br, cx - rad, cy - rad, rad * 2, rad * 2);
        }

        static void Moon(Graphics g, RectangleF r)
        {
            // a crescent: the moon disc with a second disc cut out of its top-right (drawn at 4x, so the clip edge is smooth once scaled)
            float rad = r.Width * 0.36f, cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            using (var cut = new GraphicsPath())
            {
                cut.AddEllipse(cx - rad * 0.35f, cy - rad * 1.25f, rad * 1.9f, rad * 1.9f);
                var clip = g.Clip;
                g.SetClip(cut, CombineMode.Exclude);
                using (var br = new SolidBrush(Color.FromArgb(255, 238, 236, 225))) g.FillEllipse(br, cx - rad, cy - rad, rad * 2, rad * 2);
                g.Clip = clip;
            }
        }

        static void Cloud(Graphics g, RectangleF r, Color c)
        {
            // three puffs and a flat base, filled one by one with the same gradient: they overlap without seams
            using (var br = new LinearGradientBrush(r, c, Color.FromArgb(c.A, (int)(c.R * 0.86), (int)(c.G * 0.86), (int)(c.B * 0.88)), 90f))
            {
                g.FillEllipse(br, r.X, r.Y + r.Height * 0.35f, r.Width * 0.42f, r.Height * 0.65f);
                g.FillEllipse(br, r.X + r.Width * 0.2f, r.Y, r.Width * 0.5f, r.Height * 0.85f);
                g.FillEllipse(br, r.X + r.Width * 0.5f, r.Y + r.Height * 0.25f, r.Width * 0.5f, r.Height * 0.75f);
                g.FillRectangle(br, r.X + r.Width * 0.2f, r.Y + r.Height * 0.6f, r.Width * 0.62f, r.Height * 0.4f);
            }
        }
    }
}
