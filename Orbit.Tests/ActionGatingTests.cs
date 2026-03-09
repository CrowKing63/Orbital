using Xunit;

namespace Orbital.Tests;

public class ActionGatingTests
{
    [Fact]
    public void ActionProfile_IsSelectionRequired_DefaultsToTrue_ForNonPasteActions()
    {
        var action = new ActionProfile
        {
            Name = "Translate",
            PromptFormat = "Translate: {text}",
            ResultAction = "Replace"
        };

        Assert.True(action.IsSelectionRequired);
    }

    [Fact]
    public void ActionProfile_IsSelectionRequired_DefaultsToFalse_ForPasteAction()
    {
        var action = new ActionProfile
        {
            Name = "Paste",
            PromptFormat = "",
            ResultAction = "Paste"
        };

        Assert.False(action.IsSelectionRequired);
    }

    [Fact]
    public void ActionProfile_IsSelectionRequired_RespectsExplicitValue()
    {
        var action = new ActionProfile
        {
            Name = "Custom",
            PromptFormat = "",
            ResultAction = "Popup",
            RequiresSelection = false
        };

        Assert.False(action.IsSelectionRequired);
    }

    [Fact]
    public void ActionProfile_IsSelectionRequired_ExplicitTrueOverridesDefault()
    {
        var action = new ActionProfile
        {
            Name = "CustomPaste",
            PromptFormat = "",
            ResultAction = "Paste",
            RequiresSelection = true
        };

        Assert.True(action.IsSelectionRequired);
    }

    [Theory]
    [InlineData("Browser")]
    [InlineData("DirectCopy")]
    [InlineData("Cut")]
    [InlineData("Replace")]
    [InlineData("Copy")]
    public void ActionProfile_IsSelectionRequired_DefaultsToTrue_ForCommonActions(string resultAction)
    {
        var action = new ActionProfile
        {
            Name = "Test",
            PromptFormat = "",
            ResultAction = resultAction
        };

        Assert.True(action.IsSelectionRequired);
    }
}
