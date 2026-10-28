using OpenAI.CodexSdk.AppServer.Protocol.V1;
using OpenAI.CodexSdk.AppServer.Transport;
using OpenAI.CodexSdk.Utils;
using StreamJsonRpc;
using StreamJsonRpc.Protocol;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace OpenAI.CodexSdk.AppServer;

public sealed partial class CodexAppServerClient : IAsyncDisposable
{
    private readonly JsonRpc _rpc;
    private readonly Process? _process;
    private readonly Task _standardErrorDrain;
    private readonly Task _completionMonitor;
    private readonly AppServerNotificationSubscriptions _notificationSubscriptions = new();
    private readonly ConcurrentDictionary<string, object> _requestHandlers = new(StringComparer.Ordinal);
    private readonly List<Delegate> _notificationHandlers = [];
    private int _disposed;

    private CodexAppServerClient(JsonRpc rpc, Process? process, Task standardErrorDrain)
    {
        _rpc = rpc;
        _process = process;
        _standardErrorDrain = standardErrorDrain;
        _completionMonitor = MonitorConnectionAsync();
    }

    /// <summary>
    /// 启动 Codex CLI app-server，并完成 JSON-RPC 握手。Starts the Codex CLI app-server and completes the JSON-RPC handshake.
    /// </summary>
    public static async Task<CodexAppServerClient> StartAsync(
        CodexAppServerClientOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new();
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ExecutablePath);
        ArgumentNullException.ThrowIfNull(options.Arguments);

        using var startupTimeout = CreateStartupTimeout(options, cancellationToken);
        var startupToken = startupTimeout?.Token ?? cancellationToken;
        Process? process = null;
        var processStarted = false;
        var standardErrorDrain = Task.CompletedTask;
        try
        {
            var command = await CommandUtil.FindAsync(options.ExecutablePath, cancellationToken: startupToken)
                .ConfigureAwait(false);
            if (command.Path is null)
                throw new FileNotFoundException($"Could not locate the Codex executable '{options.ExecutablePath}'.");

            var startInfo = new ProcessStartInfo(command.Path)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            if (options.WorkingDirectory is not null)
                startInfo.WorkingDirectory = options.WorkingDirectory;
            if (command.SearchPath is not null)
                startInfo.Environment["PATH"] = command.SearchPath;
            startInfo.ArgumentList.Add("app-server");
            startInfo.ArgumentList.Add("--listen");
            startInfo.ArgumentList.Add("stdio://");
            foreach (var argument in options.Arguments)
            {
                ArgumentNullException.ThrowIfNull(argument);
                startInfo.ArgumentList.Add(argument);
            }

            process = new Process { StartInfo = startInfo };
            if (!process.Start())
                throw new InvalidOperationException("Could not start the Codex app-server process.");
            processStarted = true;

            standardErrorDrain = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
            var handler = new NewLineDelimitedMessageHandler(
                process.StandardInput.BaseStream,
                process.StandardOutput.BaseStream,
                CreateTransportFormatter());
            var connectionTask = ConnectOwnedAsync(handler, options, process, standardErrorDrain, startupToken);
            process = null;
            return await connectionTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (IsStartupTimeout(startupTimeout, cancellationToken))
        {
            if (process is not null)
            {
                if (processStarted)
                    await StopProcessAsync(process, standardErrorDrain).ConfigureAwait(false);
                else
                    process.Dispose();
            }

            throw new TimeoutException($"Timed out starting the Codex app-server after {options.StartupTimeout}.", exception);
        }
        catch
        {
            if (process is not null)
            {
                if (processStarted)
                    await StopProcessAsync(process, standardErrorDrain).ConfigureAwait(false);
                else
                    process.Dispose();
            }
            throw;
        }
    }

