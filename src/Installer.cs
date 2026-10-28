using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Harbor
{
    /// <summary>Per-user install (no admin): %LOCALAPPDATA%\Programs\Harbor, Start-menu shortcut, Apps & features entry.</summary>
    static class Installer
    {
        public static string InstallDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Harbor"); }
        }

        static string ShortcutPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Harbor.lnk"); }
        }

        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Harbor";

        /// <summary>Harbor.exe --install : copies this build into place, registers it and starts it.</summary>
        public static void Install()
        {
            string src = Path.GetDirectoryName(Application.ExecutablePath);
            string dst = InstallDir;
            // stop a running copy first so its files can be replaced
            IntPtr host = N.FindWindow(null, "HarborHost");
            IntPtr legacy = N.FindWindow(null, "MacDockHost"); // the app's old name
            if (legacy != IntPtr.Zero) N.PostMessage(legacy, N.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            if (host != IntPtr.Zero) N.PostMessage(host, N.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            WaitForOtherInstances();

            Directory.CreateDirectory(dst);
            if (!string.Equals(Path.GetFullPath(src).TrimEnd('\\'), Path.GetFullPath(dst).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                foreach (var f in Directory.GetFiles(src))
                    CopyWithRetry(f, Path.Combine(dst, Path.GetFileName(f)));
            string exe = Path.Combine(dst, "Harbor.exe");

            CreateShortcut(ShortcutPath, exe);
            // remember where local builds come from so the About page can offer newer ones
            // (not for an in-app update, which installs from a temporary folder)
            bool fromTemp = Path.GetFullPath(src).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase);
            if (!fromTemp && !string.Equals(Path.GetFullPath(src).TrimEnd('\\'), Path.GetFullPath(dst).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Harbor")) k.SetValue("UpdateSource", src);
            using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", "Harbor");
                k.SetValue("DisplayVersion", SettingsWindow.Version);
                k.SetValue("Publisher", "Hritik Sharma");
                k.SetValue("URLInfoAbout", SettingsWindow.RepoUrl);
                k.SetValue("DisplayIcon", exe);
                k.SetValue("InstallLocation", dst);
                k.SetValue("UninstallString", "\"" + exe + "\" --uninstall");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                long size = 0;
                foreach (var f in Directory.GetFiles(dst)) size += new FileInfo(f).Length;
                k.SetValue("EstimatedSize", (int)(size / 1024), RegistryValueKind.DWord);
            }
            RemoveLegacy();
            Process.Start(exe, "--wait"); // waits for the old copy to finish exiting
        }

        /// <summary>Cleans up the old "MacDock" install: shortcut, Apps &amp; features entry, registry, files.</summary>
        static void RemoveLegacy()
        {
            try
            {
                string lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "MacDock.lnk");
                if (File.Exists(lnk)) File.Delete(lnk);
            }
            catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\MacDock", false); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\MacDock", false); } catch { }
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                    if (k != null) k.DeleteValue("MacDock", false);
            }
            catch { }
            string old = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\MacDock");
            if (Directory.Exists(old)) DeleteFolderLater(old);
        }

        /// <summary>A newer Harbor.exe in the folder it was installed from (your build folder), or null.</summary>
        public static string NewerBuild()
        {
            try
            {
                string src;
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Harbor")) src = k == null ? null : k.GetValue("UpdateSource") as string;
                if (string.IsNullOrEmpty(src)) return null;
                string exe = Path.Combine(src, "Harbor.exe");
                if (!File.Exists(exe)) return null;
                if (File.GetLastWriteTimeUtc(exe) > File.GetLastWriteTimeUtc(Application.ExecutablePath).AddSeconds(5)) return exe;
            }
            catch { }
            return null;
        }

        /// <summary>Waits (up to ~8 s) for the running Harbor to finish quitting so its files can be replaced.</summary>
        static void WaitForOtherInstances()
        {
            int me = Process.GetCurrentProcess().Id;
            for (int i = 0; i < 80; i++)
            {
                bool others = false;
                var procs = new List<Process>(Process.GetProcessesByName("Harbor"));
                procs.AddRange(Process.GetProcessesByName("MacDock"));
                foreach (var p in procs)
                {
                    if (p.Id != me) others = true;
                    p.Dispose();
                }
                if (!others) return;
                System.Threading.Thread.Sleep(100);
            }
        }

        static void CopyWithRetry(string from, string to)
        {
            for (int i = 0; ; i++)
            {
                try { File.Copy(from, to, true); return; }
                catch (IOException) { if (i >= 30) throw; System.Threading.Thread.Sleep(200); }
                catch (UnauthorizedAccessException) { if (i >= 30) throw; System.Threading.Thread.Sleep(200); }
            }
        }

        static void CreateShortcut(string lnk, string target)
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            dynamic shell = Activator.CreateInstance(t);
            dynamic sc = shell.CreateShortcut(lnk);
            sc.TargetPath = target;
            sc.WorkingDirectory = Path.GetDirectoryName(target);
            sc.IconLocation = target + ",0";
            sc.Description = "macOS-style dock and menu bar";
            sc.Save();
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(sc);
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }

        /// <summary>Removes auto-start, the Start-menu shortcut and the Apps & features entry.</summary>
        public static void Remove()
        {
            try { Settings.LaunchAtLogin = false; Settings.Save(); } catch { }
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                    if (k != null) k.DeleteValue("Harbor", false);
            }
            catch { }
            try { if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath); } catch { }
            try { if (ClaudeBridge.HooksInstalled()) ClaudeBridge.SetHooks(false, null); } catch { } // leave Claude Code as we found it
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
        }

        /// <summary>The running exe can't delete itself: a short-lived cmd removes the install folder once we've exited.</summary>
        public static void DeleteFilesAfterExit()
        {
            string dir = Path.GetDirectoryName(Application.ExecutablePath);
            if (!string.Equals(Path.GetFullPath(dir).TrimEnd('\\'), Path.GetFullPath(InstallDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                return; // only ever delete the installed copy, never a dev/build folder
            DeleteFolderLater(dir);
        }

        /// <summary>Deletes an app folder a few seconds later (files can't be removed while they're running).</summary>
        static void DeleteFolderLater(string dir)
        {
            var psi = new ProcessStartInfo("cmd.exe", "/c timeout /t 4 /nobreak >nul & rmdir /s /q \"" + dir + "\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WorkingDirectory = Path.GetTempPath(),
            };
            try { Process.Start(psi); } catch { }
        }
    }
}
