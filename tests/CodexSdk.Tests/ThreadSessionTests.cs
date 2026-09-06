using CodexSdk.TestSupport;
using OpenAI.CodexSdk;
using Xunit;

namespace CodexSdk.Tests;

public sealed class ThreadSessionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_ConsecutiveTurns_ResumesFirstThread(bool stream)
    {
        // Arrange
        using var cli = new FakeCodexCli();
        var thread = cli.CreateClient().StartThread();

        // Act
        await RunTurnAsync(thread, stream);
        var firstThreadId = thread.Id;
        await RunTurnAsync(thread, stream);

        // Assert
        Assert.NotNull(firstThreadId);
        Assert.Equal(firstThreadId, thread.Id);
        var invocations = cli.ReadArguments();
        Assert.Equal(2, invocations.Length);
        Assert.DoesNotContain("resume", invocations[0]);
        Assert.Equal(["resume", firstThreadId], invocations[1][^2..]);
    }

    [Fact]
    public async Task RunStreamedAsync_StoppedAfterThreadStarted_NextTurnResumes()
    {
        // Arrange
        using var cli = new FakeCodexCli();
        var thread = cli.CreateClient().StartThread();
        await foreach (var threadEvent in thread.RunStreamedAsync("first", cancellationToken: TestContext.Current.CancellationToken))
        {
            if (threadEvent is ThreadStartedEvent)
                break;
        }
        var firstThreadId = thread.Id;

        // Act
        await thread.RunAsync("next", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(firstThreadId);
        Assert.Equal(firstThreadId, thread.Id);
        Assert.Equal(["resume", firstThreadId], cli.ReadArguments()[1][^2..]);
    }

    [Fact]
    public async Task ResumeThread_ExistingId_FirstTurnResumes()
    {
        // Arrange
        using var cli = new FakeCodexCli();
        var threadId = Guid.NewGuid().ToString();
        var thread = cli.CreateClient().ResumeThread(threadId);

        // Act
        var result = await thread.RunAsync("next", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("resumed", result.FinalResponse);
        Assert.Equal(["resume", threadId], Assert.Single(cli.ReadArguments())[^2..]);
    }

    private static async Task RunTurnAsync(OpenAI.CodexSdk.Thread thread, bool stream)
    {
        if (stream)
        {
            await foreach (var _ in thread.RunStreamedAsync("test", cancellationToken: TestContext.Current.CancellationToken)) { }
        }
        else
        {
            await thread.RunAsync("test", cancellationToken: TestContext.Current.CancellationToken);
        }
    }
}
