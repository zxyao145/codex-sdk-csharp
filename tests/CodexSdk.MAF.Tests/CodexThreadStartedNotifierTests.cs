using OpenAI.CodexSdk.MAF;
using Xunit;

namespace CodexSdk.MAF.Tests;

public class CodexThreadStartedNotifierTests
{
    [Fact]
    public async Task NotifyAsync_WhenCallbackConfigured_UpdatesSessionAndInvokesCallback()
    {
        var observedThreadIds = new List<string>();
        const string threadId = "22222222-2222-2222-2222-222222222222";
        var options = new CodexAIAgentOptions
        {
            OnThreadStartedAsync = (startedThreadId, _) =>
            {
                observedThreadIds.Add(startedThreadId);
                return ValueTask.CompletedTask;
            }
        };
        var session = new CodexAgentSession("initial");

        await CodexThreadStartedNotifier.NotifyAsync(
            options,
            session,
            threadId,
            CancellationToken.None);

        Assert.Equal(threadId, session.ThreadId);
        Assert.Equal([threadId], observedThreadIds);
    }
}
