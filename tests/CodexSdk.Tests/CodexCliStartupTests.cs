using OpenAI.Codex;
using OpenAI.CodexSdk.Utils;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Xunit;

namespace CodexSdk.Tests;

public sealed class CodexCliStartupTests : IDisposable
{
    private const string CliVersion = "codex-cli 0.162.1";
    private readonly string _directory = Path.Combine(
        AppContext.BaseDirectory, "cli startup tests", Guid.NewGuid().ToString("N"));

    public CodexCliStartupTests() => Directory.CreateDirectory(_directory);

    public static bool IsWindows => OperatingSystem.IsWindows();

    [Theory(Skip = "此测试需要 Unix shell。This test requires a Unix shell.", SkipWhen = nameof(IsWindows))]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreateProcessStartInfoAsync_LoginShellEnvironment_StartsNpmCodex(bool relativePath)
    {
        var environment = await CreateEnvironmentAsync(relativePath);
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        var workingDirectory = Path.Combine(_directory, "workspace");
        var exec = new CodexExec(env: environment);

        var start = await exec.CreateProcessStartInfoAsync(
            new CodexExecArgs { Input = "", WorkingDirectory = workingDirectory, ApiKey = "request-key" },
            TestContext.Current.CancellationToken);

        Assert.Equal(Path.Combine(_directory, "cli tools", "codex"), start.FileName);
        Assert.Equal(workingDirectory, start.WorkingDirectory);
        Assert.Equal(ExpectedPath(_directory), start.Environment["PATH"]);
        Assert.Equal("execution-token", start.Environment["AGW_TOKEN"]);
        Assert.Equal("request-key", start.Environment["CODEX_API_KEY"]);
        Assert.False(start.Environment.ContainsKey("CODEX_SHELL_ONLY"));
        await AssertVersionAsync(start);
        Assert.Equal("", environment["PATH"]);
        Assert.Equal(originalPath, Environment.GetEnvironmentVariable("PATH"));
    }

