using CodexSdk.TestSupport;
using OpenAI.CodexSdk;
using OpenAI.CodexSdk.MAF;
using System.Text.Json;
using Xunit;

namespace CodexSdk.MAF.Tests;

public sealed class CodexSessionResumeTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task GetThread_WithoutSessionId_StartsNewThread(string? sessionId)
    {
        // Arrange
        using var cli = new FakeCodexCli();
        var options = new CodexAIAgentOptions { IsResume = true, ThreadOptions = new ThreadOptions() };
        var session = new CodexAgentSession(sessionId);

        // Act
        var thread = CodexAIAgent.GetThread(cli.CreateClient(), options, session);
        var result = await thread.RunAsync("first", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("started", result.FinalResponse);
        Assert.DoesNotContain("resume", Assert.Single(cli.ReadArguments()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetThread_AfterFirstTurn_ResumesCapturedSession(bool reloadSession)
    {
        // Arrange
        using var cli = new FakeCodexCli();
        var codex = cli.CreateClient();
        var capturedIds = new List<string>();
        var options = new CodexAIAgentOptions
        {
            ThreadOptions = new ThreadOptions(),
            OnThreadStartedAsync = (id, _) =>
            {
                capturedIds.Add(id);
                return ValueTask.CompletedTask;
            },
        };
        var session = new CodexAgentSession();
        var firstThread = CodexAIAgent.GetThread(codex, options, session);
        await foreach (var threadEvent in firstThread.RunStreamedAsync("first", cancellationToken: TestContext.Current.CancellationToken))
        {
            if (threadEvent is ThreadStartedEvent started)
                await CodexThreadStartedNotifier.NotifyAsync(options, session, started.ThreadId, TestContext.Current.CancellationToken);
        }
        if (reloadSession)
            session = JsonSerializer.Deserialize<CodexAgentSession>(JsonSerializer.Serialize(session))!;

        // Act
        var nextThread = CodexAIAgent.GetThread(codex, options, session);
        var result = await nextThread.RunAsync("next", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var threadId = Assert.Single(capturedIds);
        Assert.False(options.IsResume);
        Assert.Equal(threadId, session.ThreadId);
        Assert.Equal(threadId, nextThread.Id);
        Assert.Equal("resumed", result.FinalResponse);
        Assert.DoesNotContain("resume", cli.ReadArguments()[0]);
        Assert.Equal(["resume", threadId], cli.ReadArguments()[1][^2..]);
    }

    [Fact]
    public async Task GetThread_SessionIdOverridesConfiguredId_ResumesSessionId()
    {
        // Arrange
        using var cli = new FakeCodexCli();
        var sessionId = Guid.NewGuid().ToString();
        var options = new CodexAIAgentOptions
        {
            IsResume = true,
            ThreadId = Guid.NewGuid(),
            ThreadOptions = new ThreadOptions(),
        };
        var session = new CodexAgentSession(sessionId);

        // Act
        var thread = CodexAIAgent.GetThread(cli.CreateClient(), options, session);
        await thread.RunAsync("next", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["resume", sessionId], Assert.Single(cli.ReadArguments())[^2..]);
    }
}
