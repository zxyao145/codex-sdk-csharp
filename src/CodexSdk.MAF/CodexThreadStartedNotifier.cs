namespace OpenAI.CodexSdk.MAF;

internal static class CodexThreadStartedNotifier
{
    public static async ValueTask NotifyAsync(
        CodexAIAgentOptions options,
        CodexAgentSession session,
        string threadId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(session);

        if (string.IsNullOrWhiteSpace(threadId))
        {
            return;
        }

        session.ThreadId = threadId;

        if (options.OnThreadStartedAsync != null)
        {
            await options.OnThreadStartedAsync(threadId, cancellationToken).ConfigureAwait(false);
        }
    }
}
