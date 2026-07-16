using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI.CodexSdk.MAF.Internal;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace OpenAI.CodexSdk.MAF;

/// <summary>
/// AIAgent bridge backed by <see cref="Codex"/> threads.
/// </summary>
public sealed class CodexAIAgent : AIAgent
{
    private readonly CodexAIAgentOptions _options;
    private readonly Codex _codex;
    private readonly ILogger? _logger;

    public CodexAIAgent()
        : this(new CodexAIAgentOptions())
    {
    }

    public CodexAIAgent(CodexAIAgentOptions? options, ILogger? logger = null)
    {
        _options = options ?? new CodexAIAgentOptions();
        _codex = new Codex(_options.CodexOptions, logger);
        ChatHistoryProvider = _options.ChatHistoryProvider;
        _logger = logger;
    }

    public ChatHistoryProvider? ChatHistoryProvider { get; }

    protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default)
    {
        var threadId = _options.IsResume ? _options.ThreadId?.ToString() : null;

        return ValueTask.FromResult<AgentSession>(new CodexAgentSession(threadId));
    }

    protected override ValueTask<JsonElement> SerializeSessionCoreAsync(
        AgentSession session,
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session is not CodexAgentSession codexSession)
        {
            throw new InvalidOperationException(
                $"Expected {nameof(CodexAgentSession)} but got {session.GetType().Name}.");
        }

        return ValueTask.FromResult(JsonSerializer.SerializeToElement(codexSession, jsonSerializerOptions));
    }

    protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
        JsonElement serializedState,
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default)
    {
        var session = JsonSerializer.Deserialize<CodexAgentSession>(serializedState, jsonSerializerOptions)
                      ?? throw new ArgumentException("Unable to deserialize Codex session state.",
                          nameof(serializedState));

        return ValueTask.FromResult<AgentSession>(session);
    }

    protected override async Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var inputMessages = messages as ChatMessage[] ?? messages.ToArray();
        var (safeSession, mergedMessages) = await PrepareSessionAndMessagesAsync(session, inputMessages, cancellationToken);
        var input = CombineUserText(inputMessages);
        var thread = GetThread(safeSession);
        var notifiedThreadStarted = false;
        var responseMessages = new List<ChatMessage>();
        var historyMessages = new List<ChatMessage>();
        UsageDetails? usage = null;
        var failed = false;

        try
        {
            await foreach (var threadEvent in thread.RunStreamedAsync(input, cancellationToken: cancellationToken))
            {
                if (threadEvent is ThreadStartedEvent started)
                {
                    notifiedThreadStarted = await NotifyThreadStartedIfNeededAsync(
                        safeSession,
                        started.ThreadId,
                        notifiedThreadStarted,
                        cancellationToken);
                }

                switch (threadEvent)
                {
                    case TurnCompletedEvent turnCompleted:
                        usage = CreateUsageDetails(turnCompleted.Usage);
                        break;

                    case TurnFailedEvent turnFailed:
                        failed = true;
                        break;

                    case ThreadErrorEvent:
                        failed = true;
                        break;
                }

                var update = threadEvent.ToAgentResponseUpdate();
                if (update?.ShouldReturnAsResponseMessage() == true)
                {
                    responseMessages.Add(update.ToChatMessage());
                }

                if (update?.ShouldSaveAsResponseMessage() == true)
                {
                    historyMessages.Add(update.ToChatMessage());
                }

                if (failed)
                {
                    break;
                }
            }

            await SaveNewMessagesAsync(safeSession, mergedMessages, historyMessages, cancellationToken);

            return new AgentResponse
            {
                ResponseId = Guid.NewGuid().ToString(), Messages = responseMessages, Usage = usage,
            };
        }
        finally
        {
            await NotifyThreadStartedIfNeededAsync(
                safeSession,
                thread.Id,
                notifiedThreadStarted,
                CancellationToken.None);
        }
    }

    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var inputMessages = messages as ChatMessage[] ?? messages.ToArray();
        var (safeSession, mergedMessages) = await PrepareSessionAndMessagesAsync(session, inputMessages, cancellationToken);
        var input = CombineUserText(inputMessages);
        var thread = GetThread(safeSession);
        var responseMessages = new List<ChatMessage>();
        var notifiedThreadStarted = false;

        try
        {
            await foreach (var threadEvent in thread.RunStreamedAsync(input, cancellationToken: cancellationToken))
            {
                if (threadEvent is ThreadStartedEvent started)
                {
                    notifiedThreadStarted = await NotifyThreadStartedIfNeededAsync(
                        safeSession,
                        started.ThreadId,
                        notifiedThreadStarted,
                        cancellationToken);
                }

                var update = threadEvent.ToAgentResponseUpdate();
                if (update is null)
                {
                    continue;
                }

                if (update.ShouldSaveAsResponseMessage())
                {
                    responseMessages.Add(update.ToChatMessage());
                }

                yield return update;
            }
        }
        finally
        {
            await NotifyThreadStartedIfNeededAsync(
                safeSession,
                thread.Id,
                notifiedThreadStarted,
                CancellationToken.None);
        }

        await SaveNewMessagesAsync(safeSession, mergedMessages, responseMessages, cancellationToken);
    }

    private static UsageDetails CreateUsageDetails(Usage usage)
    {
        return new UsageDetails
        {
            InputTokenCount = usage.InputTokens,
            CachedInputTokenCount = usage.CachedInputTokens,
            OutputTokenCount = usage.OutputTokens,
        };
    }

    private async ValueTask<bool> NotifyThreadStartedIfNeededAsync(
        CodexAgentSession session,
        string? threadId,
        bool alreadyNotified,
        CancellationToken cancellationToken)
    {
        if (alreadyNotified || string.IsNullOrWhiteSpace(threadId))
        {
            return alreadyNotified;
        }

        await CodexThreadStartedNotifier.NotifyAsync(
            _options,
            session,
            threadId,
            cancellationToken).ConfigureAwait(false);
        return true;
    }

    private Thread GetThread(CodexAgentSession session)
    {
        var sessionId = string.IsNullOrWhiteSpace(session.ThreadId)
            ? Guid.NewGuid().ToString()
            : session.ThreadId;

        if (_options.IsResume)
        {
            return _codex.ResumeThread(sessionId, _options.ThreadOptions);
        }

        return _codex.StartThread(_options.ThreadOptions, sessionId);
    }

    private static Input CombineUserText(IEnumerable<ChatMessage> messages)
    {
        var parts = new List<UserInput>();
        var contents = messages
            .Where(static m => m.Role == ChatRole.User)
            .ToList();
        foreach (var msg in contents)
        {
            if (string.IsNullOrWhiteSpace(msg.Text))
            {
                continue;
            }
            
            UserInput userInput = new TextInput(msg.Text);
            parts.Add(userInput);
        }
        var input = Input.FromParts(parts);
        return input;
        // string.Join("\n\n", messages
        //     .Where(static m => m.Role == ChatRole.User)
        //     .Select(static m => m.Text)
        //     .Where(static text => !string.IsNullOrWhiteSpace(text)));
    }

    private async ValueTask<(CodexAgentSession Session, IEnumerable<ChatMessage> Messages)>
        PrepareSessionAndMessagesAsync(
            AgentSession? session,
            IEnumerable<ChatMessage> inputMessages,
            CancellationToken cancellationToken)
    {
        IEnumerable<ChatMessage> messages = inputMessages;
        if (ChatHistoryProvider is not null)
        {
#pragma warning disable MAAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
            var invoking = new ChatHistoryProvider.InvokingContext(this, session, inputMessages);
#pragma warning restore MAAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
            messages = await ChatHistoryProvider.InvokingAsync(invoking, cancellationToken);
        }

        session ??= await CreateSessionAsync(cancellationToken);

        if (session is not CodexAgentSession codexSession)
        {
            throw new InvalidOperationException(
                $"Expected {nameof(CodexAgentSession)} but got {session.GetType().Name}.");
        }

        return (codexSession, messages);
    }

    private async ValueTask SaveNewMessagesAsync(
        CodexAgentSession session,
        IEnumerable<ChatMessage> requestMessages,
        IEnumerable<ChatMessage> responseMessages,
        CancellationToken cancellationToken)
    {
        if (ChatHistoryProvider is null)
        {
            return;
        }

#pragma warning disable MAAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
        var invoked = new ChatHistoryProvider.InvokedContext(this, session, requestMessages, responseMessages);
#pragma warning restore MAAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
        await ChatHistoryProvider.InvokedAsync(invoked, cancellationToken);
    }
}
