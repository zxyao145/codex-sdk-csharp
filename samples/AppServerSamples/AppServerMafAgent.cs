using OpenAI.CodexSdk.AppServer;
using OpenAI.CodexSdk.AppServer.MAF;
using System.Text.Json;

namespace OpenAI.Codex.Samples.AppServerSamples;

public static class AppServerMafAgent
{
    public static async Task RunAsync(
        CodexAppServerClientOptions clientOptions,
        CancellationToken cancellationToken = default)
    {
        await using var client = await CodexAppServerClient.StartAsync(clientOptions, cancellationToken);
        var agent = new CodexAppServerAIAgent(client);
        var session = await agent.CreateSessionAsync(cancellationToken);
        var response = await agent.RunAsync(
            "Explain what this repository does in one paragraph.",
            session,
            cancellationToken: cancellationToken);

        Console.WriteLine(response.Text);

        var serializedSession = await agent.SerializeSessionAsync(session, cancellationToken: cancellationToken);
        Console.WriteLine(JsonSerializer.Serialize(serializedSession));

        var restoredSession = await agent.DeserializeSessionAsync(serializedSession, cancellationToken: cancellationToken);
        var resumedResponse = await agent.RunAsync(
            "Continue the conversation with one additional detail.",
            restoredSession,
            cancellationToken: cancellationToken);
        Console.WriteLine(resumedResponse.Text);
    }
}
