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
    /// 指定 Codex CLI 命令或路径；省略时，在执行期间搜索 PATH 和 Unix 登录 shell。
    /// Override the Codex CLI command or path; when omitted, execution searches PATH and the Unix login shell.
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
    /// 用于命令查找和 Codex CLI 进程的完整环境；null 表示继承当前进程环境。
    /// Complete environment for command lookup and the Codex CLI; null inherits the current process environment.
    /// 提供该集合时，SDK 使用它替换宿主环境，并保持调用方集合原值。
    /// A supplied collection replaces the host environment and remains unchanged by the SDK.
    /// Unix login shell 查找命中时，子进程采用该 shell 的 PATH，相对目录转换为绝对路径。
    /// When Unix login-shell discovery succeeds, the child uses its PATH with relative entries made absolute.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Env { get; init; }
}
