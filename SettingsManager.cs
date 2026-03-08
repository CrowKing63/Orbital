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
        
        public bool? RequiresSelection { get; set; }

        [JsonIgnore]
        public bool IsSelectionRequired => RequiresSelection ?? (ResultAction != "Paste");
    }

    public class AppSettings
    {
        public string EncryptedApiKey { get; set; }
        public string ApiBaseUrl  { get; set; } = "https://api.openai.com/v1";
        public string ModelName   { get; set; } = "gpt-4o-mini";
        public List<ActionProfile> Actions { get; set; } = new List<ActionProfile>();
    }

    public static class SettingsManager
    {
        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
            "Orbit", 
            "settings.json");

        public static AppSettings CurrentSettings { get; private set; }

        public static bool LoadSettings()
        {
            bool recovered = false;
            if (File.Exists(ConfigPath))
            {
                try
                {
                    string json = File.ReadAllText(ConfigPath);
                    CurrentSettings = JsonConvert.DeserializeObject<AppSettings>(json);
                    if (CurrentSettings == null)
                    {
                        throw new JsonException("Deserialized settings is null.");
                    }
                }
                catch (Exception)
                {
                    string backupPath = ConfigPath + ".corrupt.bak";
                    try
                    {
                        if (File.Exists(backupPath)) File.Delete(backupPath);
                        File.Move(ConfigPath, backupPath);
                    }
                    catch { } // Best effort backup

                    CurrentSettings = CreateDefaultSettings();
                    SaveSettings();
                    recovered = true;
                }
            }
            else
            {
                CurrentSettings = CreateDefaultSettings();
                SaveSettings();
            }

            return recovered;
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
                    new ActionProfile { Name = "Copy",      PromptFormat = "",                                                               ResultAction = "DirectCopy", RequiresSelection = true },
                    new ActionProfile { Name = "Cut",       PromptFormat = "",                                                               ResultAction = "Cut",        RequiresSelection = true },
                    new ActionProfile { Name = "Paste",     PromptFormat = "",                                                               ResultAction = "Paste",      RequiresSelection = false },
                    new ActionProfile { Name = "Translate", PromptFormat = "Translate the following to Korean organically: {text}",          ResultAction = "Replace",    RequiresSelection = true },
                    new ActionProfile { Name = "Summarize", PromptFormat = "Summarize the following in 3 lines: {text}",                   ResultAction = "Popup",      RequiresSelection = true },
                    new ActionProfile { Name = "Polish",    PromptFormat = "Correct grammar and make this sound professional: {text}",       ResultAction = "Replace",    RequiresSelection = true },
                    new ActionProfile { Name = "Search",    PromptFormat = "",                                                               ResultAction = "Browser",    RequiresSelection = true }
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
