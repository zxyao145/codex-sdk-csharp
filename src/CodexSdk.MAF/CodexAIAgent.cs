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
        var threadId = (_options.ThreadId ?? Guid.NewGuid()).ToString();

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
            throw new InvalidOperationException($"Expected {nameof(CodexAgentSession)} but got {session.GetType().Name}.");
        }

        return ValueTask.FromResult(JsonSerializer.SerializeToElement(codexSession, jsonSerializerOptions));
    }

    protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
        JsonElement serializedState,
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default)
    {
        var session = JsonSerializer.Deserialize<CodexAgentSession>(serializedState, jsonSerializerOptions)
            ?? throw new ArgumentException("Unable to deserialize Codex session state.", nameof(serializedState));

        return ValueTask.FromResult<AgentSession>(session);
    }

    protected override async Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var (safeSession, mergedMessages) = await PrepareSessionAndMessagesAsync(session, messages, cancellationToken);
        var input = CombineUserText(mergedMessages);
        var thread = GetThread(safeSession);

        var turn = await thread.RunAsync(input, cancellationToken: cancellationToken);
        safeSession.ThreadId = thread.Id;

        var responseMessages = new List<ChatMessage>
        {
            new(ChatRole.Assistant, turn.FinalResponse)
            {
                AuthorName = "codex"
            }
        };

        await SaveNewMessagesAsync(safeSession, mergedMessages, responseMessages, cancellationToken);

        return new AgentResponse
        {
            ResponseId = Guid.NewGuid().ToString(),
            Messages = responseMessages,
            Usage = turn.Usage is null
                ? null
                : new UsageDetails
                {
                    InputTokenCount = turn.Usage.InputTokens,
                    CachedInputTokenCount = turn.Usage.CachedInputTokens,
                    OutputTokenCount = turn.Usage.OutputTokens,
                },
        };
    }

    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var (safeSession, mergedMessages) = await PrepareSessionAndMessagesAsync(session, messages, cancellationToken);
        var input = CombineUserText(mergedMessages);
        var thread = GetThread(safeSession);
        var responseMessages = new List<ChatMessage>();

        await foreach (var threadEvent in thread.RunStreamedAsync(input, cancellationToken: cancellationToken))
        {
            var update = threadEvent.ToAgentResponseUpdate();
            if (update is null)
            {
                continue;
            }


            if (update.Role == ChatRole.Assistant)
            {
                responseMessages.Add(update.ToChatMessage());
            }

            yield return update;
        }

        safeSession.ThreadId = thread.Id;
        await SaveNewMessagesAsync(safeSession, mergedMessages, responseMessages, cancellationToken);
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

    private static string CombineUserText(IEnumerable<ChatMessage> messages)
    {
        return string.Join("\n\n", messages
            .Where(static m => m.Role == ChatRole.User)
            .Select(static m => m.Text)
            .Where(static text => !string.IsNullOrWhiteSpace(text)));
    }

    private async ValueTask<(CodexAgentSession Session, IEnumerable<ChatMessage> Messages)> PrepareSessionAndMessagesAsync(
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
            throw new InvalidOperationException($"Expected {nameof(CodexAgentSession)} but got {session.GetType().Name}.");
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
