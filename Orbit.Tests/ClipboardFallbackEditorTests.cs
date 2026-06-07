using Xunit;

namespace Orbital.Tests;

public class ClipboardFallbackEditorTests
{
    [Theory]
    [InlineData("", "", "notepad++")]
    [InlineData("", "", "emeditor")]
    [InlineData("", "", "sublime_text")]
    [InlineData("Notepad++", "", "")]
    [InlineData("", "EmEditorMainFrame3", "")]
    [InlineData("", "EmEditorMainFrame4", "")]
    public void IsClipboardFallbackEditor_ReturnsTrue_ForKnownFallbackEditors(
        string controlClass,
        string rootClass,
        string processName)
    {
        Assert.True(App.IsClipboardFallbackEditor(controlClass, rootClass, processName));
    }

    [Fact]
    public void IsClipboardFallbackEditor_ReturnsFalse_ForUnknownEditor()
    {
        Assert.False(App.IsClipboardFallbackEditor("CustomClass", "CustomRoot", "unknown_editor"));
    }

    [Fact]
    public void IsClipboardFallbackEditor_MatchesProcessNamesCaseInsensitively()
    {
        Assert.True(App.IsClipboardFallbackEditor("", "", "Sublime_Text"));
    }
}
