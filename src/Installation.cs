using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.Win32;

namespace MagicSelect
{
    /// <summary>Per-user install (no admin): files, shortcuts, "Apps" entry, startup. Shared by Setup and the app's uninstaller.</summary>
    static class Installation
    {
        public const string RunValue = "MagicSelect";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\MagicSelect";

        public static string Dir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\MagicSelect"); }
        }

        public static string ExePath { get { return Path.Combine(Dir, "MagicSelect.exe"); } }

        static string StartMenuLink
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Brand.Name + ".lnk"); }
        }

        static string DesktopLink
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Brand.Name + ".lnk"); }
        }

        public static bool IsInstalled { get { return File.Exists(ExePath); } }

        /// <summary>Closes any running copy (other than this process) so files can be replaced.</summary>
        public static void StopRunning()
        {
            int me = Process.GetCurrentProcess().Id;
            foreach (var p in Process.GetProcessesByName("MagicSelect"))
            {
                try
                {
                    if (p.Id == me) continue;
                    p.Kill();
                    p.WaitForExit(3000);
                }
                catch { }
                finally { p.Dispose(); }
            }
        }

        public static void Install(Stream payload, bool desktopShortcut, bool startWithWindows)
        {
            StopRunning();
            Directory.CreateDirectory(Dir);

            byte[] data;
            using (var ms = new MemoryStream()) { payload.CopyTo(ms); data = ms.ToArray(); }
            for (int attempt = 0; ; attempt++)
            {
                try { File.WriteAllBytes(ExePath, data); break; }
                catch (IOException) { if (attempt >= 10) throw; Thread.Sleep(300); }
            }

            CreateShortcut(StartMenuLink);
            if (desktopShortcut) CreateShortcut(DesktopLink);
            else TryDelete(DesktopLink);

            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (startWithWindows) k.SetValue(RunValue, "\"" + ExePath + "\"");
                else k.DeleteValue(RunValue, false);
            }

            using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", Brand.Name);
                k.SetValue("DisplayVersion", Brand.Version);
                k.SetValue("Publisher", Brand.Name);
                k.SetValue("DisplayIcon", ExePath + ",0");
                k.SetValue("InstallLocation", Dir);
                k.SetValue("UninstallString", "\"" + ExePath + "\" --uninstall");
                k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                k.SetValue("EstimatedSize", Math.Max(1, data.Length / 1024), RegistryValueKind.DWord);
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
        }

        public static void Launch()
        {
            Process.Start(new ProcessStartInfo(ExePath) { UseShellExecute = true, WorkingDirectory = Dir });
        }

        /// <summary>Removes everything. The program folder is deleted a moment after this process exits.</summary>
        public static void Uninstall()
        {
            StopRunning();
            TryDelete(StartMenuLink);
            TryDelete(DesktopLink);
            try { using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true)) if (k != null) k.DeleteValue(RunValue, false); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
            try
            {
                string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MagicSelect");
                if (Directory.Exists(data)) Directory.Delete(data, true);
            }
            catch { }

            // The running exe can't delete itself: ask cmd to remove the folder once we're gone.
            Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 >nul & rmdir /s /q \"" + Dir + "\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }

        static void CreateShortcut(string path)
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            dynamic shell = Activator.CreateInstance(t);
            dynamic lnk = shell.CreateShortcut(path);
            lnk.TargetPath = ExePath;
            lnk.WorkingDirectory = Dir;
            lnk.Description = Brand.Tagline;
            lnk.IconLocation = ExePath + ",0";
            lnk.Save();
        }

        static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
