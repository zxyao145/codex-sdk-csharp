using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.AI;
using OpenAI.CodexSdk.AppServer;
using OpenAI.CodexSdk.AppServer.MAF;
using OpenAI.CodexSdk.AppServer.Protocol;
using Xunit;

namespace OpenAI.CodexSdk.AppServer.MAF.Tests;

public sealed class CodexAppServerThreadLifecycleTests
{
    [Fact]
    public async Task RunAsync_InvalidInput_LeavesSessionUnbound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var client = await CodexAppServerClient.StartAsync(
            new CodexAppServerClientOptions { ExecutablePath = FixtureExecutable() }, cancellationToken);
        var agent = new CodexAppServerAIAgent(client);
        var session = await agent.CreateSessionAsync(cancellationToken);

        await Assert.ThrowsAsync<NotSupportedException>(() => agent.RunAsync(
            [new ChatMessage(ChatRole.System, "Unsupported message role")],
            session, cancellationToken: cancellationToken));

        Assert.Null(Assert.IsType<CodexAppServerAgentSession>(session).ThreadId);
    }

    [Fact]
    public async Task RunAsync_TurnConfigurationFails_LeavesNewSessionUnbound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var client = await CodexAppServerClient.StartAsync(
            new CodexAppServerClientOptions { ExecutablePath = FixtureExecutable() }, cancellationToken);
        var agent = new CodexAppServerAIAgent(client, new()
        {
            ConfigureTurnStart = (_, _) => throw new InvalidDataException("Invalid turn configuration."),
        });
        var session = await agent.CreateSessionAsync(cancellationToken);

        await Assert.ThrowsAsync<InvalidDataException>(() => agent.RunAsync(
            [new ChatMessage(ChatRole.User, "Do not start this turn.")],
            session, cancellationToken: cancellationToken));

        Assert.Null(Assert.IsType<CodexAppServerAgentSession>(session).ThreadId);
    }

    [Fact]
    public async Task RunAsync_UpdateCallbackFails_PropagatesFailure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var client = await CodexAppServerClient.StartAsync(
            new CodexAppServerClientOptions { ExecutablePath = FixtureExecutable() }, cancellationToken);
        var failure = new InvalidDataException("Application update persistence failed.");
        var callbackCount = 0;
        var agent = new CodexAppServerAIAgent(client, new()
        {
            OnResponseUpdateAsync = (update, _) =>
            {
                if (update.RawRepresentation is not AppServerNotification { Parameters: TurnStartedNotification })
                {
                    return ValueTask.CompletedTask;
                }
                callbackCount++;
                return ValueTask.FromException(failure);
            },
        });
        var session = await agent.CreateSessionAsync(cancellationToken);
        var run = agent.RunAsync(
            [new ChatMessage(ChatRole.User, "Reply with OK. Do not use tools.")],
            session, cancellationToken: cancellationToken);

        var observed = await Assert.ThrowsAsync<InvalidDataException>(() => run);

        Assert.Same(failure, observed);
        Assert.Equal(1, callbackCount);
    }

    [Fact]
    public async Task RunAsync_ResumeConfigured_PreservesBoundThreadIdentity()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var client = await CodexAppServerClient.StartAsync(
            new CodexAppServerClientOptions { ExecutablePath = FixtureExecutable() }, cancellationToken);
        ThreadResumeParams? configured = null;
        var boundId = Guid.NewGuid().ToString();
        var agent = new CodexAppServerAIAgent(client, new()
        {
            ConfigureThreadResume = parameters =>
            {
                configured = parameters;
                return parameters with { ThreadId = "replacement", Model = "gpt-6.1-sol" };
            },
        });
        var session = await agent.DeserializeSessionAsync(
            JsonSerializer.SerializeToElement(new { threadId = boundId }), cancellationToken: cancellationToken);

        var failure = await Assert.ThrowsAsync<CodexAppServerException>(() => agent.RunAsync(
            [new ChatMessage(ChatRole.User, "This missing thread must fail before a model turn.")],
            session, cancellationToken: cancellationToken));

        Assert.NotNull(configured);
        Assert.Equal(boundId, configured.ThreadId);
        Assert.Contains(boundId, failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"threadId":"replacement","path":"/another-rollout.jsonl"}""")]
    [InlineData("""{"threadId":"replacement","history":[]}""")]
    public async Task RunAsync_ResumeReplacesHistory_RejectsConfiguration(string configuration)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var client = await CodexAppServerClient.StartAsync(
            new CodexAppServerClientOptions { ExecutablePath = FixtureExecutable() }, cancellationToken);
        var agent = new CodexAppServerAIAgent(client, new()
        {
            ConfigureThreadResume = _ => AppServerJson.Deserialize<ThreadResumeParams>(configuration),
        });
        var session = await agent.DeserializeSessionAsync(
            JsonSerializer.SerializeToElement(new { threadId = Guid.NewGuid().ToString() }),
            cancellationToken: cancellationToken);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => agent.RunAsync(
            [new ChatMessage(ChatRole.User, "The identity override must fail before a model turn.")],
            session, cancellationToken: cancellationToken));

        Assert.Contains("path or history", failure.Message, StringComparison.Ordinal);
    }

    private static string FixtureExecutable([CallerFilePath] string sourceFile = "")
    {
        var platform = OperatingSystem.IsWindows() ? "win32" : OperatingSystem.IsMacOS() ? "darwin" : "linux";
        var architecture = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        var directory = Path.GetFullPath(
            $"../fixtures/codex-app-server/node_modules/@openai/codex-{platform}-{architecture}/vendor",
            Path.GetDirectoryName(sourceFile)!);
        return Path.Combine(Directory.EnumerateDirectories(directory).Single(), "bin",
            OperatingSystem.IsWindows() ? "codex.exe" : "codex");
    }
}
