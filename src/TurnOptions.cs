namespace OpenAI.Codex;

/// <summary>Options for a single turn (prompt) within a thread.</summary>
public sealed class TurnOptions
{
    /// <summary>
    /// JSON schema object describing the expected structure of the agent's response.
    /// When provided, the agent is asked to return output conforming to the schema.
    /// Must be a valid JSON Schema object (a dictionary of string keys to arbitrary JSON values).
    /// </summary>
    public IReadOnlyDictionary<string, object?>? OutputSchema { get; init; }

    /// <summary>
    /// Token used to cancel the turn.
    /// Equivalent to <c>AbortSignal</c> in the TypeScript SDK.
    /// </summary>
    public CancellationToken CancellationToken { get; init; }
}
