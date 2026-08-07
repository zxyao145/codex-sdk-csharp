using OpenAI.CodexSdk.MAF;
using Xunit;

namespace CodexSdk.MAF.Tests;

public class CodexAgentMetadataTests
{
    [Fact]
    public void Name_DefaultAgent_ReturnsCodex()
    {
        var agent = new CodexAIAgent();

        Assert.Equal("Codex", agent.Name);
    }
}
