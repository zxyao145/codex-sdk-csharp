using OpenAI.Codex;
using OpenAI.CodexSdk;
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
