// Based on event types from codex-rs/exec/src/exec_events.rs

using System.Text.Json.Serialization;

namespace OpenAI.Codex;

/// <summary>Top-level JSONL events emitted by <c>codex exec</c>.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ThreadStartedEvent), "thread.started")]
[JsonDerivedType(typeof(TurnStartedEvent), "turn.started")]
[JsonDerivedType(typeof(TurnCompletedEvent), "turn.completed")]
[JsonDerivedType(typeof(TurnFailedEvent), "turn.failed")]
[JsonDerivedType(typeof(ItemStartedEvent), "item.started")]
[JsonDerivedType(typeof(ItemUpdatedEvent), "item.updated")]
[JsonDerivedType(typeof(ItemCompletedEvent), "item.completed")]
[JsonDerivedType(typeof(ThreadErrorEvent), "error")]
public abstract class ThreadEvent { }

/// <summary>Emitted when a new thread is started, as the first event in the stream.</summary>
public sealed class ThreadStartedEvent : ThreadEvent
{
    /// <summary>The identifier of the new thread. Can be used to resume the thread later.</summary>
    [JsonPropertyName("thread_id")]
    public string ThreadId { get; init; } = string.Empty;
}

/// <summary>
/// Emitted when a turn is started by sending a new prompt to the model.
/// A turn encompasses all events that happen while the agent is processing the prompt.
/// </summary>
public sealed class TurnStartedEvent : ThreadEvent { }

/// <summary>Describes the usage of tokens during a turn.</summary>
public sealed class Usage
{
    /// <summary>The number of input tokens used during the turn.</summary>
    [JsonPropertyName("input_tokens")]
    public int InputTokens { get; init; }

    /// <summary>The number of cached input tokens used during the turn.</summary>
    [JsonPropertyName("cached_input_tokens")]
    public int CachedInputTokens { get; init; }

    /// <summary>The number of output tokens used during the turn.</summary>
    [JsonPropertyName("output_tokens")]
    public int OutputTokens { get; init; }
}

/// <summary>Emitted when a turn is completed — typically right after the assistant's response.</summary>
public sealed class TurnCompletedEvent : ThreadEvent
{
    [JsonPropertyName("usage")]
    public Usage Usage { get; init; } = new();
}

/// <summary>Fatal error associated with a turn or the stream itself.</summary>
public sealed class ThreadError
{
    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;
}

/// <summary>Indicates that a turn failed with an error.</summary>
public sealed class TurnFailedEvent : ThreadEvent
{
    [JsonPropertyName("error")]
    public ThreadError Error { get; init; } = new();
}

/// <summary>Emitted when a new item is added to the thread. The item is initially in progress.</summary>
public sealed class ItemStartedEvent : ThreadEvent
{
    [JsonPropertyName("item")]
    public ThreadItem Item { get; init; } = null!;
}

/// <summary>Emitted when an item is updated.</summary>
public sealed class ItemUpdatedEvent : ThreadEvent
{
    [JsonPropertyName("item")]
    public ThreadItem Item { get; init; } = null!;
}

/// <summary>Signals that an item has reached a terminal state — either success or failure.</summary>
public sealed class ItemCompletedEvent : ThreadEvent
{
    [JsonPropertyName("item")]
    public ThreadItem Item { get; init; } = null!;
}

/// <summary>Represents an unrecoverable error emitted directly by the event stream.</summary>
public sealed class ThreadErrorEvent : ThreadEvent
{
    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;
}
