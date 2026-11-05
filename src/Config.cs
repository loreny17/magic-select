using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace MagicSelect
{
    public class Config
    {
        public const string DefaultModel = "gemini-flash-latest";

        // Stored encrypted with Windows DPAPI (only your Windows user can read it).
        public string ApiKeyProtected { get; set; }
        public string Model { get; set; }
        public string TargetLanguage { get; set; }
        public string ExcludedApps { get; set; }
        public bool Enabled { get; set; }
        public bool DisableInFullscreen { get; set; }
        public bool SmartActions { get; set; }
        public bool WelcomeShown { get; set; }

        // Microphone button: either a keyboard key (+ modifiers) or a mouse button ("Middle", "X1", "X2").
        public int VoiceKey { get; set; }
        public int VoiceModifiers { get; set; }
        public string VoiceMouse { get; set; }

        public Config()
        {
            SmartActions = true;
            VoiceKey = 0x20;                                   // Space
            VoiceModifiers = VoiceTrigger.ModCtrl | VoiceTrigger.ModShift;
            VoiceMouse = "";
            DisableInFullscreen = true;
            Model = DefaultModel;
            TargetLanguage = "português do Brasil";
            ExcludedApps = "";
            Enabled = true;
        }

        static string Dir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MagicSelect"); }
        }

        static string FilePath { get { return Path.Combine(Dir, "config.json"); } }

        [ScriptIgnore]
        public string ApiKey
        {
            get
            {
                if (string.IsNullOrEmpty(ApiKeyProtected)) return "";
                try
                {
                    byte[] data = ProtectedData.Unprotect(Convert.FromBase64String(ApiKeyProtected), null, DataProtectionScope.CurrentUser);
                    return Encoding.UTF8.GetString(data);
                }
                catch { return ""; }
            }
            set
            {
                if (string.IsNullOrEmpty(value)) { ApiKeyProtected = ""; return; }
                byte[] data = ProtectedData.Protect(Encoding.UTF8.GetBytes(value.Trim()), null, DataProtectionScope.CurrentUser);
                ApiKeyProtected = Convert.ToBase64String(data);
            }
        }

        public bool IsExcluded(string processName)
        {
            if (string.IsNullOrEmpty(ExcludedApps) || string.IsNullOrEmpty(processName)) return false;
            foreach (string raw in ExcludedApps.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string n = raw.Trim();
                if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) n = n.Substring(0, n.Length - 4);
                if (n.Length > 0 && string.Equals(n, processName, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public static Config Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var c = new JavaScriptSerializer().Deserialize<Config>(File.ReadAllText(FilePath, Encoding.UTF8));
                    if (c != null)
                    {
                        if (string.IsNullOrWhiteSpace(c.Model)) c.Model = DefaultModel;
                        if (string.IsNullOrWhiteSpace(c.TargetLanguage)) c.TargetLanguage = "português do Brasil";
                        if (c.VoiceMouse == null) c.VoiceMouse = "";
                        return c;
                    }
                }
            }
            catch { }
            return new Config();
        }

        public void Save()
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, new JavaScriptSerializer().Serialize(this), Encoding.UTF8);
        }

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static bool StartsWithWindows
        {
            get
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue("MagicSelect") != null;
            }
            set
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (value) k.SetValue("MagicSelect", "\"" + System.Windows.Forms.Application.ExecutablePath + "\"");
                    else k.DeleteValue("MagicSelect", false);
                }
            }
        }
    }
}
