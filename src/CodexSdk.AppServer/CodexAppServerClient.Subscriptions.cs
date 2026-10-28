using System.Runtime.CompilerServices;

namespace OpenAI.CodexSdk.AppServer;

/// <summary>带有强类型参数的通知。A notification with strongly typed parameters.</summary>
public sealed record AppServerNotification<T>(string Method, T Parameters, string? ThreadId, string? TurnId);

public sealed partial class CodexAppServerClient
{
    private IAsyncEnumerable<AppServerNotification<T>> Subscribe<T>(
        string method,
        string? threadId,
        string? turnId,
        CancellationToken cancellationToken)
    {
        var source = Subscribe(cancellationToken);
        return Filter(source, method, threadId, turnId, cancellationToken);

        static async IAsyncEnumerable<AppServerNotification<T>> Filter(
            IAsyncEnumerable<AppServerNotification> source,
            string method,
            string? threadId,
            string? turnId,
            [EnumeratorCancellation] CancellationToken token)
        {
            await foreach (var notification in source.WithCancellation(token).ConfigureAwait(false))
            {
                if (notification.Method == method
                    && (threadId is null || notification.ThreadId == threadId)
                    && (turnId is null || notification.TurnId == turnId))
                    yield return new AppServerNotification<T>(method, (T)notification.Parameters, notification.ThreadId, notification.TurnId);
            }
        }
    }
}
