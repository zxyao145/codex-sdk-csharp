using OpenAI.CodexSdk.AppServer;
using OpenAI.CodexSdk.AppServer.Protocol;

namespace OpenAI.Codex.Samples.AppServerSamples;

public static class AppServerProtocol
{
    public static async Task RunAsync(
        CodexAppServerClientOptions clientOptions,
        CancellationToken cancellationToken = default)
    {
        await using var client = await CodexAppServerClient.StartAsync(clientOptions, cancellationToken);
        var thread = await client.ThreadStartAsync(new ThreadStartParams(), cancellationToken);
        var notifications = client.SubscribeTurnCompleted(thread.Thread.Id, cancellationToken: cancellationToken);
        var turn = await client.TurnStartAsync(
            new TurnStartParams
            {
                ThreadId = thread.Thread.Id,
                Input = [new TextUserInput { Text = "Explain what this repository does in one paragraph." }],
            },
            cancellationToken);

        Console.WriteLine($"Thread ID: {thread.Thread.Id}");
        Console.WriteLine($"Turn ID: {turn.Turn.Id}");

        await foreach (var notification in notifications.WithCancellation(cancellationToken))
        {
            if (!string.Equals(notification.Parameters.Turn.Id, turn.Turn.Id, StringComparison.Ordinal))
                continue;

            if (notification.Parameters.Turn.Status == TurnStatus.Failed)
            {
                var error = notification.Parameters.Turn.Error.GetValueOrDefault();
                throw new InvalidOperationException(error?.Message ?? "The Codex app-server turn failed.");
            }

            Console.WriteLine($"Completed turn: {notification.Parameters.Turn.Id}");
            break;
        }
    }
}