    /// <summary>
    /// 使用调用方提供的消息处理器连接 app-server，并完成 JSON-RPC 握手。Connects using the supplied message handler and completes the JSON-RPC handshake.
    /// </summary>
    public static async Task<CodexAppServerClient> ConnectAsync(
        IJsonRpcMessageHandler messageHandler,
        CodexAppServerClientOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messageHandler);
        options ??= new();
        using var startupTimeout = CreateStartupTimeout(options, cancellationToken);
        var startupToken = startupTimeout?.Token ?? cancellationToken;
        try
        {
            return await ConnectOwnedAsync(messageHandler, options, null, Task.CompletedTask, startupToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (IsStartupTimeout(startupTimeout, cancellationToken))
        {
            throw new TimeoutException($"Timed out connecting to the Codex app-server after {options.StartupTimeout}.", exception);
        }
    }

    /// <summary>
    /// 获取 app-server 协议使用的 System.Text.Json 选项。Gets the System.Text.Json options used by the app-server protocol.
    /// </summary>
    public static JsonSerializerOptions CreateSerializerOptions() => AppServerJson.CreateOptions();

    /// <summary>
    /// 创建用于换行分隔 app-server 消息的 UTF-8 JSON-RPC formatter。Creates a UTF-8 JSON-RPC formatter for newline-delimited app-server messages.
    /// </summary>
    public static SystemTextJsonFormatter CreateFormatter() => new()
    {
        JsonSerializerOptions = CreateSerializerOptions(),
    };

    /// <summary>
    /// 创建供 app-server transport 使用的 formatter，并拥有接收消息缓冲区。Creates an app-server transport formatter with owned input buffers.
    /// </summary>
    public static IJsonRpcMessageTextFormatter CreateTransportFormatter() =>
        new OwnedInputJsonRpcMessageTextFormatter(CreateFormatter());

    /// <summary>
    /// 为有类型的 app-server 请求注册异步处理器。Registers an asynchronous handler for a typed app-server request.
    /// </summary>
    public void RegisterRequestHandler<TParams, TResult>(
        string method,
        Func<TParams, CancellationToken, Task<TResult>> handler)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentNullException.ThrowIfNull(handler);

        if (!AppServerProtocolRegistry.ServerRequestTypes.TryGetValue(method, out var requestTypes))
            throw new ArgumentException($"'{method}' is not a registered app-server request method.", nameof(method));
        if (requestTypes.ParamsType != typeof(TParams) || requestTypes.ResultType != typeof(TResult))
            throw new ArgumentException($"The registered request types for '{method}' do not match the supplied handler.", nameof(handler));

        if (!_requestHandlers.TryAdd(method, new RegisteredRequestHandler<TParams, TResult>(handler)))
            throw new InvalidOperationException($"A handler is already registered for app-server request '{method}'.");
    }

    /// <summary>
    /// 获取独立且按接收顺序排列的 app-server 通知流。Gets an independent app-server notification stream in receive order.
    /// </summary>
    public IAsyncEnumerable<AppServerNotification> Subscribe(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _notificationSubscriptions.Subscribe(cancellationToken);
    }

    internal async Task<TResult> InvokeAsync<TResult>(string method, object? parameters, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            // 本地取消停止等待；服务端操作由专用方法中断。Local cancellation stops waiting; dedicated methods interrupt server operations.
            var invocation = _rpc.InvokeWithParameterObjectAsync<TResult>(method, parameters, CancellationToken.None);
            ObserveTaskFailure(invocation);
            var result = await invocation.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            AppServerJson.ValidateParameters(result);
            return result;
        }
        catch (RemoteRpcException exception) when (exception.ErrorCode is not null)
        {
            throw new CodexAppServerException(
                (int)exception.ErrorCode.Value,
                exception.Message,
                GetErrorData(exception));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        try
        {
            _rpc.Dispose();
        }
        finally
        {
            try
            {
                await StopProcessAsync(_process, _standardErrorDrain).ConfigureAwait(false);
            }
            finally
            {
                await _completionMonitor.ConfigureAwait(false);
            }
        }
    }

