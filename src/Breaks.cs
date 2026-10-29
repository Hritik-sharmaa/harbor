using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace Harbor
{
    /// <summary>
    /// Gentle break reminders as live activities: rest your eyes (every 20 minutes, the 20-20-20 rule), stand up
    /// (every hour), drink water (every 90 minutes). Only time you're actually at the PC counts: five minutes away
    /// counts as a break and starts the clocks again. They wait while Focus is on or something is fullscreen.
    /// One check a minute, and only while at least one is switched on.
    /// </summary>
    static class Breaks
    {
        [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint cbSize, dwTime; }
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO li);

        static System.Windows.Forms.Timer minute;
        static int eyes, stand, water; // minutes at the PC since each last reminder

        public static void Init()
        {
            bool any = Look.Bool("BreakEyes") || Look.Bool("BreakStand") || Look.Bool("BreakWater");
            if (any && minute == null)
            {
                minute = new System.Windows.Forms.Timer { Interval = 60000 };
                minute.Tick += delegate { Tick(); };
            }
            if (minute != null) { if (any) minute.Start(); else minute.Stop(); }
        }

        static double IdleMinutes()
        {
            var li = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO)) };
            if (!GetLastInputInfo(ref li)) return 0;
            return unchecked((uint)Environment.TickCount - li.dwTime) / 60000.0;
        }

        static void Tick()
        {
            double idle = IdleMinutes();
            if (idle >= 5) { eyes = stand = water = 0; return; } // away long enough: that was a break
            if (idle >= 1) return;                                 // reading or watching: don't count, don't nag
            eyes++; stand++; water++;
            if (FocusMode.On || LiveActivity.Target() == null) return; // later: the counts keep, the reminder waits
            if (Look.Bool("BreakEyes") && eyes >= 20)
            {
                eyes = 0;
                LiveActivity.Show(new LiveActivity.Item { Glyph = "", Title = "Rest your eyes", Detail = "look at something far away for 20 seconds", Tint = Color.FromArgb(255, 120, 200, 255), Ms = 8000 });
            }
            else if (Look.Bool("BreakStand") && stand >= 60)
            {
                stand = 0;
                LiveActivity.Show(new LiveActivity.Item { Glyph = "", Title = "Time to stand up", Detail = "stretch and walk around for a minute", Tint = Color.FromArgb(255, 52, 199, 89), Ms = 8000 });
            }
            else if (Look.Bool("BreakWater") && water >= 90)
            {
                water = 0;
                LiveActivity.Show(new LiveActivity.Item { Glyph = "", Title = "Drink some water", Detail = "it's been a while", Tint = Color.FromArgb(255, 100, 180, 255), Ms = 8000 });
            }
        }
    }
}
