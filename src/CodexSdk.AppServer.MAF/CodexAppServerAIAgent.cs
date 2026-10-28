using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI.CodexSdk.AppServer;
using OpenAI.CodexSdk.AppServer.MAF.Internal;
using OpenAI.CodexSdk.AppServer.Protocol;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;

namespace OpenAI.CodexSdk.AppServer.MAF;

/// <summary>
/// Adapts Codex app-server threads to Microsoft Agent Framework.
/// 将 Codex app-server threads 适配为 Microsoft Agent Framework agent。
/// </summary>
/// <remarks>
/// When an app-server tool item has no wire call ID, the generated function call uses its item ID as the MAF correlation ID.
/// 当 app-server tool item 没有 wire call ID 时，生成的 function call 使用 item ID 作为 MAF correlation ID。
/// </remarks>
public sealed class CodexAppServerAIAgent : AIAgent
{
    private readonly CodexAppServerClient _client;
    private readonly CodexAppServerAIAgentOptions _options;

    public CodexAppServerAIAgent(CodexAppServerClient client, CodexAppServerAIAgentOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        _client = client;
        _options = options ?? new CodexAppServerAIAgentOptions();
        ArgumentNullException.ThrowIfNull(_options.ThreadStartParams);
    }

    public override string? Name => _options.Name;

    public override string? Description => _options.Description;

    protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<AgentSession>(new CodexAppServerAgentSession());

    protected override ValueTask<JsonElement> SerializeSessionCoreAsync(
        AgentSession session,
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return ValueTask.FromResult(JsonSerializer.SerializeToElement(RequireSession(session), jsonSerializerOptions));
    }

    protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
        JsonElement serializedState,
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var session = serializedState.Deserialize<CodexAppServerAgentSession>(jsonSerializerOptions)
            ?? throw new JsonException("The serialized Codex app-server session was null.");
        return ValueTask.FromResult<AgentSession>(session);
    }

    protected override async Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var contents = new List<AIContent>();
        var rawEvents = new List<AppServerNotification>();
        UsageDetails? usage = null;
        string? threadId = null;
        string? turnId = null;
        await foreach (var update in RunCoreStreamingAsync(messages, session, options, cancellationToken)
                           .ConfigureAwait(false))
        {
            contents.AddRange(update.Contents);
            if (update.RawRepresentation is AppServerNotification notification)
            {
                rawEvents.Add(notification);
                if (notification.ThreadId is not null)
                {
                    threadId = notification.ThreadId;
                }
                if (notification.TurnId is not null)
                {
                    turnId = notification.TurnId;
                }
                if (notification.Parameters is TurnCompletedNotification completed)
                {
                    threadId = completed.ThreadId;
                    turnId = completed.Turn.Id;
                }
            }

            foreach (var usageContent in update.Contents.OfType<UsageContent>())
            {
                usage ??= new UsageDetails();
                usage.Add(usageContent.Details);
            }
        }

        var response = new AgentResponse(new ChatMessage(ChatRole.Assistant, contents))
        {
            AgentId = Id,
            RawRepresentation = rawEvents,
            Usage = usage,
        };
        if (turnId is not null)
        {
            response.ResponseId = turnId;
        }
        if (threadId is not null || turnId is not null)
        {
            response.AdditionalProperties = new AdditionalPropertiesDictionary();
            if (threadId is not null)
            {
                response.AdditionalProperties["threadId"] = threadId;
            }
            if (turnId is not null)
            {
                response.AdditionalProperties["turnId"] = turnId;
            }
        }

        return response;
    }

    protected override IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default) =>
        RunEventsAsync(messages, session, options, cancellationToken);

    private async IAsyncEnumerable<AgentResponseUpdate> RunEventsAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session,
        AgentRunOptions? runOptions,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var codexSession = session is null ? new CodexAppServerAgentSession() : RequireSession(session);
        if (!codexSession.TryBeginRun())
        {
            throw new InvalidOperationException("This Codex app-server session already has an active run.");
        }

        using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var channel = Channel.CreateUnbounded<AgentResponseUpdate>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
        });
        var producer = ProduceUpdatesAsync(
            messages,
            codexSession,
            runOptions,
            channel.Writer,
            runCancellation.Token);

        try
        {
            await foreach (var update in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return update;
            }

            await producer.ConfigureAwait(false);
        }
        finally
        {
            runCancellation.Cancel();
            try
            {
                await producer.ConfigureAwait(false);
            }
            finally
            {
                codexSession.EndRun();
            }
        }
    }

    private async Task ProduceUpdatesAsync(
        IEnumerable<ChatMessage> messages,
        CodexAppServerAgentSession session,
        AgentRunOptions? runOptions,
        ChannelWriter<AgentResponseUpdate> writer,
        CancellationToken cancellationToken)
    {
        Exception? producerFailure = null;
        var requestMessages = new List<ChatMessage>();
        var responseContents = new List<AIContent>();
        string? activeTurnId = null;
        var historyStarted = false;
        var historyInvoked = false;
        var startingNewThread = session.ThreadId is not { Length: > 0 };
        var turnStartRequested = false;

        try
        {
            requestMessages = messages.ToList();
            if (_options.ChatHistoryProvider is { } historyProvider)
            {
#pragma warning disable MAAI001
                _ = await historyProvider.InvokingAsync(
                    new ChatHistoryProvider.InvokingContext(this, session, requestMessages),
                    cancellationToken).ConfigureAwait(false);
#pragma warning restore MAAI001
                historyStarted = true;
            }

            var input = CodexAppServerInputConverter.Convert(requestMessages);
            var threadId = await EnsureThreadAsync(session, cancellationToken).ConfigureAwait(false);
            var turnStart = new TurnStartParams { ThreadId = threadId, Input = input };
            if (_options.ConfigureTurnStart is { } configureTurnStart)
            {
                turnStart = configureTurnStart(runOptions, turnStart)
                    ?? throw new InvalidOperationException("The configured turn parameters were null.");
                turnStart = turnStart with { ThreadId = threadId, Input = input };
            }

            cancellationToken.ThrowIfCancellationRequested();
            var notificationStream = _client.Subscribe(cancellationToken);
            turnStartRequested = true;
            var turn = await _client.TurnStartAsync(turnStart, CancellationToken.None).ConfigureAwait(false);
            activeTurnId = turn.Turn.Id;
            cancellationToken.ThrowIfCancellationRequested();
            var converter = new CodexAppServerEventConverter();
            var completedTurn = false;

            await foreach (var notification in notificationStream.ConfigureAwait(false))
            {
                if (!string.Equals(notification.ThreadId, threadId, StringComparison.Ordinal))
                {
                    continue;
                }
                if (notification.TurnId is { } notificationTurnId &&
                    !string.Equals(notificationTurnId, activeTurnId, StringComparison.Ordinal))
                {
                    continue;
                }

                var update = await converter.ConvertAsync(notification, cancellationToken).ConfigureAwait(false);
                if (_options.OnResponseUpdateAsync is { } onResponseUpdate)
                {
                    await onResponseUpdate(update, cancellationToken).ConfigureAwait(false);
                }
                if (update.Contents.Count > 0)
                {
                    responseContents.AddRange(update.Contents);
                }
                await writer.WriteAsync(update, cancellationToken).ConfigureAwait(false);

                if (notification.Method == "turn/completed" &&
                    notification.Parameters is TurnCompletedNotification completed &&
                    string.Equals(completed.Turn.Id, activeTurnId, StringComparison.Ordinal))
                {
                    completedTurn = true;
                    activeTurnId = null;
                    if (completed.Turn.Status == TurnStatus.Failed)
                    {
                        throw new InvalidOperationException(
                            completed.Turn.Error.IsDefined && completed.Turn.Error.Value is { } error
                                ? error.Message
                                : "The Codex app-server turn failed.");
                    }

                    break;
                }
            }

            if (!completedTurn)
            {
                throw new InvalidOperationException("The Codex app-server notification stream ended before the turn completed.");
            }

            var response = new ChatMessage(ChatRole.Assistant, responseContents);
            if (_options.ChatHistoryProvider is { } provider)
            {
                historyInvoked = true;
#pragma warning disable MAAI001
                await provider.InvokedAsync(
                    new ChatHistoryProvider.InvokedContext(this, session, requestMessages, [response]),
                    cancellationToken).ConfigureAwait(false);
#pragma warning restore MAAI001
            }
        }
        catch (Exception exception)
        {
            if (startingNewThread && !turnStartRequested)
            {
                session.ThreadId = null;
            }
            var failures = new List<Exception> { exception };
            if (activeTurnId is not null && session.ThreadId is { } threadId)
            {
                try
                {
                    await _client.TurnInterruptAsync(
                        new TurnInterruptParams { ThreadId = threadId, TurnId = activeTurnId },
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception interruptException)
                {
                    failures.Add(interruptException);
                }
            }

            if (_options.ChatHistoryProvider is { } provider && historyStarted && !historyInvoked)
            {
                try
                {
#pragma warning disable MAAI001
                    await provider.InvokedAsync(
                        new ChatHistoryProvider.InvokedContext(this, session, requestMessages, exception),
                        cancellationToken).ConfigureAwait(false);
#pragma warning restore MAAI001
                    historyInvoked = true;
                }
                catch (Exception historyException)
                {
                    failures.Add(historyException);
                }
            }

            producerFailure = failures.Count == 1 ? exception : new AggregateException(failures);
        }
        finally
        {
            writer.TryComplete(producerFailure);
        }
    }

    private async Task<string> EnsureThreadAsync(CodexAppServerAgentSession session, CancellationToken cancellationToken)
    {
        if (session.ThreadId is { Length: > 0 } existingThreadId)
        {
            var parameters = new ThreadResumeParams { ThreadId = existingThreadId };
            if (_options.ConfigureThreadResume is { } configureThreadResume)
            {
                parameters = configureThreadResume(parameters)
                    ?? throw new InvalidOperationException("The configured resume parameters were null.");
                parameters = parameters with { ThreadId = existingThreadId };
                if (parameters.Path.IsDefined && parameters.Path.Value is not null ||
                    parameters.History.IsDefined && parameters.History.Value is not null)
                {
                    throw new InvalidOperationException("Resume configuration cannot replace the session path or history.");
                }
            }
            var resumed = await _client.ThreadResumeAsync(parameters, cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(resumed.Thread.Id, existingThreadId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The resumed thread does not match the session thread ID.");
            }
            return existingThreadId;
        }

        var startedThread = await _client.ThreadStartAsync(_options.ThreadStartParams, cancellationToken)
            .ConfigureAwait(false);
        session.ThreadId = startedThread.Thread.Id;
        return startedThread.Thread.Id;
    }

    private static CodexAppServerAgentSession RequireSession(AgentSession session) =>
        session as CodexAppServerAgentSession
        ?? throw new ArgumentException(
            $"The session must be a {nameof(CodexAppServerAgentSession)} created by this agent.",
            nameof(session));
}
