using System;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;
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
}
