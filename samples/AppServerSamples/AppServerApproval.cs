using OpenAI.CodexSdk.AppServer;
using OpenAI.CodexSdk.AppServer.Protocol;
using OpenAI.CodexSdk.AppServer.Protocol.V1;

namespace OpenAI.Codex.Samples.AppServerSamples;

public static class AppServerApproval
{
    public static async Task RunAsync(
        CodexAppServerClientOptions clientOptions,
        CancellationToken cancellationToken = default)
    {
        await using var client = await CodexAppServerClient.StartAsync(clientOptions, cancellationToken);
        client.RegisterItemCommandExecutionRequestApprovalHandler((request, token) =>
        {
            token.ThrowIfCancellationRequested();
            Console.WriteLine($"Command requested: {request.Command.GetValueOrDefault() ?? "(not provided)"}");
            Console.Write("Approve this command? [y/N] ");
            var approved = string.Equals(Console.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase);
            CommandExecutionApprovalDecision decision = approved
                ? new StringCommandExecutionApprovalDecision
                {
                    Value = StringCommandExecutionApprovalDecisionValue.Accept,
                }
                : new StringCommandExecutionApprovalDecisionDB868C0C
                {
                    Value = StringCommandExecutionApprovalDecisionValueDB868C0C.Decline,
                };
            return Task.FromResult(new CommandExecutionRequestApprovalResponse { Decision = decision });
        });

        var thread = await client.ThreadStartAsync(
            new ThreadStartParams
            {
                ApprovalPolicy = new StringAskForApproval { Value = StringAskForApprovalValue.OnRequest },
            },
            cancellationToken);
        var notifications = client.SubscribeTurnCompleted(thread.Thread.Id, cancellationToken: cancellationToken);
        var turn = await client.TurnStartAsync(
            new TurnStartParams
            {
                ThreadId = thread.Thread.Id,
                Input = [new TextUserInput { Text = "Run a harmless terminal command that prints 'app-server approval demo', then summarize its output." }],
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
