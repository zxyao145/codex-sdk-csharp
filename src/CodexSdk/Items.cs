// Based on item types from codex-rs/exec/src/exec_events.rs

using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenAI.CodexSdk;

/// <summary>
/// <see cref="JsonStringEnumConverter{T}"/> variant that converts enum member names
/// using <see cref="JsonNamingPolicy.SnakeCaseLower"/> (e.g. <c>InProgress</c> → <c>"in_progress"</c>).
/// </summary>
internal sealed class SnakeCaseEnumConverter<T> : JsonStringEnumConverter<T>
    where T : struct, Enum
{
    public SnakeCaseEnumConverter() : base(JsonNamingPolicy.SnakeCaseLower) { }
}

/// <summary>Canonical union of thread items produced by the agent.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(AgentMessageItem), "agent_message")]
[JsonDerivedType(typeof(ReasoningItem), "reasoning")]
[JsonDerivedType(typeof(CommandExecutionItem), "command_execution")]
[JsonDerivedType(typeof(FileChangeItem), "file_change")]
[JsonDerivedType(typeof(McpToolCallItem), "mcp_tool_call")]
[JsonDerivedType(typeof(WebSearchItem), "web_search")]
[JsonDerivedType(typeof(TodoListItem), "todo_list")]
[JsonDerivedType(typeof(ErrorItem), "error")]
public abstract class ThreadItem
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;
}

/// <summary>Response from the agent. Either natural-language text or JSON when structured output is requested.</summary>
public sealed class AgentMessageItem : ThreadItem
{
    /// <summary>Either natural-language text or JSON when structured output is requested.</summary>
    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;
}

/// <summary>Agent's reasoning summary.</summary>
public sealed class ReasoningItem : ThreadItem
{
    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;
}

/// <summary>The status of a command execution.</summary>
public enum CommandExecutionStatus { InProgress, Completed, Failed }

/// <summary>A command executed by the agent.</summary>
public sealed class CommandExecutionItem : ThreadItem
{
    /// <summary>The command line executed by the agent.</summary>
    [JsonPropertyName("command")]
    public string Command { get; init; } = string.Empty;

    /// <summary>Aggregated stdout and stderr captured while the command was running.</summary>
    [JsonPropertyName("aggregated_output")]
    public string AggregatedOutput { get; init; } = string.Empty;

    /// <summary>Exit code — set when the command exits; null while still running.</summary>
    [JsonPropertyName("exit_code")]
    public int? ExitCode { get; init; }

    /// <summary>Current status of the command execution.</summary>
    [JsonPropertyName("status")]
    [JsonConverter(typeof(SnakeCaseEnumConverter<CommandExecutionStatus>))]
    public CommandExecutionStatus Status { get; init; }
}

/// <summary>Indicates the type of a file change.</summary>
public enum PatchChangeKind { Add, Delete, Update }

/// <summary>A single file change within a patch.</summary>
public sealed class FileUpdateChange
{
    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;

    [JsonPropertyName("kind")]
    [JsonConverter(typeof(SnakeCaseEnumConverter<PatchChangeKind>))]
    public PatchChangeKind Kind { get; init; }
}

/// <summary>Whether a patch applied successfully.</summary>
public enum PatchApplyStatus { Completed, Failed }

/// <summary>A set of file changes by the agent. Emitted once the patch succeeds or fails.</summary>
public sealed class FileChangeItem : ThreadItem
{
    /// <summary>Individual file changes that comprise the patch.</summary>
    [JsonPropertyName("changes")]
    public IReadOnlyList<FileUpdateChange> Changes { get; init; } = [];

    /// <summary>Whether the patch ultimately succeeded or failed.</summary>
    [JsonPropertyName("status")]
    [JsonConverter(typeof(SnakeCaseEnumConverter<PatchApplyStatus>))]
    public PatchApplyStatus Status { get; init; }
}

/// <summary>The status of an MCP tool call.</summary>
public enum McpToolCallStatus { InProgress, Completed, Failed }

/// <summary>Content block returned from an MCP server.</summary>
public sealed class McpContentBlock
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>Result payload returned by the MCP server for successful calls.</summary>
public sealed class McpToolCallResult
{
    [JsonPropertyName("content")]
    public IReadOnlyList<McpContentBlock> Content { get; init; } = [];

    [JsonPropertyName("structured_content")]
    public JsonElement? StructuredContent { get; init; }
}

/// <summary>Error message reported for failed MCP calls.</summary>
public sealed class McpToolCallError
{
    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Represents a call to an MCP tool. The item starts when the invocation is dispatched
/// and completes when the MCP server reports success or failure.
/// </summary>
public sealed class McpToolCallItem : ThreadItem
{
    /// <summary>Name of the MCP server handling the request.</summary>
    [JsonPropertyName("server")]
    public string Server { get; init; } = string.Empty;

    /// <summary>The tool invoked on the MCP server.</summary>
    [JsonPropertyName("tool")]
    public string Tool { get; init; } = string.Empty;

    /// <summary>Arguments forwarded to the tool invocation.</summary>
    [JsonPropertyName("arguments")]
    public JsonElement Arguments { get; init; }

    /// <summary>Result payload returned by the MCP server for successful calls.</summary>
    [JsonPropertyName("result")]
    public McpToolCallResult? Result { get; init; }

    /// <summary>Error message reported for failed calls.</summary>
    [JsonPropertyName("error")]
    public McpToolCallError? Error { get; init; }

    /// <summary>Current status of the tool invocation.</summary>
    [JsonPropertyName("status")]
    [JsonConverter(typeof(SnakeCaseEnumConverter<McpToolCallStatus>))]
    public McpToolCallStatus Status { get; init; }
}

/// <summary>Captures a web search request. Completes when results are returned to the agent.</summary>
public sealed class WebSearchItem : ThreadItem
{
    [JsonPropertyName("query")]
    public string Query { get; init; } = string.Empty;
}

/// <summary>Describes a non-fatal error surfaced as an item.</summary>
public sealed class ErrorItem : ThreadItem
{
    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;
}

/// <summary>An item in the agent's to-do list.</summary>
public sealed class TodoItem
{
    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;

    [JsonPropertyName("completed")]
    public bool Completed { get; init; }
}

/// <summary>
/// Tracks the agent's running to-do list. Starts when the plan is issued, updates as steps
/// change, and completes when the turn ends.
/// </summary>
public sealed class TodoListItem : ThreadItem
{
    [JsonPropertyName("items")]
    public IReadOnlyList<TodoItem> Items { get; init; } = [];
}
