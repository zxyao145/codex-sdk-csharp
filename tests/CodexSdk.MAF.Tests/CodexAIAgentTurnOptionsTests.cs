using CodexSdk.TestSupport;
using OpenAI.CodexSdk;
using OpenAI.CodexSdk.MAF;
using Xunit;

namespace CodexSdk.MAF.Tests;

public sealed class CodexAIAgentTurnOptionsTests
{
    [Fact]
    public async Task RunStreamingAsync_WithOutputSchema_PassesOutputSchemaFlagToCli()
    {
        // Arrange
        using var cli = new FakeCodexCli();
        var agent = new CodexAIAgent(
            new CodexAIAgentOptions
            {
                CodexOptions = cli.CreateOptions(),
                TurnOptions = new TurnOptions
                {
                    OutputSchema = new Dictionary<string, object?> { ["type"] = "object" },
                },
            }
        );
        var session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);

        // Act
        await foreach (
            var _ in agent.RunStreamingAsync("hi", session, cancellationToken: TestContext.Current.CancellationToken)
        ) { }

        // Assert
        var args = Assert.Single(cli.ReadArguments());
        Assert.Contains("--output-schema", args);
    }

    [Fact]
    public async Task RunAsync_WithOutputSchema_PassesOutputSchemaFlagToCli()
    {
        // Arrange
        using var cli = new FakeCodexCli();
        var agent = new CodexAIAgent(
            new CodexAIAgentOptions
            {
                CodexOptions = cli.CreateOptions(),
                TurnOptions = new TurnOptions
                {
                    OutputSchema = new Dictionary<string, object?> { ["type"] = "object" },
                },
            }
        );
        var session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);

        // Act
        await agent.RunAsync("hi", session, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var args = Assert.Single(cli.ReadArguments());
        Assert.Contains("--output-schema", args);
    }

    [Fact]
    public async Task RunStreamingAsync_WithoutTurnOptions_OmitsOutputSchemaFlag()
    {
        // Arrange
        using var cli = new FakeCodexCli();
        var agent = new CodexAIAgent(new CodexAIAgentOptions { CodexOptions = cli.CreateOptions() });
        var session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);

        // Act
        await foreach (
            var _ in agent.RunStreamingAsync("hi", session, cancellationToken: TestContext.Current.CancellationToken)
        ) { }

        // Assert
        var args = Assert.Single(cli.ReadArguments());
        Assert.DoesNotContain("--output-schema", args);
    }
}
