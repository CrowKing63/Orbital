using System;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Orbital.Tests;

public class SettingsManagerTests
{
    private sealed class IsolatedSettingsScope : IDisposable
    {
        private readonly IDisposable _settingsOverride;

        public string RootDirectory { get; }
        public string ConfigPath { get; }

        public IsolatedSettingsScope()
        {
            RootDirectory = Path.Combine(Path.GetTempPath(), "Orbital.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootDirectory);
            ConfigPath = Path.Combine(RootDirectory, "settings.json");
            _settingsOverride = SettingsManager.OverrideConfigPathForTesting(ConfigPath);
        }

        public void Dispose()
        {
            _settingsOverride.Dispose();

            if (Directory.Exists(RootDirectory))
            {
                Directory.Delete(RootDirectory, true);
            }
        }
    }

    private static AppSettings CreateDefaultSettings()
    {
        var method = typeof(SettingsManager).GetMethod("CreateDefaultSettings",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        return Assert.IsType<AppSettings>(method?.Invoke(null, null));
    }

    [Fact]
    public void CreateDefaultSettings_ReturnsValidSettings()
    {
        var settings = CreateDefaultSettings();

        Assert.NotNull(settings);
        Assert.NotNull(settings.Actions);
        Assert.NotEmpty(settings.Actions);
        Assert.Equal("https://api.openai.com/v1", settings.ApiBaseUrl);
        Assert.Equal("gpt-4o-mini", settings.ModelName);
        Assert.Equal(PopupPlacementMode.BottomRight, settings.PopupPlacement);
        Assert.True(settings.PopupAutoCloseEnabled);
        Assert.Equal(20, settings.PopupAutoCloseSeconds);
    }

    [Fact]
    public void DefaultSettings_ContainsExpectedActions()
    {
        var settings = CreateDefaultSettings();

        Assert.NotNull(settings);
        Assert.Contains(settings.Actions, a => a.Name == "Copy");
        Assert.Contains(settings.Actions, a => a.Name == "Cut");
        Assert.Contains(settings.Actions, a => a.Name == "Paste");
        Assert.Contains(settings.Actions, a => a.Name == "Translate");
        Assert.Contains(settings.Actions, a => a.Name == "Summarize");
        Assert.Contains(settings.Actions, a => a.Name == "Polish");
        Assert.Contains(settings.Actions, a => a.Name == "Search");
    }

    [Fact]
    public void DefaultSettings_LocalActionsHaveCorrectConfiguration()
    {
        var settings = CreateDefaultSettings();

        Assert.NotNull(settings);
        
        var copyAction = settings.Actions.Find(a => a.Name == "Copy");
        Assert.NotNull(copyAction);
        Assert.Equal("DirectCopy", copyAction.ResultAction);
        Assert.True(copyAction.IsSelectionRequired);

        var cutAction = settings.Actions.Find(a => a.Name == "Cut");
        Assert.NotNull(cutAction);
        Assert.Equal("Cut", cutAction.ResultAction);
        Assert.True(cutAction.IsSelectionRequired);

        var pasteAction = settings.Actions.Find(a => a.Name == "Paste");
        Assert.NotNull(pasteAction);
        Assert.Equal("Paste", pasteAction.ResultAction);
        Assert.False(pasteAction.IsSelectionRequired);
    }

    [Fact]
    public void ApiKey_EncryptionRoundTrip_PreservesValue()
    {
        using var scope = new IsolatedSettingsScope();
        SettingsManager.LoadSettings();
        
        string testKey = "sk-test-key-12345";
        
        SettingsManager.SetApiKey(testKey);
        string retrieved = SettingsManager.GetApiKey();

        Assert.Equal(testKey, retrieved);
    }

    [Fact]
    public void ApiKey_EmptyString_HandledCorrectly()
    {
        using var scope = new IsolatedSettingsScope();
        SettingsManager.LoadSettings();
        
        SettingsManager.SetApiKey("");
        string retrieved = SettingsManager.GetApiKey();

        Assert.Equal("", retrieved);
    }

    [Fact]
    public void ApiKey_NullString_HandledCorrectly()
    {
        using var scope = new IsolatedSettingsScope();
        SettingsManager.LoadSettings();
        
        SettingsManager.SetApiKey(null!);
        string retrieved = SettingsManager.GetApiKey();

        Assert.Equal("", retrieved);
    }

    [Fact]
    public void LoadSettings_WithCorruptedJson_RecoversAndCreatesBackup_InIsolatedPath()
    {
        using var scope = new IsolatedSettingsScope();
        File.WriteAllText(scope.ConfigPath, "{ invalid json");

        bool recovered = SettingsManager.LoadSettings();

        Assert.True(recovered);
        Assert.True(File.Exists(scope.ConfigPath));
        Assert.True(File.Exists(scope.ConfigPath + ".corrupt.bak"));
        Assert.Contains(SettingsManager.CurrentSettings.Actions, a => a.Name == "Copy");
    }

    [Fact]
    public void ImportActionPack_WithUnknownResultAction_ReturnsValidationError_AndDoesNotMutateSettings()
    {
        using var scope = new IsolatedSettingsScope();
        SettingsManager.LoadSettings();

        string importPath = Path.Combine(scope.RootDirectory, "invalid-actions.json");
        var actions = new List<ActionProfile>
        {
            new() { Name = "Broken Action", PromptFormat = "", ResultAction = "NotARealAction" }
        };
        File.WriteAllText(importPath, JsonConvert.SerializeObject(actions, Formatting.Indented));

        string? error = SettingsManager.ImportActionPack(importPath, replaceExisting: false);

        Assert.NotNull(error);
        Assert.Contains("Unknown ResultAction", error);
        Assert.DoesNotContain(SettingsManager.CurrentSettings.Actions, a => a.Name == "Broken Action");
    }

    [Fact]
    public void ImportActionPack_RejectsDuplicateNamesFromImportedFile()
    {
        using var scope = new IsolatedSettingsScope();
        SettingsManager.LoadSettings();

        string importPath = Path.Combine(scope.RootDirectory, "duplicate-actions.json");
        var actions = new List<ActionProfile>
        {
            new() { Name = "Quick Search", PromptFormat = "", ResultAction = "Browser" },
            new() { Name = "Quick Search", PromptFormat = "", ResultAction = "Browser" }
        };
        File.WriteAllText(importPath, JsonConvert.SerializeObject(actions, Formatting.Indented));

        string? error = SettingsManager.ImportActionPack(importPath, replaceExisting: false);

        Assert.NotNull(error);
        Assert.Contains("Duplicate action name", error);
        Assert.Empty(SettingsManager.CurrentSettings.Actions.FindAll(a => a.Name == "Quick Search"));
    }

    [Fact]
    public void LoadSettings_WhenPopupPlacementMissing_DefaultsToBottomRight()
    {
        using var scope = new IsolatedSettingsScope();

        var settings = CreateDefaultSettings();
        settings.PopupPlacement = PopupPlacementMode.NearCursor;
        var obj = JObject.Parse(JsonConvert.SerializeObject(settings, Formatting.Indented));
        obj.Remove(nameof(AppSettings.PopupPlacement));
        File.WriteAllText(scope.ConfigPath, obj.ToString());

        SettingsManager.LoadSettings();

        Assert.Equal(PopupPlacementMode.BottomRight, SettingsManager.CurrentSettings.PopupPlacement);
    }

    [Fact]
    public void SaveAndLoadSettings_PreservesPopupAutoCloseSettings()
    {
        using var scope = new IsolatedSettingsScope();
        SettingsManager.LoadSettings();

        SettingsManager.CurrentSettings.PopupAutoCloseEnabled = false;
        SettingsManager.CurrentSettings.PopupAutoCloseSeconds = 60;
        SettingsManager.SaveSettings();

        SettingsManager.LoadSettings();

        Assert.False(SettingsManager.CurrentSettings.PopupAutoCloseEnabled);
        Assert.Equal(60, SettingsManager.CurrentSettings.PopupAutoCloseSeconds);
    }

    [Fact]
    public void LoadSettings_WhenPopupAutoCloseFieldsMissing_DefaultsArePreserved()
    {
        using var scope = new IsolatedSettingsScope();

        var settings = CreateDefaultSettings();
        settings.PopupAutoCloseEnabled = false;
        settings.PopupAutoCloseSeconds = 45;
        var obj = JObject.Parse(JsonConvert.SerializeObject(settings, Formatting.Indented));
        obj.Remove(nameof(AppSettings.PopupAutoCloseEnabled));
        obj.Remove(nameof(AppSettings.PopupAutoCloseSeconds));
        File.WriteAllText(scope.ConfigPath, obj.ToString());

        SettingsManager.LoadSettings();

        Assert.True(SettingsManager.CurrentSettings.PopupAutoCloseEnabled);
        Assert.Equal(20, SettingsManager.CurrentSettings.PopupAutoCloseSeconds);
    }

    [Fact]
    public void ResolveConfigPath_InstalledBuild_ReturnsAppDataPath_EvenWithStrayMarkerFile()
    {
        // This test assembly is built without the PORTABLE constant, so the installed
        // branch must always resolve to %APPDATA%\Orbital — a stray portable.txt next to
        // the exe has no effect.
        using var dir = new TempDirectory();
        File.WriteAllText(System.IO.Path.Combine(dir.Path, "portable.txt"), "");

        string expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Orbital",
            "settings.json");
        Assert.Equal(expected, SettingsManager.ResolveConfigPath(dir.Path));
    }

