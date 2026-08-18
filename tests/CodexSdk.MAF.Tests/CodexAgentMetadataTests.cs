using OpenAI.CodexSdk;
using OpenAI.CodexSdk.MAF;
using Xunit;

namespace CodexSdk.MAF.Tests;

public class CodexAgentMetadataTests
{
    [Fact]
    public void Name_ReturnsCodex()
    {
        var agent = new CodexAIAgent(new CodexAIAgentOptions
        {
            CodexOptions = new CodexOptions { CodexPathOverride = "unused-codex" },
        });

        Assert.Equal("Codex", agent.Name);
    }
}