    private static async Task<CodexAppServerClient> ConnectOwnedAsync(
        IJsonRpcMessageHandler messageHandler,
        CodexAppServerClientOptions options,
        Process? process,
        Task standardErrorDrain,
        CancellationToken cancellationToken)
    {
        JsonRpc? rpc = null;
        CodexAppServerClient? client = null;
        try
        {
            rpc = new JsonRpc(messageHandler);
            rpc.TraceSource.Switch.Level = SourceLevels.Off;
            rpc.CancelLocallyInvokedMethodsWhenConnectionIsClosed = true;
            client = new CodexAppServerClient(rpc, process, standardErrorDrain);
            client.RegisterProtocolMethods();
            rpc.StartListening();
            await client.InitializeAsync(options, cancellationToken).ConfigureAwait(false);
            return client;
        }
        catch
        {
            if (client is not null)
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                try
                {
                    if (rpc is not null)
                        rpc.Dispose();
                    else
                        await DisposeMessageHandlerAsync(messageHandler).ConfigureAwait(false);
                }
                finally
                {
                    await StopProcessAsync(process, standardErrorDrain).ConfigureAwait(false);
                }
            }

            throw;
        }
    }

    private async Task InitializeAsync(CodexAppServerClientOptions options, CancellationToken cancellationToken)
    {
        var initializeParameters = new
        {
            clientInfo = options.ClientInfo,
            capabilities = options.Capabilities,
        };
        _ = await InvokeAsync<InitializeResponse>("initialize", initializeParameters, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await _rpc.NotifyAsync("initialized", arguments: null, argumentDeclaredTypes: null).ConfigureAwait(false);
    }

    private void RegisterProtocolMethods()
    {
        foreach (var (method, parameterType) in AppServerProtocolRegistry.NotificationTypes)
        {
            Action<JsonElement> handler = parameters => PublishNotification(method, parameterType, parameters);
            _notificationHandlers.Add(handler);
            _rpc.AddLocalRpcMethod(
                handler.Method,
                handler.Target,
                new JsonRpcMethodAttribute(method) { UseSingleObjectParameterDeserialization = true });
        }

        foreach (var (method, requestTypes) in AppServerProtocolRegistry.ServerRequestTypes)
        {
            var dispatcherType = typeof(ServerRequestDispatcher<,>)
                .MakeGenericType(requestTypes.ParamsType, requestTypes.ResultType);
            var dispatcher = Activator.CreateInstance(
                dispatcherType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                args: [this, method],
                culture: null)!;
            var dispatchMethod = dispatcherType.GetMethod("InvokeAsync", BindingFlags.Instance | BindingFlags.Public)!;
            _rpc.AddLocalRpcMethod(
                dispatchMethod,
                dispatcher,
                new JsonRpcMethodAttribute(method) { UseSingleObjectParameterDeserialization = true });
        }
    }

    private void PublishNotification(string method, Type parametersType, JsonElement parameters)
    {
        try
        {
            ProtocolSchemas.Validate(parametersType, parameters);
            var typedParameters = JsonSerializer.Deserialize(parameters, parametersType, CreateSerializerOptions())
                ?? throw new JsonException($"Notification parameters for '{method}' were null.");
            var notification = new AppServerNotification(
                method,
                typedParameters,
                ReadStringProperty(parameters, "threadId") ?? ReadNestedStringProperty(parameters, "thread", "id"),
                ReadStringProperty(parameters, "turnId") ?? ReadNestedStringProperty(parameters, "turn", "id"));
            _notificationSubscriptions.Publish(notification);
        }
        catch (Exception exception)
        {
            _notificationSubscriptions.Complete(exception);
            throw;
        }
    }

    private Task<TResult> DispatchRequestAsync<TParams, TResult>(
        string method,
        TParams parameters,
        CancellationToken cancellationToken)
    {
        var dispatch = Task.Run(async () =>
        {
            AppServerJson.ValidateParameters(parameters);
            if (!_requestHandlers.TryGetValue(method, out var registeredHandler) ||
                registeredHandler is not RegisteredRequestHandler<TParams, TResult> handler)
                throw new LocalRpcException($"No handler is registered for app-server request '{method}'.")
                {
                    ErrorCode = (int)JsonRpcErrorCode.MethodNotFound,
                };

            var result = await handler.InvokeAsync(parameters, cancellationToken).ConfigureAwait(false);
            AppServerJson.ValidateParameters(result);
            return result;
        }, CancellationToken.None);
        ObserveTaskFailure(dispatch);
        return dispatch.WaitAsync(cancellationToken);
    }

    private static string? ReadStringProperty(JsonElement parameters, string propertyName) =>
        parameters.ValueKind == JsonValueKind.Object &&
        parameters.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? ReadNestedStringProperty(JsonElement parameters, string parentName, string propertyName) =>
        parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty(parentName, out var parent)
            ? ReadStringProperty(parent, propertyName)
            : null;

    private static JsonElement? GetErrorData(RemoteRpcException exception)
    {
        if (exception.ErrorData is JsonElement element)
            return element.ValueKind == JsonValueKind.Undefined ? null : element.Clone();
        return exception.ErrorData is null
            ? null
            : JsonSerializer.SerializeToElement(exception.ErrorData, CreateSerializerOptions());
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    private async Task MonitorConnectionAsync()
    {
        Exception? completionError = null;
        try
        {
            await _rpc.Completion.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            completionError = exception;
        }

        try
        {
            await _rpc.DispatchCompletion.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            completionError ??= exception;
        }

        _notificationSubscriptions.Complete(completionError);
    }

    private static void ObserveTaskFailure(Task task)
    {
        _ = task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static CancellationTokenSource? CreateStartupTimeout(
        CodexAppServerClientOptions options,
        CancellationToken cancellationToken)
    {
        if (options.StartupTimeout is not { } timeout)
            return null;
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "Startup timeout must be positive or null.");

        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cancellation.CancelAfter(timeout);
        return cancellation;
    }

    private static bool IsStartupTimeout(CancellationTokenSource? startupTimeout, CancellationToken cancellationToken) =>
        startupTimeout?.IsCancellationRequested == true && !cancellationToken.IsCancellationRequested;

    private static async Task DisposeMessageHandlerAsync(IJsonRpcMessageHandler messageHandler)
    {
        if (messageHandler is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        else if (messageHandler is IDisposable disposable)
            disposable.Dispose();
    }

    private static async Task StopProcessAsync(Process? process, Task standardErrorDrain)
    {
        if (process is null)
            return;

        try
        {
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) when (process.HasExited)
                {
                    await process.WaitForExitAsync().ConfigureAwait(false);
                }
            }

            await process.WaitForExitAsync().ConfigureAwait(false);
            await standardErrorDrain.ConfigureAwait(false);
        }
        finally
        {
            process.Dispose();
        }
    }

    private sealed class RegisteredRequestHandler<TParams, TResult>(
        Func<TParams, CancellationToken, Task<TResult>> handler)
    {
        public Task<TResult> InvokeAsync(TParams parameters, CancellationToken cancellationToken) =>
            handler(parameters, cancellationToken);
    }

    private sealed class ServerRequestDispatcher<TParams, TResult>(CodexAppServerClient client, string method)
    {
        public Task<TResult> InvokeAsync(TParams parameters, CancellationToken cancellationToken) =>
            client.DispatchRequestAsync<TParams, TResult>(method, parameters, cancellationToken);
    }
}
