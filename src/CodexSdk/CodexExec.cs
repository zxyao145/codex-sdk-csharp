using OpenAI.CodexSdk;
using OpenAI.CodexSdk.Utils;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenAI.Codex;

/// <summary>Arguments forwarded to a single <c>codex exec</c> invocation.</summary>
internal sealed class CodexExecArgs
{
    public required string Input { get; init; }

    public string? BaseUrl { get; init; }
    public string? ApiKey { get; init; }
    public string? ThreadId { get; init; }
    public IReadOnlyList<string>? Images { get; init; }

    // --model
    public string? Model { get; init; }
    // --sandbox
    public SandboxMode? SandboxMode { get; init; }
    // --cd
    public string? WorkingDirectory { get; init; }
    // --add-dir
    public IReadOnlyList<string>? AdditionalDirectories { get; init; }
    // --skip-git-repo-check
    public bool? SkipGitRepoCheck { get; init; }
    // --output-schema
    public string? OutputSchemaFile { get; init; }
    // --config model_reasoning_effort
    public ModelReasoningEffort? ModelReasoningEffort { get; init; }
    // --config sandbox_workspace_write.network_access
    public bool? NetworkAccessEnabled { get; init; }
    // --config web_search
    public WebSearchMode? WebSearchMode { get; init; }
    // legacy --config features.web_search_request
    public bool? WebSearchEnabled { get; init; }
    // --config approval_policy
    public ApprovalMode? ApprovalPolicy { get; init; }
    // cancellation
    public CancellationToken CancellationToken { get; init; }
}

/// <summary>
/// Spawns the Codex CLI as a subprocess, writes the prompt on stdin, and yields JSONL lines
/// from stdout as an async stream.
/// </summary>
internal sealed partial class CodexExec
{
    private const string InternalOriginatorEnv = "CODEX_INTERNAL_ORIGINATOR_OVERRIDE";
    private const string CsharpSdkOriginator = "codex_sdk_cs";

    private readonly string _executablePath;
    private readonly IReadOnlyDictionary<string, string>? _envOverride;
    private readonly IReadOnlyDictionary<string, CodexConfigValue>? _configOverrides;

    public CodexExec(
        string? executablePath = null,
        IReadOnlyDictionary<string, string>? env = null,
        IReadOnlyDictionary<string, CodexConfigValue>? configOverrides = null)
    {
        _executablePath = executablePath ?? FindCodexPath();
        _envOverride = env;
        _configOverrides = configOverrides;
    }

    /// <summary>
    /// Runs the Codex CLI and yields each raw JSONL line from stdout.
    /// Throws when the process exits with a non-zero exit code.
    /// </summary>
    public async IAsyncEnumerable<string> RunAsync(
        CodexExecArgs args,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            args.CancellationToken, cancellationToken);
        var token = linkedCts.Token;

        var commandArgs = BuildArgs(args);
        var env = BuildEnv(args);

        var fileName = CommandUtil.GetOptimallyQualifiedTargetFilePath(_executablePath);
        var psi = new ProcessStartInfo()
        {
            FileName = fileName,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardInputEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var arg in commandArgs)
        {
            psi.ArgumentList.Add(arg);
        }

