using System;
using System.IO;
using Xunit;

namespace Orbit.Tests;

public class SettingsManagerTests
{
    [Fact]
    public void CreateDefaultSettings_ReturnsValidSettings()
    {
        // Use reflection to call private method for testing
        var method = typeof(SettingsManager).GetMethod("CreateDefaultSettings", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        
        var settings = method?.Invoke(null, null) as AppSettings;

        Assert.NotNull(settings);
        Assert.NotNull(settings.Actions);
        Assert.NotEmpty(settings.Actions);
        Assert.Equal("https://api.openai.com/v1", settings.ApiBaseUrl);
        Assert.Equal("gpt-4o-mini", settings.ModelName);
    }

    [Fact]
    public void DefaultSettings_ContainsExpectedActions()
    {
        var method = typeof(SettingsManager).GetMethod("CreateDefaultSettings", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        
        var settings = method?.Invoke(null, null) as AppSettings;

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
        var method = typeof(SettingsManager).GetMethod("CreateDefaultSettings", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        
        var settings = method?.Invoke(null, null) as AppSettings;

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
        // Initialize settings first
        SettingsManager.LoadSettings();
        
        string testKey = "sk-test-key-12345";
        
        SettingsManager.SetApiKey(testKey);
        string retrieved = SettingsManager.GetApiKey();

        Assert.Equal(testKey, retrieved);
    }

    [Fact]
    public void ApiKey_EmptyString_HandledCorrectly()
    {
        // Initialize settings first
        SettingsManager.LoadSettings();
        
        SettingsManager.SetApiKey("");
        string retrieved = SettingsManager.GetApiKey();

        Assert.Equal("", retrieved);
    }

    [Fact]
    public void ApiKey_NullString_HandledCorrectly()
    {
        // Initialize settings first
        SettingsManager.LoadSettings();
        
        SettingsManager.SetApiKey(null!);
        string retrieved = SettingsManager.GetApiKey();

        Assert.Equal("", retrieved);
    }
}
