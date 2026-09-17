using CodexSdk.TestSupport;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI.CodexSdk.MAF;
using Xunit;

namespace CodexSdk.MAF.Tests;

public sealed class CodexAIAgentResultUpdateTests
{
    [Fact]
    public async Task RunStreamingAsync_WhenTurnCompletes_EmitsResultUpdateBeforeTurnCompleted()
    {
        // Arrange
        using var cli = new FakeCodexCli();
        var agent = new CodexAIAgent(new CodexAIAgentOptions { CodexOptions = cli.CreateOptions() });
        var session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);

        // Act
        var updates = new List<AgentResponseUpdate>();
        await foreach (var update in agent.RunStreamingAsync(
            "hi", session, cancellationToken: TestContext.Current.CancellationToken))
        {
            updates.Add(update);
        }

        // Assert
        var resultIndex = updates.FindIndex(update => GetUpdateType(update) == "result");
        var turnCompletedIndex = updates.FindIndex(update => GetUpdateType(update) == "turn.completed");
        Assert.True(resultIndex >= 0, "Expected a result update.");
        Assert.Equal(turnCompletedIndex - 1, resultIndex);
        Assert.Equal(resultIndex, updates.FindLastIndex(update => GetUpdateType(update) == "result"));

        var result = updates[resultIndex];
        Assert.Equal("started", result.Text);
        Assert.Equal(ChatRole.Assistant, result.Role);
        Assert.Equal("codex", result.AuthorName);
    }

    [Fact]
    public async Task RunAsync_WhenTurnCompletes_IncludesResultMessageWithFinalResponse()
    {
        // Arrange
        using var cli = new FakeCodexCli();
        var agent = new CodexAIAgent(new CodexAIAgentOptions { CodexOptions = cli.CreateOptions() });
        var session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);

        // Act
        var response = await agent.RunAsync(
            "hi", session, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, response.Messages.Count(message => GetMessageType(message) == "result"));

        var result = response.Messages[^1];
        Assert.Equal("result", GetMessageType(result));
        Assert.Equal("started", result.Text);
        Assert.Equal(ChatRole.Assistant, result.Role);
        Assert.Equal("codex", result.AuthorName);
    }

    [Theory]
    [InlineData("turn", "turn.failed", "model unavailable")]
    [InlineData("thread", "error", "stream disconnected")]
    public async Task RunStreamingAsync_WhenTurnFails_EmitsResultUpdateWithErrorMessage(
        string failure, string failureUpdateType, string expectedMessage)
    {
        // Arrange
        using var cli = new FakeCodexCli(failure);
        var agent = new CodexAIAgent(new CodexAIAgentOptions { CodexOptions = cli.CreateOptions() });
        var session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);

        // Act
        var updates = new List<AgentResponseUpdate>();
        await foreach (var update in agent.RunStreamingAsync(
            "hi", session, cancellationToken: TestContext.Current.CancellationToken))
        {
            updates.Add(update);
        }

        // Assert
        var resultIndex = updates.FindIndex(update => GetUpdateType(update) == "result");
        var failureIndex = updates.FindIndex(update => GetUpdateType(update) == failureUpdateType);
        Assert.True(resultIndex >= 0, "Expected a result update.");
        Assert.Equal(failureIndex - 1, resultIndex);
        Assert.Equal(resultIndex, updates.FindLastIndex(update => GetUpdateType(update) == "result"));
        Assert.Equal(expectedMessage, updates[resultIndex].Text);
        Assert.Equal(ChatRole.Assistant, updates[resultIndex].Role);
    }

    [Theory]
    [InlineData("turn", "model unavailable")]
    [InlineData("thread", "stream disconnected")]
    public async Task RunAsync_WhenTurnFails_IncludesResultMessageWithErrorMessage(
        string failure, string expectedMessage)
    {
        // Arrange
        using var cli = new FakeCodexCli(failure);
        var agent = new CodexAIAgent(new CodexAIAgentOptions { CodexOptions = cli.CreateOptions() });
        var session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);

        // Act
        var response = await agent.RunAsync(
            "hi", session, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, response.Messages.Count(message => GetMessageType(message) == "result"));

        var result = response.Messages[^1];
        Assert.Equal("result", GetMessageType(result));
        Assert.Equal(expectedMessage, result.Text);
        Assert.Equal(ChatRole.Assistant, result.Role);
    }

    private static string? GetUpdateType(AgentResponseUpdate update) =>
        update.AdditionalProperties?.TryGetValue("type", out var type) == true
            ? type?.ToString()
            : null;

    private static string? GetMessageType(ChatMessage message) =>
        message.AdditionalProperties?.TryGetValue("type", out var type) == true
            ? type?.ToString()
            : null;
}
