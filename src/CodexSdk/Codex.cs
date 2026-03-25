using OpenAI.Codex;

namespace OpenAI.CodexSdk;

/// <summary>
/// Main entry point for interacting with the Codex agent.
/// Use <see cref="StartThread"/> to begin a new conversation or
/// <see cref="ResumeThread"/> to continue a previously saved one.
/// </summary>
public sealed class Codex
{
    private readonly CodexExec _exec;
    private readonly CodexOptions _options;

    /// <summary>
    /// Initialises a new <see cref="Codex"/> client with the given options.
    /// </summary>
    /// <param name="options">
    /// Global configuration such as the API key, base URL, and CLI path override.
    /// All fields are optional.
    /// </param>
    public Codex(CodexOptions? options = null)
    {
        _options = options ?? new CodexOptions();
        _exec = new CodexExec(
            _options.CodexPathOverride,
            _options.Env,
            _options.Config);
    }

    /// <summary>
    /// Starts a new conversation with the agent.
    /// </summary>
    /// <param name="options">Per-thread configuration overrides.</param>
    /// <returns>A new <see cref="Thread"/> instance.</returns>
    public Thread StartThread(ThreadOptions? options = null) =>
        new(_exec, _options, options ?? new ThreadOptions());

    /// <summary>
    /// Resumes a conversation with the agent based on a previously recorded thread ID.
    /// Threads are persisted in <c>~/.codex/sessions</c>.
    /// </summary>
    /// <param name="id">The ID of the thread to resume.</param>
    /// <param name="options">Per-thread configuration overrides.</param>
    /// <returns>A new <see cref="Thread"/> instance bound to the existing session.</returns>
    public Thread ResumeThread(string id, ThreadOptions? options = null) =>
        new(_exec, _options, options ?? new ThreadOptions(), id);
}
