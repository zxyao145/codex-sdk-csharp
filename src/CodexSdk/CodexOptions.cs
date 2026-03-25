namespace OpenAI.CodexSdk;

/// <summary>
/// A value that can appear in a Codex config object.
/// Supported types: string, double, bool, List&lt;CodexConfigValue&gt;, Dictionary&lt;string, CodexConfigValue&gt;.
/// </summary>
public abstract class CodexConfigValue
{
    private CodexConfigValue() { }

    public sealed class StringValue : CodexConfigValue
    {
        public string Value { get; }
        public StringValue(string value) => Value = value;
    }

    public sealed class NumberValue : CodexConfigValue
    {
        public double Value { get; }
        public NumberValue(double value) => Value = value;
    }

    public sealed class BoolValue : CodexConfigValue
    {
        public bool Value { get; }
        public BoolValue(bool value) => Value = value;
    }

    public sealed class ArrayValue : CodexConfigValue
    {
        public IReadOnlyList<CodexConfigValue> Items { get; }
        public ArrayValue(IReadOnlyList<CodexConfigValue> items) => Items = items;
    }

    public sealed class ObjectValue : CodexConfigValue
    {
        public IReadOnlyDictionary<string, CodexConfigValue> Properties { get; }
        public ObjectValue(IReadOnlyDictionary<string, CodexConfigValue> properties) => Properties = properties;
    }

    // Implicit conversions for ergonomic usage
    public static implicit operator CodexConfigValue(string value) => new StringValue(value);
    public static implicit operator CodexConfigValue(double value) => new NumberValue(value);
    public static implicit operator CodexConfigValue(int value) => new NumberValue(value);
    public static implicit operator CodexConfigValue(bool value) => new BoolValue(value);
}

/// <summary>
/// Options for the <see cref="Codex"/> client.
/// </summary>
public sealed class CodexOptions
{
    /// <summary>
    /// Override the path to the Codex CLI executable.
    /// When null, the SDK resolves the bundled platform binary automatically.
    /// </summary>
    public string? CodexPathOverride { get; init; }

    /// <summary>Base URL forwarded to the Codex CLI as <c>openai_base_url</c>.</summary>
    public string? BaseUrl { get; init; }

    /// <summary>API key forwarded to the CLI via the <c>CODEX_API_KEY</c> environment variable.</summary>
    public string? ApiKey { get; init; }

    /// <summary>
    /// Additional <c>--config key=value</c> overrides to pass to the Codex CLI.
    /// The SDK flattens the object into dotted paths and serializes values as TOML literals.
    /// </summary>
    public IReadOnlyDictionary<string, CodexConfigValue>? Config { get; init; }

    /// <summary>
    /// Environment variables passed to the Codex CLI process.
    /// When provided, the SDK will not inherit variables from the current process environment.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Env { get; init; }
}
