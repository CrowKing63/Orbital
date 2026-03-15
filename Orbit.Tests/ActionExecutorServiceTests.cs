using System;
using System.Threading.Tasks;
using Xunit;

namespace Orbital.Tests;

public class ActionExecutorServiceTests
{
    [Fact]
    public void Constructor_WithNullLlmService_CreatesValidInstance()
    {
        var service = new ActionExecutorService(null);

        Assert.NotNull(service);
        Assert.False(service.HasLlmService);
        Assert.Null(service.LlmService);
    }

    [Fact]
    public void Constructor_WithLlmService_SetsProperties()
    {
        var mockLlmService = new MockLlmService();
        var service = new ActionExecutorService(mockLlmService);

        Assert.NotNull(service);
        Assert.True(service.HasLlmService);
        Assert.NotNull(service.LlmService);
    }

    [Fact]
    public async Task ExecuteAsync_WithSelectionRequiredAction_AndEmptyText_ReturnsEarly()
    {
        var service = new ActionExecutorService(null);
        var action = new ActionProfile
        {
            Name = "Test",
            ResultAction = "DirectCopy",
            RequiresSelection = true
        };

        // Should not throw, just return early
        await service.ExecuteAsync(action, "");
        
        // If we get here without exception, the defensive guard worked
        Assert.True(true);
    }

    [Fact]
    public async Task ExecuteAsync_WithLlmAction_AndNoService_ThrowsException()
    {
        var service = new ActionExecutorService(null);
        var action = new ActionProfile
        {
            Name = "Translate",
            PromptFormat = "Translate: {text}",
            ResultAction = "Replace"
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.ExecuteAsync(action, "test text")
        );
    }

    private class MockLlmService : ILlmApiService
    {
        public Task<string> CallApiAsync(string prompt, string? systemPrompt = null)
        {
            return Task.FromResult("mock response");
        }
    }
}