    [Fact]
    public void MigratePortableSettings_CopiesLegacyFile_WhenLocalMissing()
    {
        using var dir = new TempDirectory();
        string local = Path.Combine(dir.Path, "settings.json");
        string legacy = Path.Combine(dir.Path, "legacy-settings.json");
        File.WriteAllText(legacy, "{}");

        SettingsManager.MigratePortableSettings(local, legacy);

        Assert.True(File.Exists(local));
        Assert.Equal("{}", File.ReadAllText(local));
    }

    [Fact]
    public void MigratePortableSettings_DoesNothing_WhenLocalAlreadyExists()
    {
        using var dir = new TempDirectory();
        string local = Path.Combine(dir.Path, "settings.json");
        string legacy = Path.Combine(dir.Path, "legacy-settings.json");
        File.WriteAllText(local, "{}");
        File.WriteAllText(legacy, "{\"changed\":true}");

        SettingsManager.MigratePortableSettings(local, legacy);

        Assert.Equal("{}", File.ReadAllText(local));
    }

    [Fact]
    public void MigratePortableSettings_DoesNothing_WhenLegacyMissing()
    {
        using var dir = new TempDirectory();
        string local = Path.Combine(dir.Path, "settings.json");

        SettingsManager.MigratePortableSettings(local, Path.Combine(dir.Path, "legacy-settings.json"));

        Assert.False(File.Exists(local));
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; }

        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Orbital.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, true);
        }
    }
}
