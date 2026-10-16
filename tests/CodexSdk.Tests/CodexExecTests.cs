using OpenAI.Codex;
using OpenAI.CodexSdk;
using System.Diagnostics;
using Xunit;

namespace CodexSdk.Tests;

public sealed class CodexExecTests
{
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
    public void ApplyEnvironment_WhenEnvOverrideProvided_DoesNotInheritProcessEnvironment()
    {
        var exec = new CodexExec(
            executablePath: "codex",
            env: new Dictionary<string, string> { ["FOO"] = "bar" });
        var psi = new ProcessStartInfo();

        exec.ApplyEnvironment(psi.Environment, new CodexExecArgs { Input = "test", ApiKey = "sk-test" });

        Assert.Equal(
            ["CODEX_API_KEY", "CODEX_INTERNAL_ORIGINATOR_OVERRIDE", "FOO"],
            psi.Environment.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("bar", psi.Environment["FOO"]);
        Assert.Equal("sk-test", psi.Environment["CODEX_API_KEY"]);
    }

    [Fact]
    public void ApplyEnvironment_WhenNoEnvOverride_InheritsProcessEnvironment()
    {
        var exec = new CodexExec(executablePath: "codex");
        var psi = new ProcessStartInfo();

        exec.ApplyEnvironment(psi.Environment, new CodexExecArgs { Input = "test" });

        Assert.Equal(Environment.GetEnvironmentVariable("PATH"), psi.Environment["PATH"]);
        Assert.Equal("codex_sdk_cs", psi.Environment["CODEX_INTERNAL_ORIGINATOR_OVERRIDE"]);
    }

    [Fact]
    public void FindCli_InFallbackDirectory_ReturnsPath()
    {
        var root = Directory.CreateTempSubdirectory("codex-sdk-tests-");
        try
        {
            var pathDir = root.CreateSubdirectory("path");
            var codexPath = Path.Combine(root.CreateSubdirectory("fallback").FullName, "codex");
            File.WriteAllText(codexPath, "");

            var result = CodexExec.FindCli("codex", pathDir.FullName, [codexPath]);

            Assert.Equal(codexPath, result);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void DefaultFallbackLocations_IncludeHomebrewBin()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Homebrew install locations only apply to Unix.");

        Assert.Contains("/opt/homebrew/bin/codex", CodexExec.DefaultFallbackLocations("codex"));
    }

    [Fact]
    public void FindCli_NotFound_MessageMentionsOverrideNotNodeJs()
    {
        var root = Directory.CreateTempSubdirectory("codex-sdk-tests-");
        try
        {
            var missingPath = Path.Combine(root.FullName, "codex");

            var exception = Assert.Throws<Exception>(
                () => CodexExec.FindCli("codex", root.FullName, [missingPath]));

            Assert.Contains("CodexPathOverride is not set", exception.Message);
            Assert.Contains(missingPath, exception.Message);
            Assert.DoesNotContain("Node.js", exception.Message);
        }
        finally
        {
            root.Delete(recursive: true);
        }
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
}
