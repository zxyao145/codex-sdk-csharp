using OpenAI.CodexSdk.Utils;
using Xunit;

namespace CodexSdk.Tests;

public sealed class CommandUtilTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        AppContext.BaseDirectory, "command lookup tests", Guid.NewGuid().ToString("N"));

    public CommandUtilTests() => Directory.CreateDirectory(_directory);

    public static bool IsWindows => OperatingSystem.IsWindows();
    public static bool HasZsh => !OperatingSystem.IsWindows() && File.Exists("/bin/zsh");

    [Fact]
    public async Task FindAsync_CommandOnPath_ReturnsExecutable()
    {
        var executable = CreateExecutable(_directory);

        var result = await CommandUtil.FindAsync("codex", Options(_directory), TestContext.Current.CancellationToken);

        Assert.Equal("codex", result.Command);
        Assert.True(result.IsInstalled);
        Assert.Equal(executable, result.Path, ignoreCase: IsWindows);
        Assert.Equal(CommandSource.Path, result.Source);
        Assert.Null(result.Error);
        Assert.Null(result.SearchPath);
    }

    [Fact]
    public async Task FindAsync_MultiplePathEntries_ReturnsFirstExecutable()
    {
        var firstDirectory = Path.Combine(_directory, "first");
        var secondDirectory = Path.Combine(_directory, "second");
        var executable = CreateExecutable(firstDirectory);
        CreateExecutable(secondDirectory);
        var searchPath = string.Join(Path.PathSeparator, "", firstDirectory, secondDirectory);

        var result = await CommandUtil.FindAsync("codex", Options(searchPath), TestContext.Current.CancellationToken);

        Assert.Equal(executable, result.Path, ignoreCase: IsWindows);
    }

    [Fact]
    public async Task FindAsync_RelativePathEntry_ReturnsAbsolutePath()
    {
        var executable = CreateExecutable(_directory);
        var searchPath = Path.GetRelativePath(Environment.CurrentDirectory, _directory);

        var result = await CommandUtil.FindAsync("codex", Options(searchPath), TestContext.Current.CancellationToken);

        Assert.Equal(executable, result.Path, ignoreCase: IsWindows);
        Assert.True(Path.IsPathFullyQualified(result.Path!));
    }

    [Fact]
    public async Task FindAsync_ExplicitPath_ReturnsExplicitExecutable()
    {
        var executable = CreateExecutable(_directory);
        var otherDirectory = Path.Combine(_directory, "other");
        CreateExecutable(otherDirectory);

        var result = await CommandUtil.FindAsync(
            executable, Options(otherDirectory), TestContext.Current.CancellationToken);

        Assert.Equal(executable, result.Path, ignoreCase: IsWindows);
        Assert.Equal(CommandSource.ExplicitPath, result.Source);
        Assert.Null(result.SearchPath);
    }

    [Fact]
    public async Task FindAsync_MissingExplicitPath_ReturnsNotInstalled()
    {
        var executable = CreateExecutable(_directory);
        var missingPath = Path.Combine(_directory, "missing", Path.GetFileName(executable));

        var result = await CommandUtil.FindAsync(
            missingPath, Options(_directory), TestContext.Current.CancellationToken);

        Assert.False(result.IsInstalled);
        Assert.Null(result.Path);
        Assert.Null(result.Source);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task FindAsync_EmptyEnvironment_ReturnsNotInstalled()
    {
        var result = await CommandUtil.FindAsync(
            "codex",
            new CommandLookupOptions { Environment = new Dictionary<string, string>() },
            TestContext.Current.CancellationToken);

        Assert.False(result.IsInstalled);
        Assert.Null(result.Path);
        Assert.Null(result.Source);
        Assert.Null(result.Error);
    }

    [Theory(Skip = "此测试需要 Windows。This test requires Windows.", SkipUnless = nameof(IsWindows))]
    [InlineData(null, true)]
    [InlineData(".exe", true)]
    [InlineData(".CMD", false)]
    public async Task FindAsync_WindowsPathExt_UsesConfiguredExtensions(string? pathExt, bool expectedInstalled)
    {
        var executable = CreateExecutable(_directory);
        var environment = new Dictionary<string, string> { ["Path"] = $"\"{_directory}\"" };
        if (pathExt is not null)
            environment["PathExt"] = pathExt;

        var result = await CommandUtil.FindAsync(
            "codex",
            new CommandLookupOptions { UseLoginShell = false, Environment = environment },
            TestContext.Current.CancellationToken);

        Assert.Equal(expectedInstalled, result.IsInstalled);
        Assert.Equal(expectedInstalled ? executable : null, result.Path, ignoreCase: true);
        Assert.Equal(expectedInstalled ? CommandSource.Path : (CommandSource?)null, result.Source);
        Assert.Null(result.Error);
    }

    [Fact(Skip = "此测试需要 Unix。This test requires Unix.", SkipWhen = nameof(IsWindows))]
    public async Task FindAsync_NonExecutableUnixFile_ReturnsNotInstalled()
    {
        var executable = CreateExecutable(_directory);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        var result = await CommandUtil.FindAsync("codex", Options(_directory), TestContext.Current.CancellationToken);

        Assert.False(result.IsInstalled);
        Assert.Null(result.Path);
    }

    [Fact(Skip = "此测试需要 Unix。This test requires Unix.", SkipWhen = nameof(IsWindows))]
    public async Task FindAsync_SymbolicLink_PreservesEntrypointPath()
    {
        var executable = CreateExecutable(Path.Combine(_directory, "target"));
        var link = Path.Combine(_directory, "codex");
        File.CreateSymbolicLink(link, executable);

        var result = await CommandUtil.FindAsync("codex", Options(_directory), TestContext.Current.CancellationToken);

        Assert.Equal(link, result.Path);
        Assert.Equal(CommandSource.Path, result.Source);
    }

    [Fact(Skip = "此测试需要 /bin/zsh。This test requires /bin/zsh.", SkipUnless = nameof(HasZsh))]
    public async Task FindAsync_LoginShellPath_ReturnsExecutableFromShellWorkingDirectory()
    {
        var executable = CreateExecutable(Path.Combine(_directory, "tools"));
        await File.WriteAllTextAsync(
            Path.Combine(_directory, ".zshrc"),
            "printf 'shell startup\\n'\ncd \"$HOME\"\nexport PATH=tools\n",
            TestContext.Current.CancellationToken);

        var result = await CommandUtil.FindAsync(
            "codex", ShellOptions(), TestContext.Current.CancellationToken);

        Assert.True(result.IsInstalled, result.Error);
        Assert.Equal(executable, result.Path);
        Assert.Equal(CommandSource.LoginShell, result.Source);
        Assert.Null(result.Error);
        Assert.Equal(Path.GetDirectoryName(executable), result.SearchPath);
    }

    [Fact(Skip = "此测试需要 /bin/zsh。This test requires /bin/zsh.", SkipUnless = nameof(HasZsh))]
    public async Task FindAsync_LoginShellPath_PreservesDirectoryOrderAndResolvesRelativeEntries()
    {
        var executable = CreateExecutable(Path.Combine(_directory, "cli tools"));
        await File.WriteAllTextAsync(Path.Combine(_directory, ".zshrc"),
            "cd \"$HOME\"\nexport PATH=\"cli tools::./node tools:.:/usr/bin\"\n",
            TestContext.Current.CancellationToken);

        var result = await CommandUtil.FindAsync("codex", ShellOptions(), TestContext.Current.CancellationToken);

        Assert.Equal(executable, result.Path);
        Assert.Equal(string.Join(Path.PathSeparator,
            Path.Combine(_directory, "cli tools"), Path.Combine(_directory, "node tools"), _directory, "/usr/bin"),
            result.SearchPath);
    }

    [Fact(Skip = "此测试需要 /bin/zsh。This test requires /bin/zsh.", SkipUnless = nameof(HasZsh))]
    public async Task FindAsync_LoginShellCommandMissing_ReturnsNoSearchPath()
    {
        await File.WriteAllTextAsync(Path.Combine(_directory, ".zshrc"),
            "export PATH=\"$HOME/missing\"\n", TestContext.Current.CancellationToken);

        var result = await CommandUtil.FindAsync("codex", ShellOptions(), TestContext.Current.CancellationToken);

        Assert.False(result.IsInstalled);
        Assert.Null(result.SearchPath);
        Assert.Null(result.Error);
    }

    [Fact(Skip = "此测试需要 /bin/zsh。This test requires /bin/zsh.", SkipUnless = nameof(HasZsh))]
    public async Task FindAsync_LoginShellTimeout_ReturnsDiagnostic()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_directory, ".zshrc"), "/bin/sleep 30\n", TestContext.Current.CancellationToken);

        var result = await CommandUtil.FindAsync(
            "codex",
            ShellOptions() with { ShellTimeout = TimeSpan.FromMilliseconds(200) },
            TestContext.Current.CancellationToken);

        Assert.False(result.IsInstalled);
        Assert.Equal("Login shell lookup timed out.", result.Error);
    }

    [Fact(Skip = "此测试需要 /bin/zsh。This test requires /bin/zsh.", SkipUnless = nameof(HasZsh))]
    public async Task FindAsync_LoginShellCanceled_ThrowsOperationCanceledException()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_directory, ".zshrc"), "/bin/sleep 30\n", TestContext.Current.CancellationToken);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CommandUtil.FindAsync(
            "codex", ShellOptions(), cancellation.Token));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("codex\0")]
    public async Task FindAsync_InvalidCommand_ThrowsArgumentException(string command)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => CommandUtil.FindAsync(
            command, Options(_directory), TestContext.Current.CancellationToken));
    }

    private static CommandLookupOptions Options(string searchPath) => new()
    {
        UseLoginShell = false,
        Environment = new Dictionary<string, string> { ["PATH"] = searchPath }
    };

    private CommandLookupOptions ShellOptions() => new()
    {
        Environment = new Dictionary<string, string>
        {
            ["PATH"] = "",
            ["SHELL"] = "/bin/zsh",
            ["HOME"] = _directory,
            ["ZDOTDIR"] = _directory
        }
    };

    private static string CreateExecutable(string directory)
    {
        Directory.CreateDirectory(directory);
        var executable = Path.Combine(directory, IsWindows ? "codex.exe" : "codex");
        // 使用当前测试进程的可执行文件作为查找目标。
        // Use the current test process executable as the lookup target.
        File.Copy(Environment.ProcessPath!, executable);
        return executable;
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