    [Fact(Skip = "此测试需要 Unix shell。This test requires a Unix shell.", SkipWhen = nameof(IsWindows))]
    public async Task CreateProcessStartInfoAsync_InheritedLoginShellEnvironment_StartsNpmCodex()
    {
        var environment = await CreateEnvironmentAsync(relativePath: true);
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "CodexSdk.Tests"))
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var (key, value) in environment)
            start.Environment[key] = value;
        start.ArgumentList.Add("-explicit");
        start.ArgumentList.Add("only");
        start.ArgumentList.Add("-method");
        start.ArgumentList.Add($"{typeof(CodexCliStartupTests).FullName}.{nameof(InheritedEnvironmentProbe_StartsNpmCodex)}");
        start.ArgumentList.Add("-noLogo");

        var result = await RunProcessAsync(start);

        Assert.True(result.ExitCode == 0, $"{result.Output}\n{result.Error}");
    }

    // 独立测试进程继承受控环境，避免修改并行测试共享的进程环境。
    // The isolated test process inherits a controlled environment without changing shared test state.
    [Fact(Explicit = true)]
    public async Task InheritedEnvironmentProbe_StartsNpmCodex()
    {
        var home = Environment.GetEnvironmentVariable("HOME")!;
        var exec = new CodexExec();
        var start = await exec.CreateProcessStartInfoAsync(
            new CodexExecArgs { Input = "", WorkingDirectory = Path.Combine(home, "workspace") },
            TestContext.Current.CancellationToken);

        Assert.Equal(Path.Combine(home, "cli tools", "codex"), start.FileName);
        await AssertVersionAsync(start);
        Assert.Equal(ExpectedPath(home), start.Environment["PATH"]);
        Assert.Equal("", Environment.GetEnvironmentVariable("PATH"));
    }

    [Fact(Skip = "此测试需要 Unix shell。This test requires a Unix shell.", SkipWhen = nameof(IsWindows))]
    public async Task NpmLauncher_EmptyPath_ReportsMissingNode()
    {
        var environment = await CreateEnvironmentAsync(relativePath: false);
        var start = new ProcessStartInfo(Path.Combine(_directory, "cli tools", "codex"))
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.Environment.Clear();
        foreach (var (key, value) in environment)
            start.Environment[key] = value;
        start.ArgumentList.Add("--version");

        var result = await RunProcessAsync(start);

        Assert.Equal(127, result.ExitCode);
        Assert.Contains("node", result.Error);
    }

    [Fact(Skip = "此测试需要 Unix shell。This test requires a Unix shell.", SkipWhen = nameof(IsWindows))]
    public async Task CreateProcessStartInfoAsync_ExplicitNpmPath_StartsWithProvidedEnvironment()
    {
        var environment = await CreateEnvironmentAsync(relativePath: false);
        environment["PATH"] = ExpectedPath(_directory);
        var executable = Path.Combine(_directory, "cli tools", "codex");
        var start = await new CodexExec(executablePath: executable, env: environment)
            .CreateProcessStartInfoAsync(new CodexExecArgs { Input = "" }, TestContext.Current.CancellationToken);

        Assert.Equal(executable, start.FileName);
        Assert.Equal(environment["PATH"], start.Environment["PATH"]);
        await AssertVersionAsync(start);
    }

    private async Task<Dictionary<string, string>> CreateEnvironmentAsync(bool relativePath)
    {
        var token = TestContext.Current.CancellationToken;
        var launcher = Path.Combine(FixtureDirectory(), "node_modules", "@openai", "codex", "bin", "codex.js");
        Assert.True(File.Exists(launcher), "Run npm ci --prefix tests/fixtures/codex-cli before the CLI startup tests.");
        var node = await CommandUtil.FindAsync("node", new CommandLookupOptions { UseLoginShell = false }, token);
        Assert.NotNull(node.Path);
        var cliDirectory = Path.Combine(_directory, "cli tools");
        var nodeDirectory = Path.Combine(_directory, "node tools");
        Directory.CreateDirectory(cliDirectory);
        Directory.CreateDirectory(nodeDirectory);
        Directory.CreateDirectory(Path.Combine(_directory, "workspace"));
        File.CreateSymbolicLink(Path.Combine(cliDirectory, "codex"), launcher);
        File.CreateSymbolicLink(Path.Combine(nodeDirectory, "node"), node.Path);
        var shell = OperatingSystem.IsMacOS() ? "/bin/zsh" : "/bin/bash";
        var shellConfiguration = OperatingSystem.IsMacOS() ? ".zshrc" : ".bash_profile";
        var path = relativePath
            ? "cli tools:node tools::/usr/bin:/bin:/usr/sbin:/sbin"
            : "$HOME/cli tools:$HOME/node tools:/usr/bin:/bin:/usr/sbin:/sbin";
        await File.WriteAllTextAsync(Path.Combine(_directory, shellConfiguration),
            $"cd \"$HOME\"\nexport PATH=\"{path}\"\nexport CODEX_SHELL_ONLY=shell-value\n", token);
        return new Dictionary<string, string>
        {
            ["PATH"] = "",
            ["HOME"] = _directory,
            ["ZDOTDIR"] = _directory,
            ["SHELL"] = shell,
            ["AGW_TOKEN"] = "execution-token",
        };
    }

    private static string ExpectedPath(string directory) =>
        $"{Path.Combine(directory, "cli tools")}:{Path.Combine(directory, "node tools")}:/usr/bin:/bin:/usr/sbin:/sbin";

    private static string FixtureDirectory([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath("../fixtures/codex-cli", Path.GetDirectoryName(sourceFile)!);

    private static async Task AssertVersionAsync(ProcessStartInfo start)
    {
        start.ArgumentList.Clear();
        start.ArgumentList.Add("--version");
        var result = await RunProcessAsync(start);
        Assert.True(result.ExitCode == 0, $"Exit code {result.ExitCode}: {result.Error}");
        Assert.Equal(CliVersion, result.Output.Trim());
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunProcessAsync(ProcessStartInfo start)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var process = new Process { StartInfo = start };
        Assert.True(process.Start());
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            return (process.ExitCode, await output, await error);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
