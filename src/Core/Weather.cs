using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;

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
                Core.Post(() =>
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
                if (status == 200) return Parse(Json.Object(json));
                err = status == 0 ? "Couldn't reach the weather service" : "Weather service error (" + status + ")";
            }
            catch (Exception ex) { Core.Log(ex); err = "Couldn't read the weather"; }
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
                var j = Json.Object(json);
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
            catch (Exception ex) { Core.Log(ex); return "Couldn't look that city up"; }
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
                Core.Post(() =>
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
}
