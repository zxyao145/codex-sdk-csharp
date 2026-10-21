using OpenAI.Codex;
using OpenAI.CodexSdk;
using Xunit;

namespace CodexSdk.Tests;

public sealed class CodexExecTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        AppContext.BaseDirectory, "process start tests", Guid.NewGuid().ToString("N"));

    public CodexExecTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task RunAsync_MissingExplicitExecutable_ThrowsLookupError()
    {
        var path = Path.Combine(AppContext.BaseDirectory, Guid.NewGuid().ToString("N"), "codex");
        var exec = new CodexExec(executablePath: path);
        await using var lines = exec.RunAsync(
            new CodexExecArgs { Input = "test" },
            TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await lines.MoveNextAsync());

        Assert.Contains(path, exception.Message);
        Assert.Contains("CodexPathOverride", exception.Message);
    }

    [Fact]
    public async Task RunAsync_CanceledLookup_ThrowsOperationCanceledException()
    {
        var path = Path.Combine(AppContext.BaseDirectory, Guid.NewGuid().ToString("N"), "codex");
        var exec = new CodexExec(executablePath: path);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();
        await using var lines = exec.RunAsync(
            new CodexExecArgs { Input = "test" },
            cancellation.Token).GetAsyncEnumerator(TestContext.Current.CancellationToken);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await lines.MoveNextAsync());
    }

    [Fact]
    public void BuildArgs_WhenContextLimitsArePositive_ForwardsConfigOverrides()
    {
        var commandArgs = BuildArgs(new ThreadOptions
        {
            ModelContextWindow = 1_000_000,
            ModelAutoCompactTokenLimit = 900_000,
        });

        Assert.Equal(
            ["model_context_window=1000000"],
            CollectConfigValues(commandArgs, "model_context_window"));
        Assert.Equal(
            ["model_auto_compact_token_limit=900000"],
            CollectConfigValues(commandArgs, "model_auto_compact_token_limit"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void BuildArgs_WhenContextLimitsAreNotPositive_OmitsConfigOverrides(long? value)
    {
        var commandArgs = BuildArgs(new ThreadOptions
        {
            ModelContextWindow = value,
            ModelAutoCompactTokenLimit = value,
        });

        Assert.Empty(CollectConfigValues(commandArgs, "model_context_window"));
        Assert.Empty(CollectConfigValues(commandArgs, "model_auto_compact_token_limit"));
    }

    [Fact]
    public void BuildArgs_WhenOnlyOneContextLimitIsPositive_ForwardsOnlyPositiveValue()
    {
        var commandArgs = BuildArgs(new ThreadOptions
        {
            ModelContextWindow = 1_000_000,
            ModelAutoCompactTokenLimit = 0,
        });

        Assert.Equal(
            ["model_context_window=1000000"],
            CollectConfigValues(commandArgs, "model_context_window"));
        Assert.Empty(CollectConfigValues(commandArgs, "model_auto_compact_token_limit"));
    }

    [Fact]
    public void BuildArgs_WhenGlobalAndThreadLimitsAreSet_ThreadValuesComeLast()
    {
        var config = new Dictionary<string, CodexConfigValue>
        {
            ["model_context_window"] = 200_000,
            ["model_auto_compact_token_limit"] = 180_000,
        };
        var commandArgs = BuildArgs(
            new ThreadOptions
            {
                ModelContextWindow = 1_000_000,
                ModelAutoCompactTokenLimit = 900_000,
            },
            config);

        Assert.Equal(
            ["model_context_window=200000", "model_context_window=1000000"],
            CollectConfigValues(commandArgs, "model_context_window"));
        Assert.Equal(
            ["model_auto_compact_token_limit=180000", "model_auto_compact_token_limit=900000"],
            CollectConfigValues(commandArgs, "model_auto_compact_token_limit"));
    }

    [Fact]
    public void BuildArgs_WhenThreadLimitsAreNotPositive_DoesNotMaskGlobalValues()
    {
        var config = new Dictionary<string, CodexConfigValue>
        {
            ["model_context_window"] = 200_000,
            ["model_auto_compact_token_limit"] = 180_000,
        };
        var commandArgs = BuildArgs(
            new ThreadOptions
            {
                ModelContextWindow = 0,
                ModelAutoCompactTokenLimit = -1,
            },
            config);

        Assert.Equal(
            ["model_context_window=200000"],
            CollectConfigValues(commandArgs, "model_context_window"));
        Assert.Equal(
            ["model_auto_compact_token_limit=180000"],
            CollectConfigValues(commandArgs, "model_auto_compact_token_limit"));
    }

    [Fact]
    public async Task CreateProcessStartInfoAsync_EnvProvided_DoesNotInheritProcessEnvironment()
    {
        var exec = new CodexExec(
            executablePath: Environment.ProcessPath!,
            env: new Dictionary<string, string> { ["FOO"] = "bar" });
        var psi = await exec.CreateProcessStartInfoAsync(
            new CodexExecArgs { Input = "test", ApiKey = "sk-test" }, TestContext.Current.CancellationToken);

        Assert.Equal(
            ["CODEX_API_KEY", "CODEX_INTERNAL_ORIGINATOR_OVERRIDE", "FOO"],
            psi.Environment.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("bar", psi.Environment["FOO"]);
        Assert.Equal("sk-test", psi.Environment["CODEX_API_KEY"]);
    }

    [Fact]
    public async Task CreateProcessStartInfoAsync_NoEnvOverride_InheritsProcessEnvironment()
    {
        var exec = new CodexExec(executablePath: Environment.ProcessPath!);
        var psi = await exec.CreateProcessStartInfoAsync(
            new CodexExecArgs { Input = "test" }, TestContext.Current.CancellationToken);

        Assert.Equal(Environment.GetEnvironmentVariable("PATH"), psi.Environment["PATH"]);
        Assert.Equal(
            Environment.GetEnvironmentVariable("CODEX_INTERNAL_ORIGINATOR_OVERRIDE") ?? "codex_sdk_cs",
            psi.Environment["CODEX_INTERNAL_ORIGINATOR_OVERRIDE"]);
    }

    [Fact]
    public async Task CreateProcessStartInfoAsync_EnvProvided_UsesProvidedPath()
    {
        var command = $"codex-{Guid.NewGuid():N}";
        var executable = Path.Combine(_directory, OperatingSystem.IsWindows() ? command + ".exe" : command);
        File.Copy(Environment.ProcessPath!, executable);
        var pathKey = OperatingSystem.IsWindows() ? "Path" : "PATH";
        var environment = new Dictionary<string, string> { [pathKey] = _directory };
        var exec = new CodexExec(executablePath: command, env: environment);

        var start = await exec.CreateProcessStartInfoAsync(
            new CodexExecArgs { Input = "" }, TestContext.Current.CancellationToken);

        Assert.Equal(executable, start.FileName, ignoreCase: OperatingSystem.IsWindows());
        Assert.Equal(_directory, start.Environment[pathKey]);
        Assert.Single(environment);
    }

    [Fact]
    public async Task CreateProcessStartInfoAsync_EmptyEnvironment_ThrowsLookupError()
    {
        var exec = new CodexExec(env: new Dictionary<string, string>());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            exec.CreateProcessStartInfoAsync(new CodexExecArgs { Input = "" }, TestContext.Current.CancellationToken));

        Assert.Contains("was not found", exception.Message);
    }

    [Fact]
    public async Task CreateProcessStartInfoAsync_SdkVariablesProvided_PreservesPrecedence()
    {
        var environment = new Dictionary<string, string>
        {
            ["CODEX_INTERNAL_ORIGINATOR_OVERRIDE"] = "caller",
            ["CODEX_API_KEY"] = "environment-key",
        };
        var exec = new CodexExec(executablePath: Environment.ProcessPath!, env: environment);

        var start = await exec.CreateProcessStartInfoAsync(
            new CodexExecArgs { Input = "", ApiKey = "request-key" }, TestContext.Current.CancellationToken);

        Assert.Equal("caller", start.Environment["CODEX_INTERNAL_ORIGINATOR_OVERRIDE"]);
        Assert.Equal("request-key", start.Environment["CODEX_API_KEY"]);
        Assert.Equal("environment-key", environment["CODEX_API_KEY"]);
    }

    [Fact]
    public async Task CreateProcessStartInfoAsync_ConcurrentCalls_KeepEnvironmentSnapshotsIndependent()
    {
        var environment = new Dictionary<string, string> { ["FOO"] = "original" };
        var exec = new CodexExec(executablePath: Environment.ProcessPath!, env: environment);
        var args = new CodexExecArgs { Input = "" };

        var results = await Task.WhenAll(
            exec.CreateProcessStartInfoAsync(args, TestContext.Current.CancellationToken),
            exec.CreateProcessStartInfoAsync(args, TestContext.Current.CancellationToken));
        results[0].Environment["FOO"] = "changed";

        Assert.Equal("original", results[1].Environment["FOO"]);
        Assert.Equal("original", environment["FOO"]);
    }

    [Fact]
    public void Constructor_WithCodexPathOverride_DoesNotSearchForCli()
    {
        var exception = Record.Exception(() => new CodexExec(executablePath: "/nonexistent/codex"));

        Assert.Null(exception);
    }

    private static IReadOnlyList<string> BuildArgs(
        ThreadOptions threadOptions,
        IReadOnlyDictionary<string, CodexConfigValue>? config = null)
    {
        var options = new CodexOptions { Config = config };
        var exec = new CodexExec(executablePath: "codex", configOverrides: config);
        var thread = new OpenAI.CodexSdk.Thread(exec, options, threadOptions);
        var execArgs = thread.BuildExecArgs("test", [], schemaPath: null, new TurnOptions());

        return exec.BuildArgs(execArgs);
    }

    private static IReadOnlyList<string> CollectConfigValues(
        IReadOnlyList<string> commandArgs,
        string key)
    {
        var values = new List<string>();
        for (var i = 0; i < commandArgs.Count - 1; i++)
        {
            if (commandArgs[i] == "--config" && commandArgs[i + 1].StartsWith($"{key}=", StringComparison.Ordinal))
                values.Add(commandArgs[i + 1]);
        }

        return values;
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
