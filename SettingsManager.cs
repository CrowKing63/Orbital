using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using System.Security.Cryptography;
using System.Text;

namespace Orbit
{
    public class ActionProfile
    {
        public string Name { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;  // Icon glyph or emoji
        public string PromptFormat { get; set; } = string.Empty;  // e.g. "Translate this to Korean: {text}"
        
        // String property for JSON serialization (backward compatibility)
        public string ResultAction { get; set; } = ActionType.Popup.ToSerializedString();  // "Copy", "Replace", "Popup", etc.
        
        // Typed property for code usage
        [JsonIgnore]
        public ActionType ActionType
        {
            get => ActionTypeExtensions.FromString(ResultAction ?? "Popup");
            set => ResultAction = value.ToSerializedString();
        }
        
        public bool? RequiresSelection { get; set; }

        [JsonIgnore]
        public bool IsSelectionRequired => RequiresSelection ?? (ActionType != Orbit.ActionType.Paste);
    }

    public class AppSettings
    {
        public string EncryptedApiKey { get; set; } = string.Empty;
        public string ApiBaseUrl  { get; set; } = "https://api.openai.com/v1";
        public string ModelName   { get; set; } = "gpt-4o-mini";
        public List<ActionProfile> Actions { get; set; } = new List<ActionProfile>();
    }

    public static class SettingsManager
    {
        private static readonly string DefaultConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
            "Orbit", 
            "settings.json");
        private static string? _configPathOverride;

        private static string ConfigPath => _configPathOverride ?? DefaultConfigPath;

        public static AppSettings CurrentSettings { get; private set; } = CreateDefaultSettings();

        public static bool LoadSettings()
        {
            bool recovered = false;
            if (File.Exists(ConfigPath))
            {
                try
                {
                    string json = File.ReadAllText(ConfigPath);
                    AppSettings? loadedSettings = JsonConvert.DeserializeObject<AppSettings>(json);
                    if (loadedSettings == null)
                    {
                        throw new JsonException("Deserialized settings is null.");
                    }

                    CurrentSettings = loadedSettings;
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
            string dir = Path.GetDirectoryName(ConfigPath)
                ?? throw new InvalidOperationException("Unable to resolve settings directory.");
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
                    new ActionProfile { Name = "Copy",      Icon = "\uE8C8", PromptFormat = "",                                                               ResultAction = "DirectCopy", RequiresSelection = true },
                    new ActionProfile { Name = "Cut",       Icon = "\uE8C6", PromptFormat = "",                                                               ResultAction = "Cut",        RequiresSelection = true },
                    new ActionProfile { Name = "Paste",     Icon = "\uE77F", PromptFormat = "",                                                               ResultAction = "Paste",      RequiresSelection = false },
                    new ActionProfile { Name = "Translate", Icon = "\uE8C1", PromptFormat = "Translate the following to Korean organically: {text}",          ResultAction = "Replace",    RequiresSelection = true },
                    new ActionProfile { Name = "Summarize", Icon = "\uE7C3", PromptFormat = "Summarize the following in 3 lines: {text}",                   ResultAction = "Popup",      RequiresSelection = true },
                    new ActionProfile { Name = "Polish",    Icon = "\uE70F", PromptFormat = "Correct grammar and make this sound professional: {text}",       ResultAction = "Replace",    RequiresSelection = true },
                    new ActionProfile { Name = "Search",    Icon = "\uE721", PromptFormat = "",                                                               ResultAction = "Browser",    RequiresSelection = true }
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

        internal static IDisposable OverrideConfigPathForTesting(string configPath)
        {
            string? previousOverride = _configPathOverride;
            AppSettings previousSettings = CurrentSettings;

            _configPathOverride = configPath;
            CurrentSettings = CreateDefaultSettings();

            return new ConfigPathOverrideScope(previousOverride, previousSettings);
        }

        public static string? ExportActionPack(string filePath)
        {
            try
            {
                var actionPack = new List<ActionProfile>(CurrentSettings.Actions);
                string json = JsonConvert.SerializeObject(actionPack, Formatting.Indented);
                File.WriteAllText(filePath, json);
                return null; // Success
            }
            catch (Exception ex)
            {
                return $"Export failed: {ex.Message}";
            }
        }

        public static string? ImportActionPack(string filePath, bool replaceExisting)
        {
            try
            {
                if (!File.Exists(filePath))
                    return "File not found.";

                string json = File.ReadAllText(filePath);
                var importedActions = JsonConvert.DeserializeObject<List<ActionProfile>>(json);

                if (importedActions == null || importedActions.Count == 0)
                    return "No valid actions found in file.";

                // Validate imported actions
                var importedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var action in importedActions)
                {
                    string? validationError = ValidateImportedAction(action);
                    if (validationError != null)
                        return validationError;

                    action.Name = action.Name.Trim();

                    if (!importedNames.Add(action.Name))
                        return $"Duplicate action name '{action.Name}' found in import file.";
                }

                if (replaceExisting)
                {
                    CurrentSettings.Actions = importedActions;
                }
                else
                {
                    // Merge: add imported actions, skip duplicates by name
                    var existingNames = new HashSet<string>(
                        CurrentSettings.Actions.Select(a => a.Name),
                        StringComparer.OrdinalIgnoreCase);

                    foreach (var action in importedActions)
                    {
                        if (!existingNames.Contains(action.Name))
                        {
                            CurrentSettings.Actions.Add(action);
                            existingNames.Add(action.Name);
                        }
                    }
                }

                SaveSettings();
                return null; // Success
            }
            catch (JsonException ex)
            {
                return $"Invalid JSON format: {ex.Message}";
            }
            catch (Exception ex)
            {
                return $"Import failed: {ex.Message}";
            }
        }

        private static string? ValidateImportedAction(ActionProfile action)
        {
            if (string.IsNullOrWhiteSpace(action.Name))
                return "Invalid action: Name is required.";
                    
            if (string.IsNullOrWhiteSpace(action.ResultAction))
                return $"Invalid action '{action.Name}': ResultAction is required.";

            if (!ActionTypeExtensions.TryFromString(action.ResultAction, out _))
                return $"Invalid action '{action.Name}': Unknown ResultAction '{action.ResultAction}'.";

            action.PromptFormat ??= string.Empty;
            return null;
        }

        private sealed class ConfigPathOverrideScope : IDisposable
        {
            private readonly string? _previousOverride;
            private readonly AppSettings _previousSettings;
            private bool _disposed;

            public ConfigPathOverrideScope(string? previousOverride, AppSettings previousSettings)
            {
                _previousOverride = previousOverride;
                _previousSettings = previousSettings;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _configPathOverride = _previousOverride;
                CurrentSettings = _previousSettings;
                _disposed = true;
            }
        }
    }
}
