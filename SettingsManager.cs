using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using System.Security.Cryptography;
using System.Text;

namespace Orbit
{
    public class ActionProfile
    {
        public string Name { get; set; }
        public string PromptFormat { get; set; }  // e.g. "Translate this to Korean: {text}"
        public string ResultAction { get; set; }  // "Copy", "Replace", "Popup"
    }

    public class AppSettings
    {
        public string EncryptedApiKey { get; set; }
        public List<ActionProfile> Actions { get; set; } = new List<ActionProfile>();
    }

    public static class SettingsManager
    {
        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
            "Orbit", 
            "settings.json");

        public static AppSettings CurrentSettings { get; private set; }

        public static void LoadSettings()
        {
            if (File.Exists(ConfigPath))
            {
                string json = File.ReadAllText(ConfigPath);
                CurrentSettings = JsonConvert.DeserializeObject<AppSettings>(json) ?? CreateDefaultSettings();
            }
            else
            {
                CurrentSettings = CreateDefaultSettings();
                SaveSettings();
            }
        }

        public static void SaveSettings()
        {
            string dir = Path.GetDirectoryName(ConfigPath);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonConvert.SerializeObject(CurrentSettings, Formatting.Indented);
            File.WriteAllText(ConfigPath, json);
        }

        private static AppSettings CreateDefaultSettings()
        {
            return new AppSettings
            {
                EncryptedApiKey = string.Empty,
                Actions = new List<ActionProfile>
                {
                    new ActionProfile { Name = "번역", PromptFormat = "Translate the following to Korean organically: {text}", ResultAction = "Replace" },
                    new ActionProfile { Name = "요약", PromptFormat = "Summarize the following in 3 lines: {text}", ResultAction = "Popup" },
                    new ActionProfile { Name = "수정", PromptFormat = "Correct grammar and make this sound professional: {text}", ResultAction = "Replace" },
                    new ActionProfile { Name = "검색", PromptFormat = "Search web for: {text}", ResultAction = "Browser" }
                }
            };
        }

        // DPAPI Encryption for API Keys
        public static void SetApiKey(string rawKey)
        {
            if (string.IsNullOrEmpty(rawKey))
            {
                CurrentSettings.EncryptedApiKey = string.Empty;
                return;
            }

            byte[] encryptedData = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(rawKey),
                null, 
                DataProtectionScope.CurrentUser);
            
            CurrentSettings.EncryptedApiKey = Convert.ToBase64String(encryptedData);
        }

        public static string GetApiKey()
        {
            if (string.IsNullOrEmpty(CurrentSettings.EncryptedApiKey))
                return string.Empty;

            try
            {
                byte[] rawData = Convert.FromBase64String(CurrentSettings.EncryptedApiKey);
                byte[] decryptedData = ProtectedData.Unprotect(
                    rawData,
                    null,
                    DataProtectionScope.CurrentUser);
                
                return Encoding.UTF8.GetString(decryptedData);
            }
            catch
            {
                return string.Empty; // Failsafe if encryption key changes or gets corrupted
            }
        }
    }
}
