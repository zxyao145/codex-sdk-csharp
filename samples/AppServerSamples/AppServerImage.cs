using Microsoft.Extensions.AI;
using OpenAI.CodexSdk.AppServer;
using OpenAI.CodexSdk.AppServer.MAF;

namespace OpenAI.Codex.Samples.AppServerSamples;

public static class AppServerImage
{
    public static async Task RunAsync(
        string imagePath,
        CodexAppServerClientOptions clientOptions,
        CancellationToken cancellationToken = default)
    {
        var mediaType = Path.GetExtension(imagePath).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => throw new ArgumentException("Use a PNG, JPEG, or WebP image.", nameof(imagePath)),
        };
        var image = new DataContent(await File.ReadAllBytesAsync(imagePath, cancellationToken), mediaType);

        await using var client = await CodexAppServerClient.StartAsync(clientOptions, cancellationToken);
        var agent = new CodexAppServerAIAgent(client);
        var session = await agent.CreateSessionAsync(cancellationToken);
        var message = new ChatMessage(ChatRole.User, [new TextContent("Describe this image."), image]);
        var response = await agent.RunAsync([message], session, cancellationToken: cancellationToken);

        Console.WriteLine(response.Text);
    }
}