        foreach (var (k, v) in env)
        {
            psi.Environment[k] = v;
        }

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        var stderrBuilder = new StringBuilder();
        var stderrTcs = new TaskCompletionSource<string>();

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                stderrBuilder.AppendLine(e.Data);
        };

        process.Start();
        process.BeginErrorReadLine();

        // Write prompt to stdin then close it so the CLI knows input is done.
        await process.StandardInput.WriteAsync(args.Input).ConfigureAwait(false);
        process.StandardInput.Close();

        // Register cancellation: kill the process if the token fires.
        await using var reg = token.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch { /* ignore */ }
        });

        // Yield lines from stdout.
        string? line;
        while ((line = await process.StandardOutput.ReadLineAsync(token).ConfigureAwait(false)) is not null)
        {
            yield return line;
        }

        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);

        token.ThrowIfCancellationRequested();

        if (process.ExitCode != 0)
        {
            var stderr = stderrBuilder.ToString();
            throw new InvalidOperationException(
                $"Codex Exec exited with code {process.ExitCode}: {stderr}");
        }
    }

    // -------------------------------------------------------------------------
    // Argument building
    // -------------------------------------------------------------------------

    private List<string> BuildArgs(CodexExecArgs args)
    {
        var list = new List<string> { "exec", "--experimental-json" };

        if (_configOverrides is not null)
        {
            foreach (var cfg in SerializeConfigOverrides(_configOverrides))
            {
                list.Add("--config");
                list.Add(cfg);
            }
        }

        if (args.BaseUrl is not null)
        {
            list.Add("--config");
            list.Add($"openai_base_url={ToTomlValue(new CodexConfigValue.StringValue(args.BaseUrl), "openai_base_url")}");
        }

        if (args.Model is not null)
        {
            list.Add("--model");
            list.Add(args.Model);
        }

        if (args.SandboxMode is not null)
        {
            list.Add("--sandbox");
            list.Add(SandboxModeToString(args.SandboxMode.Value));
        }

        if (args.WorkingDirectory is not null)
        {
            list.Add("--cd");
            list.Add(args.WorkingDirectory);
        }

        if (args.AdditionalDirectories?.Count > 0)
        {
            foreach (var dir in args.AdditionalDirectories)
            {
                list.Add("--add-dir");
                list.Add(dir);
            }
        }

        if (args.SkipGitRepoCheck == true)
        {
            list.Add("--skip-git-repo-check");
        }

        if (args.OutputSchemaFile is not null)
        {
            list.Add("--output-schema");
            list.Add(args.OutputSchemaFile);
        }

        if (args.ModelReasoningEffort is not null)
        {
            list.Add("--config");
            list.Add($"model_reasoning_effort=\"{ModelReasoningEffortToString(args.ModelReasoningEffort.Value)}\"");
        }

        if (args.NetworkAccessEnabled is not null)
        {
            list.Add("--config");
            list.Add($"sandbox_workspace_write.network_access={BoolToToml(args.NetworkAccessEnabled.Value)}");
        }

        if (args.WebSearchMode is not null)
        {
            list.Add("--config");
            list.Add($"web_search=\"{WebSearchModeToString(args.WebSearchMode.Value)}\"");
        }
        else if (args.WebSearchEnabled == true)
        {
            list.Add("--config");
            list.Add("web_search=\"live\"");
        }
        else if (args.WebSearchEnabled == false)
        {
            list.Add("--config");
            list.Add("web_search=\"disabled\"");
        }

        if (args.ApprovalPolicy is not null)
        {
            list.Add("--config");
            list.Add($"approval_policy=\"{ApprovalModeToString(args.ApprovalPolicy.Value)}\"");
        }

        if (args.ThreadId is not null)
        {
            list.Add("resume");
            list.Add(args.ThreadId);
        }

        if (args.Images?.Count > 0)
        {
            foreach (var image in args.Images)
            {
                list.Add("--image");
                list.Add(image);
            }
        }

        return list;
    }

    private Dictionary<string, string> BuildEnv(CodexExecArgs args)
    {
        var env = new Dictionary<string, string>(StringComparer.Ordinal);

        if (_envOverride is not null)
        {
            foreach (var (k, v) in _envOverride)
                env[k] = v;
        }
        else
        {
            foreach (System.Collections.DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
            {
                if (entry.Key is string k && entry.Value is string v)
                    env[k] = v;
            }
        }

        env.TryAdd(InternalOriginatorEnv, CsharpSdkOriginator);

        if (args.ApiKey is not null)
        {
            env["CODEX_API_KEY"] = args.ApiKey;
        }

        return env;
    }

    // -------------------------------------------------------------------------
    // Config serialization (TOML literals)
    // -------------------------------------------------------------------------

    private static IEnumerable<string> SerializeConfigOverrides(
        IReadOnlyDictionary<string, CodexConfigValue> overrides)
    {
        var result = new List<string>();
        FlattenConfigOverrides(overrides, prefix: "", result);
        return result;
    }

    private static void FlattenConfigOverrides(
        IReadOnlyDictionary<string, CodexConfigValue> obj,
        string prefix,
        List<string> result)
    {
        if (obj.Count == 0)
        {
            if (!string.IsNullOrEmpty(prefix))
                result.Add($"{prefix}={{}}");
            return;
        }

        foreach (var (key, value) in obj)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Codex config override keys must be non-empty strings.");

            var path = string.IsNullOrEmpty(prefix) ? key : $"{prefix}.{key}";

            if (value is CodexConfigValue.ObjectValue nested)
            {
                FlattenConfigOverrides(nested.Properties, path, result);
            }
            else
            {
                result.Add($"{path}={ToTomlValue(value, path)}");
            }
        }
    }

    private static string ToTomlValue(CodexConfigValue value, string path) => value switch
    {
        CodexConfigValue.StringValue s =>
            System.Text.Json.JsonSerializer.Serialize(s.Value),

        CodexConfigValue.NumberValue n when double.IsFinite(n.Value) =>
            n.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),

        CodexConfigValue.NumberValue n =>
            throw new ArgumentException($"Codex config override at {path} must be a finite number."),

        CodexConfigValue.BoolValue b =>
            b.Value ? "true" : "false",

        CodexConfigValue.ArrayValue a =>
            $"[{string.Join(", ", a.Items.Select((item, i) => ToTomlValue(item, $"{path}[{i}]")))}]",

        CodexConfigValue.ObjectValue o =>
            $"{{{string.Join(", ", o.Properties.Select(kvp =>
                $"{FormatTomlKey(kvp.Key)} = {ToTomlValue(kvp.Value, $"{path}.{kvp.Key}")}"))}}}",

        _ => throw new ArgumentException($"Unsupported config value type at {path}: {value.GetType().Name}")
    };

    [GeneratedRegex(@"^[A-Za-z0-9_-]+$")]
    private static partial Regex TomlBareKeyRegex();

    private static string FormatTomlKey(string key) =>
        TomlBareKeyRegex().IsMatch(key) ? key : System.Text.Json.JsonSerializer.Serialize(key);

    // -------------------------------------------------------------------------
    // Enum helpers — match the CLI's expected string values
    // -------------------------------------------------------------------------

    private static string SandboxModeToString(SandboxMode m) => m switch
    {
        SandboxMode.ReadOnly => "read-only",
        SandboxMode.WorkspaceWrite => "workspace-write",
        SandboxMode.DangerFullAccess => "danger-full-access",
        _ => throw new ArgumentOutOfRangeException(nameof(m), m, null)
    };

    private static string ModelReasoningEffortToString(ModelReasoningEffort e) => e switch
    {
        ModelReasoningEffort.Minimal => "minimal",
        ModelReasoningEffort.Low => "low",
        ModelReasoningEffort.Medium => "medium",
        ModelReasoningEffort.High => "high",
        ModelReasoningEffort.XHigh => "xhigh",
        _ => throw new ArgumentOutOfRangeException(nameof(e), e, null)
    };

    private static string WebSearchModeToString(WebSearchMode m) => m switch
    {
        WebSearchMode.Disabled => "disabled",
        WebSearchMode.Cached => "cached",
        WebSearchMode.Live => "live",
        _ => throw new ArgumentOutOfRangeException(nameof(m), m, null)
    };

    private static string ApprovalModeToString(ApprovalMode m) => m switch
    {
        ApprovalMode.Never => "never",
        ApprovalMode.OnRequest => "on-request",
        ApprovalMode.OnFailure => "on-failure",
        ApprovalMode.Untrusted => "untrusted",
        _ => throw new ArgumentOutOfRangeException(nameof(m), m, null)
    };

    private static string BoolToToml(bool value) => value ? "true" : "false";

    // -------------------------------------------------------------------------
    // Binary resolution
    // -------------------------------------------------------------------------

    private static string FindCodexPath()
    {
        var targetTriple = GetTargetTriple();
        var binaryName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "codex.exe" : "codex";

        //// Walk well-known roots looking for the vendor/<triple>/codex/<binary> layout
        //// that matches the @openai/codex npm optional-dependency packages.
        //var assemblyDir = Path.GetDirectoryName(typeof(CodexExec).Assembly.Location)
        //    ?? AppContext.BaseDirectory;

        //var searchRoots = new[]
        //{
        //    assemblyDir,
        //    AppContext.BaseDirectory,
        //    Directory.GetCurrentDirectory(),
        //};

        //foreach (var root in searchRoots)
        //{
        //    var candidate = Path.Combine(root, "vendor", targetTriple, "codex", binaryName);
        //    if (File.Exists(candidate))
        //        return candidate;
        //}

        var cli = FindCli("codex");
        if (!string.IsNullOrWhiteSpace(cli))
        {
            return cli;
        }

        throw new InvalidOperationException(
            $"Unable to locate the Codex CLI binary for {targetTriple}. " +
            "Set CodexOptions.CodexPathOverride or place the binary at " +
            $"vendor/{targetTriple}/codex/{binaryName} relative to the application.");
    }


    private static string FindCli(string command)
    {
        // Try PATH first
        var cli = Which(command);
        if (cli != null)
            return cli;

        // Try common installation locations
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var isWindows = OperatingSystem.IsWindows();

        var locations = new[]
        {
            command,
            Path.Combine(home, ".npm-global", "bin", command),
            Path.Combine("/usr/local/bin", command),
            Path.Combine(home, ".local", "bin", command),
            Path.Combine(home, "node_modules", ".bin", command),
            Path.Combine(home, ".yarn", "bin", command),
        };

        foreach (var path in locations)
        {
            if (File.Exists(path))
                return path;

            if (isWindows && File.Exists(path + ".exe"))
                return path + ".exe";
        }

        // Check if Node.js is installed
        if (Which("node") == null)
        {
            throw new Exception(
                "codex requires Node.js, which is not installed.\n\n" +
                "Install Node.js from: https://nodejs.org/\n\n" +
                "After installing Node.js, install codex:\n" +
                "  npm install -g @openai/codex");
        }

        // CLI not found
        throw new Exception(
            "codex not found. Install with:\n" +
            "  npm install -g @openai/codex\n\n" +
            "If already installed locally, try:\n" +
            "  export PATH=\"$HOME/node_modules/.bin:$PATH\"");
    }

    private static string? Which(string command)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        var paths = pathEnv.Split(Path.PathSeparator);
        var isWindows = OperatingSystem.IsWindows();

        foreach (var path in paths)
        {
            var fullPath = Path.Combine(path, command);
            if (File.Exists(fullPath))
                return fullPath;

            if (isWindows)
            {
                var fullExe = fullPath + ".exe";
                if (File.Exists(fullExe))
                    return fullExe;
            }
        }
        return null;
    }


    private static string GetTargetTriple()
    {
        var isArm64 = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return isArm64 ? "aarch64-pc-windows-msvc" : "x86_64-pc-windows-msvc";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return isArm64 ? "aarch64-apple-darwin" : "x86_64-apple-darwin";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return isArm64 ? "aarch64-unknown-linux-musl" : "x86_64-unknown-linux-musl";

        throw new PlatformNotSupportedException(
            $"Unsupported OS: {RuntimeInformation.OSDescription}");
    }
}
