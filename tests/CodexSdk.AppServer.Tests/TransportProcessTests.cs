using OpenAI.CodexSdk.AppServer;
using OpenAI.CodexSdk.AppServer.Protocol;
using StreamJsonRpc;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Xunit;

namespace CodexSdk.AppServer.Tests;

public sealed class TransportProcessTests : IDisposable
{
    private readonly string _sqliteHome = Directory.CreateTempSubdirectory("codex app-server tests ").FullName;

    [Fact]
    public async Task StartAsync_RealCodexAppServer_ListsModelsThreadsAndReadsFile()
    {
        await using var client = await CodexAppServerClient.StartAsync(
            CreateClientOptions(),
            TestContext.Current.CancellationToken);

        var modelsTask = client.ModelListAsync(new ModelListParams(), TestContext.Current.CancellationToken);
        var threadsTask = client.ThreadListAsync(new ThreadListParams(), TestContext.Current.CancellationToken);
        await Task.WhenAll(modelsTask, threadsTask);

        var models = await modelsTask;
        Assert.NotNull(models.Data);

        var threads = await threadsTask;
        Assert.NotNull(threads.Data);

        var fixtureReadme = FixtureReadme();
        var file = await client.FsReadFileAsync(new FsReadFileParams { Path = fixtureReadme }, TestContext.Current.CancellationToken);
        Assert.Equal(await File.ReadAllTextAsync(fixtureReadme, TestContext.Current.CancellationToken),
            System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(file.DataBase64)));
    }

    [Fact]
    public async Task StartAsync_WhenProcessExits_CompletesNotificationSubscriber()
    {
        await using var client = await CodexAppServerClient.StartAsync(
            CreateClientOptions(),
            TestContext.Current.CancellationToken);
        await using var notifications = client.Subscribe(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        var process = (Process)typeof(CodexAppServerClient)
            .GetField("_process", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(client)!;

        process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        while (await notifications.MoveNextAsync().AsTask()
                   .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken))
        {
            Assert.NotNull(notifications.Current);
        }
    }

    [Fact]
    public async Task ConnectAsync_RealCodexAppServer_OwnsHandlerAndDisposesProcess()
    {
        var options = CreateClientOptions();
        var startInfo = new ProcessStartInfo(options.ExecutablePath)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("app-server");
        startInfo.ArgumentList.Add("--listen");
        startInfo.ArgumentList.Add("stdio://");
        foreach (var argument in options.Arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        Assert.True(process.Start());
        var standardErrorDrain = process.StandardError.BaseStream.CopyToAsync(Stream.Null, CancellationToken.None);
        var messageHandler = new NewLineDelimitedMessageHandler(
            process.StandardInput.BaseStream,
            process.StandardOutput.BaseStream,
            CodexAppServerClient.CreateTransportFormatter());

        try
        {
            await using (var client = await CodexAppServerClient.ConnectAsync(
                             messageHandler,
                             cancellationToken: TestContext.Current.CancellationToken))
            {
                var models = await client.ModelListAsync(new ModelListParams(), TestContext.Current.CancellationToken);
                Assert.NotNull(models.Data);
            }

            await process.WaitForExitAsync(TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }

            await standardErrorDrain;
        }
    }

    [Fact]
    public async Task StartAsync_UnknownMethod_PreservesRemoteError()
    {
        await using var client = await CodexAppServerClient.StartAsync(
            CreateClientOptions(),
            TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<CodexAppServerException>(() =>
            client.InvokeAsync<System.Text.Json.JsonElement>(
                "transport/notRegisteredInRealCodexCli",
                new { },
                TestContext.Current.CancellationToken));

        Assert.Equal(-32600, exception.Code);
        Assert.StartsWith("Invalid request: unknown variant", exception.Message);
        Assert.Contains("transport/notRegisteredInRealCodexCli", exception.Message);
        Assert.Null(exception.ErrorData);
    }

    [Fact]
    public async Task StartAsync_CancelCommandExec_LeavesConnectionUsableForFileRead()
    {
        await using var client = await CodexAppServerClient.StartAsync(
            CreateClientOptions(),
            TestContext.Current.CancellationToken);

        using var cancellation = new CancellationTokenSource();
        var command = client.CommandExecAsync(new CommandExecParams
        {
            Command = ["node", "-e", "setTimeout(() => {}, 10000)"],
            TimeoutMs = 15000,
        }, cancellation.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
        Assert.False(command.IsCompleted);
        cancellation.Cancel();

        var completed = await Task.WhenAny(
            command,
            Task.Delay(TimeSpan.FromSeconds(6), TestContext.Current.CancellationToken));
        Assert.Same(command, completed);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => command);

        var fixtureReadme = FixtureReadme();
        var file = await client.FsReadFileAsync(new FsReadFileParams { Path = fixtureReadme }, TestContext.Current.CancellationToken);
        Assert.Equal(await File.ReadAllTextAsync(fixtureReadme, TestContext.Current.CancellationToken),
            System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(file.DataBase64)));
    }

    [Fact]
    public async Task StartAsync_TypedThreadSubscriptions_PreserveOrderAndThreadIds()
    {
        await using var client = await CodexAppServerClient.StartAsync(
            CreateClientOptions(),
            TestContext.Current.CancellationToken);
        var cancellationToken = TestContext.Current.CancellationToken;
        var firstSubscription = client.SubscribeThreadStarted(cancellationToken: cancellationToken);
        var secondSubscription = client.SubscribeThreadStarted(cancellationToken: cancellationToken);
        await using var firstNotifications = firstSubscription.GetAsyncEnumerator(cancellationToken);
        await using var secondNotifications = secondSubscription.GetAsyncEnumerator(cancellationToken);

        var firstThread = await client.ThreadStartAsync(new ThreadStartParams { Ephemeral = true }, cancellationToken);
        var secondThread = await client.ThreadStartAsync(new ThreadStartParams { Ephemeral = true }, cancellationToken);
        Assert.NotEqual(firstThread.Thread.Id, secondThread.Thread.Id);

        foreach (var notifications in new[] { firstNotifications, secondNotifications })
        {
            Assert.True(await notifications.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), cancellationToken));
            Assert.Equal(firstThread.Thread.Id, notifications.Current.ThreadId);
            Assert.Equal(firstThread.Thread.Id, notifications.Current.Parameters.Thread.Id);

            Assert.True(await notifications.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), cancellationToken));
            Assert.Equal(secondThread.Thread.Id, notifications.Current.ThreadId);
            Assert.Equal(secondThread.Thread.Id, notifications.Current.Parameters.Thread.Id);
        }
    }

    [Fact]
    public async Task Subscribe_CanceledBeforeEnumeration_RemovesLiveSubscription()
    {
        await using var client = await CodexAppServerClient.StartAsync(
            CreateClientOptions(),
            TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        var subscription = client.Subscribe(cancellation.Token);
        var subscriptions = GetSubscribers(client);
        Assert.Single(subscriptions);

        cancellation.Cancel();
        Assert.Empty(subscriptions);

        await using var notifications = subscription.GetAsyncEnumerator(cancellation.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => notifications.MoveNextAsync().AsTask());
    }

    public void Dispose() => Directory.Delete(_sqliteHome, recursive: true);

    private CodexAppServerClientOptions CreateClientOptions() => new()
    {
        ExecutablePath = FixtureExecutable(),
        // Parallel app-server processes must not initialize the same SQLite database.
        Arguments = ["--config", $"sqlite_home={System.Text.Json.JsonSerializer.Serialize(_sqliteHome)}"],
    };

    private static string FixtureExecutable([CallerFilePath] string sourceFile = "")
    {
        var platform = OperatingSystem.IsWindows() ? "win32" : OperatingSystem.IsMacOS() ? "darwin" : "linux";
        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => throw new PlatformNotSupportedException($"The Codex fixture does not support {RuntimeInformation.ProcessArchitecture}.")
        };
        var packagePath = Path.GetFullPath(
            $"../fixtures/codex-app-server/node_modules/@openai/codex-{platform}-{architecture}/vendor",
            Path.GetDirectoryName(sourceFile)!);
        var targetDirectory = Directory.EnumerateDirectories(packagePath).Single();
        return Path.Combine(targetDirectory, "bin", OperatingSystem.IsWindows() ? "codex.exe" : "codex");
    }

    private static string FixtureReadme([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath("../fixtures/codex-app-server/README.md", Path.GetDirectoryName(sourceFile)!);

    private static IDictionary GetSubscribers(CodexAppServerClient client)
    {
        var subscriptions = typeof(CodexAppServerClient)
            .GetField("_notificationSubscriptions", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(client)!;
        return (IDictionary)subscriptions.GetType()
            .GetField("_subscribers", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(subscriptions)!;
    }
}
