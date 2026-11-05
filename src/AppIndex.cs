using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace MagicSelect
{
    class InstalledApp
    {
        public string Name;
        public string Id; // parsing name inside shell:AppsFolder (works for normal programs and Store apps)
    }

    /// <summary>
    /// The programs you have installed, as listed in the Start menu ("Todos os aplicativos").
    /// Built in the background and refreshed every few minutes.
    /// </summary>
    static class AppIndex
    {
        static readonly object gate = new object();
        static List<InstalledApp> apps = new List<InstalledApp>();
        static DateTime builtAt = DateTime.MinValue;
        static bool building;

        public static void RefreshAsync()
        {
            lock (gate)
            {
                if (building || (apps.Count > 0 && (DateTime.Now - builtAt).TotalMinutes < 10)) return;
                building = true;
            }
            var t = new Thread(Build);
            t.SetApartmentState(ApartmentState.STA); // Shell COM objects need STA
            t.IsBackground = true;
            t.Start();
        }

        /// <summary>Current list; waits a little if the first build is still running.</summary>
        public static List<InstalledApp> Snapshot(int waitMs)
        {
            DateTime until = DateTime.Now.AddMilliseconds(waitMs);
            while (DateTime.Now < until)
            {
                lock (gate) if (!building || apps.Count > 0) break;
                Thread.Sleep(50);
            }
            lock (gate) return new List<InstalledApp>(apps);
        }

        static void Build()
        {
            var list = new List<InstalledApp>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                Type shellType = Type.GetTypeFromProgID("Shell.Application");
                dynamic shell = Activator.CreateInstance(shellType);
                dynamic folder = shell.NameSpace("shell:AppsFolder");
                foreach (dynamic item in folder.Items())
                {
                    string name = item.Name;
                    string id = item.Path;
                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(id) || !Useful(name) || !seen.Add(name)) continue;
                    list.Add(new InstalledApp { Name = name, Id = id });
                }
            }
            catch (Exception ex) { Program.Log("app index error: " + ex); }

            Program.Log("app index: " + list.Count + " apps");
            lock (gate)
            {
                if (list.Count > 0) apps = list;
                builtAt = DateTime.Now;
                building = false;
            }
        }

        static bool Useful(string name)
        {
            string n = name.ToLowerInvariant();
            string[] junk = { "uninstall", "desinstal", "remover", "readme", "leia-me", "leiame", "release notes",
                              "documentation", "documentação", "license", "licença", "help", "ajuda", "website", "site da web" };
            foreach (string j in junk) if (n.Contains(j)) return false;
            return name.Length > 1;
        }

        /// <summary>Exact name first, then the closest "contains" match.</summary>
        public static InstalledApp Find(string name, List<InstalledApp> list)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            string q = name.Trim();
            foreach (var a in list)
                if (string.Equals(a.Name, q, StringComparison.OrdinalIgnoreCase)) return a;

            InstalledApp best = null;
            foreach (var a in list)
            {
                bool match = a.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                             (q.Length >= 4 && q.IndexOf(a.Name, StringComparison.OrdinalIgnoreCase) >= 0);
                if (match && (best == null || a.Name.Length < best.Name.Length)) best = a;
            }
            return best;
        }

        public static void Launch(InstalledApp app)
        {
            Process.Start(new ProcessStartInfo("explorer.exe", "\"shell:AppsFolder\\" + app.Id + "\"") { UseShellExecute = true });
        }
    }
}
