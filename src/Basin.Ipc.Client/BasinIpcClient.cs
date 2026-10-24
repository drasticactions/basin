using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Win32.SafeHandles;

namespace Basin.Ipc;

/// <summary>
/// A connection to a compositor's control socket. Calls can run in parallel from any thread. An error reply raises <see cref="IpcCallException"/>, and a failed connection raises <see cref="IOException"/>.
/// </summary>
public sealed partial class BasinIpcClient : IAsyncDisposable, IDisposable
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly Socket _socket;
    private readonly int _fd;
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly ConcurrentDictionary<long, Pending> _pending = new();
    private readonly List<Channel<IpcEvent>> _subscribers = [];
    private readonly Lock _subscriberGate = new();
    private readonly CancellationTokenSource _closing = new();
    private readonly Task _reader;
    private long _nextId;
    private Exception? _failure;

    private BasinIpcClient(Socket socket, int fd, string path)
    {
        _socket = socket;
        _fd = fd;
        Path = path;
        _reader = Task.Run(ReadLoopAsync);
    }

    /// <summary>
    /// Gets the socket path that the client connected to.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets a value indicating whether the connection has not failed and is not disposed.
    /// </summary>
    public bool IsConnected => _failure is null && !_closing.IsCancellationRequested;

    /// <summary>
    /// Chooses a socket path: the given path, then <c>BASIN_SOCKET</c>, then the one <c>basin-*.sock</c> file in <c>XDG_RUNTIME_DIR</c>.
    /// </summary>
    /// <param name="path">The path to use, or <see langword="null"/> to search.</param>
    /// <returns>The path, or <see langword="null"/> when no socket or several sockets are found.</returns>
    public static string? ResolvePath(string? path = null)
    {
        if (!string.IsNullOrEmpty(path))
        {
            return path;
        }

        if (Environment.GetEnvironmentVariable(IpcProtocol.SocketVariable) is { Length: > 0 } fromEnvironment)
        {
            return fromEnvironment;
        }

        if (Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is not { Length: > 0 } runtime || !Directory.Exists(runtime))
        {
            return null;
        }

        var candidates = Candidates(runtime);
        return candidates.Length == 1 ? candidates[0] : null;
    }

    /// <summary>
    /// Lists the <c>basin-*.sock</c> files in a runtime directory that a compositor listens on. A file that refuses a connection is left out, because its compositor stopped without removing it.
    /// </summary>
    /// <param name="runtimeDirectory">The directory, or <see langword="null"/> for <c>XDG_RUNTIME_DIR</c>.</param>
    /// <returns>The paths in ordinal order, or an empty array when the directory does not exist.</returns>
    public static string[] Candidates(string? runtimeDirectory = null)
    {
        runtimeDirectory ??= Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (string.IsNullOrEmpty(runtimeDirectory) || !Directory.Exists(runtimeDirectory))
        {
            return [];
        }

        var candidates = Array.FindAll(
            Directory.GetFiles(runtimeDirectory, IpcProtocol.SocketPrefix + "*" + IpcProtocol.SocketSuffix),
            static candidate => !Refuses(candidate));
        Array.Sort(candidates, StringComparer.Ordinal);
        return candidates;
    }

    /// <summary>
    /// Connects to a control socket. The connect is synchronous, and the task is complete when it returns.
    /// </summary>
    /// <param name="path">The socket path, or <see langword="null"/> to resolve it as <see cref="ResolvePath"/> does.</param>
    /// <param name="cancellationToken">A token checked before the connect.</param>
    /// <returns>The connected client.</returns>
    /// <exception cref="IOException">No socket path is found, or the connect fails.</exception>
    public static Task<BasinIpcClient> ConnectAsync(string? path = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resolved = ResolvePath(path)
            ?? throw new IOException(Candidates() is { Length: > 1 } several
                ? $"There are several control sockets: {string.Join(", ", several)}. Set {IpcProtocol.SocketVariable} or give a path."
                : $"No control socket is found. Set {IpcProtocol.SocketVariable}, give a path, or run exactly one basin compositor.");
        var fd = UnixSocket.Create();
        if (fd < 0)
        {
            throw new IOException($"The client could not make a socket (errno {UnixSocket.LastError}).");
        }

        if (UnixSocket.Connect(fd, resolved) != 0)
        {
            var error = UnixSocket.LastError;
            _ = UnixSocket.Close(fd);
            throw new IOException($"No compositor answers on '{resolved}' (errno {error}). Check that the compositor runs.");
        }

        var socket = new Socket(new SafeSocketHandle(fd, ownsHandle: true));
        return Task.FromResult(new BasinIpcClient(socket, fd, resolved));
    }

    /// <summary>
    /// Calls a method and waits for its reply.
    /// </summary>
    /// <param name="method">The method name.</param>
    /// <param name="writeParams">A callback that writes the params value, or <see langword="null"/> for no params.</param>
    /// <param name="cancellationToken">A token that stops the wait for the reply. The compositor can still run the call.</param>
    /// <returns>The result, which the caller must dispose to close its descriptors.</returns>
    /// <exception cref="IpcCallException">The compositor answered with an error.</exception>
    /// <exception cref="IOException">The connection failed.</exception>
    /// <exception cref="ArgumentException">The request is larger than the request limit.</exception>
    public async Task<IpcResult> CallAsync(
        string method, Action<Utf8JsonWriter>? writeParams = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(method);
        ObjectDisposedException.ThrowIf(_closing.IsCancellationRequested, this);
        if (_failure is { } failure)
        {
            throw new IOException($"The connection to the compositor failed. {failure.Message}", failure);
        }

        var id = Interlocked.Increment(ref _nextId);
        var pending = new Pending(method);
        _pending[id] = pending;
        try
        {
            var frame = BuildRequest(id, method, writeParams);
            await SendAsync(frame, cancellationToken).ConfigureAwait(false);
            using var registration = cancellationToken.Register(() => pending.Completion.TrySetCanceled(cancellationToken));
            return await pending.Completion.Task.ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    /// <summary>
    /// Calls a method and reads its result before the result is disposed.
    /// </summary>
    /// <typeparam name="T">The type that the read returns.</typeparam>
    /// <param name="method">The method name.</param>
    /// <param name="writeParams">A callback that writes the params value, or <see langword="null"/> for no params.</param>
    /// <param name="read">A function that reads the result. Descriptors that it does not take are closed after it returns.</param>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>The value that <paramref name="read"/> returns.</returns>
    public async Task<T> CallAsync<T>(
        string method,
        Action<Utf8JsonWriter>? writeParams,
        Func<IpcResult, T> read,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(read);
        using var result = await CallAsync(method, writeParams, cancellationToken).ConfigureAwait(false);
        return read(result);
    }

    /// <summary>
    /// Sends a request written as JSON text. The client replaces its <c>id</c> with one of its own.
    /// </summary>
    /// <param name="json">A request object with <c>method</c> and optional <c>params</c>.</param>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>The result, which the caller must dispose.</returns>
    /// <exception cref="ArgumentException">The text is not a valid request.</exception>
    public async Task<IpcResult> RawAsync(string json, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(json);
        var bytes = Encoding.UTF8.GetBytes(json);
        if (!IpcRequest.TryParse(bytes, out var request, out var error))
        {
            throw new ArgumentException(error, nameof(json));
        }

        var method = request.Method!;
        var parameters = request.Params.ToArray();
        return await CallAsync(
            method,
            parameters.Length == 0 ? null : writer => writer.WriteRawValue(parameters),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Subscribes to events when the enumeration starts, and yields the events with the given names.
    /// </summary>
    /// <param name="events">The event names.</param>
    /// <param name="cancellationToken">A token that ends the enumeration.</param>
    /// <returns>The events. The enumeration ends when the client is disposed, and throws when the connection fails.</returns>
    public IAsyncEnumerable<IpcEvent> SubscribeAsync(IReadOnlyList<string> events, CancellationToken cancellationToken = default) =>
        SubscribeAsync(events, null, cancellationToken);

    /// <summary>
    /// Subscribes to events when the enumeration starts, and yields the events with the given names. Events that arrive before the subscription reply are kept.
    /// </summary>
    /// <param name="events">The event names.</param>
    /// <param name="subscribed">An action that runs after the compositor confirms the subscription, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">A token that ends the enumeration.</param>
    /// <returns>The events. The enumeration ends when the client is disposed, and throws when the connection fails.</returns>
    public async IAsyncEnumerable<IpcEvent> SubscribeAsync(
        IReadOnlyList<string> events, Action? subscribed, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(events);
        var channel = Channel.CreateUnbounded<IpcEvent>(new UnboundedChannelOptions { SingleReader = true });
        lock (_subscriberGate)
        {
            _subscribers.Add(channel);
        }

        try
        {
            using (await CallAsync(
                IpcMethodNames.Subscribe,
                writer => JsonSerializer.Serialize(writer, new IpcEventsParams(events), IpcJsonContext.Default.IpcEventsParams),
                cancellationToken).ConfigureAwait(false))
            {
            }

            subscribed?.Invoke();

            await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                if (Contains(events, item.Name))
                {
                    yield return item;
                }
            }
        }
        finally
        {
            lock (_subscriberGate)
            {
                _subscribers.Remove(channel);
            }
        }
    }

    /// <summary>
    /// Closes the connection. Pending calls fail with <see cref="IOException"/>, and subscriptions end.
    /// </summary>
    public void Dispose()
    {
        if (_closing.IsCancellationRequested)
        {
            return;
        }

        _closing.Cancel();
        try
        {
            _socket.Shutdown(SocketShutdown.Both);
        }
        catch (SocketException)
        {
        }

        _socket.Dispose();
        try
        {
            _reader.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        Fail(new ObjectDisposedException(nameof(BasinIpcClient)));
        _sendGate.Dispose();
        _closing.Dispose();
    }

    /// <summary>
    /// Closes the connection, as <see cref="Dispose"/> does.
    /// </summary>
    /// <returns>A completed task.</returns>
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private static bool Refuses(string path)
    {
        var fd = UnixSocket.Create();
        if (fd < 0)
        {
            return false;
        }

        var refused = UnixSocket.Connect(fd, path) != 0 && UnixSocket.LastError == UnixSocket.EConnRefused;
        _ = UnixSocket.Close(fd);
        return refused;
    }

    private static bool Contains(IReadOnlyList<string> names, string name)
    {
        foreach (var candidate in names)
        {
            if (candidate == name)
            {
                return true;
            }
        }

        return false;
    }

    private static byte[] BuildRequest(long id, string method, Action<Utf8JsonWriter>? writeParams)
    {
        using var stream = new MemoryStream();
        stream.Write(new byte[IpcProtocol.HeaderBytes]);
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteNumber("id"u8, id);
            writer.WriteString("method"u8, method);
            if (writeParams is not null)
            {
                writer.WritePropertyName("params"u8);
                writeParams(writer);
            }

            writer.WriteEndObject();
        }

        var frame = stream.ToArray();
        var length = frame.Length - IpcProtocol.HeaderBytes;
        if (length > IpcProtocol.MaxRequestBytes)
        {
            throw new ArgumentException($"The {length}-byte request is larger than the {IpcProtocol.MaxRequestBytes}-byte limit.");
        }

        IpcProtocol.WriteLength(frame, length);
        return frame;
    }

    private async Task SendAsync(byte[] frame, CancellationToken cancellationToken)
    {
        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var sent = 0;
            while (sent < frame.Length)
            {
                sent += await _socket.SendAsync(frame.AsMemory(sent), SocketFlags.None, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private async Task ReadLoopAsync()
    {
        var buffer = new byte[64 * 1024];
        var length = 0;
        var marks = new List<(int Start, int End, int[] Fds)>();
        var fds = new int[UnixSocket.MaxFds];
        try
        {
            while (!_closing.IsCancellationRequested)
            {
                _ = await _socket.ReceiveAsync(Memory<byte>.Empty, SocketFlags.None, _closing.Token).ConfigureAwait(false);
                while (true)
                {
                    if (buffer.Length - length < 4096)
                    {
                        Array.Resize(ref buffer, buffer.Length * 2);
                    }

                    var read = UnixSocket.Receive(_fd, buffer.AsSpan(length), fds, out var fdCount);
                    if (fdCount > 0)
                    {
                        marks.Add((length, length + (int)Math.Max(read, 0), fds[..fdCount]));
                    }

                    if (read < 0)
                    {
                        var error = UnixSocket.LastError;
                        if (error == UnixSocket.EIntr)
                        {
                            continue;
                        }

                        if (error == UnixSocket.EAgain)
                        {
                            break;
                        }

                        throw new IOException($"The read from the control socket failed (errno {error}).");
                    }

                    if (read == 0)
                    {
                        throw new IOException("The compositor closed the control connection.");
                    }

                    length += (int)read;
                    var consumed = Dispatch(buffer, length, marks);
                    if (consumed > 0)
                    {
                        Array.Copy(buffer, consumed, buffer, 0, length - consumed);
                        length -= consumed;
                        for (var i = 0; i < marks.Count; i++)
                        {
                            marks[i] = (marks[i].Start - consumed, marks[i].End - consumed, marks[i].Fds);
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException)
        {
            Fail(exception);
        }
    }

    private int Dispatch(byte[] buffer, int length, List<(int Start, int End, int[] Fds)> marks)
    {
        var offset = 0;
        while (length - offset >= IpcProtocol.HeaderBytes)
        {
            var size = IpcProtocol.ReadLength(buffer.AsSpan(offset));
            if (size > IpcProtocol.MaxMessageBytes)
            {
                throw new IOException($"The compositor sent a {size}-byte frame, which is larger than the limit.");
            }

            if (length - offset - IpcProtocol.HeaderBytes < (int)size)
            {
                break;
            }

            var descriptors = Array.Empty<int>();
            var end = offset + IpcProtocol.HeaderBytes + (int)size;
            for (var i = 0; i < marks.Count; i++)
            {
                if (offset >= marks[i].Start && offset < marks[i].End && end >= marks[i].End)
                {
                    descriptors = marks[i].Fds;
                    marks.RemoveAt(i);
                    break;
                }
            }

            Handle(buffer.AsSpan(offset + IpcProtocol.HeaderBytes, (int)size), descriptors);
            offset += IpcProtocol.HeaderBytes + (int)size;
        }

        return offset;
    }

    private void Handle(ReadOnlySpan<byte> frame, int[] descriptors)
    {
        if (!IpcMessage.TryParse(frame, out var message))
        {
            Close(descriptors);
            return;
        }

        if (message.Kind == IpcMessageKind.Event)
        {
            Close(descriptors);
            var item = new IpcEvent(message.EventName ?? string.Empty, message.Payload.ToArray());
            lock (_subscriberGate)
            {
                foreach (var subscriber in _subscribers)
                {
                    _ = subscriber.Writer.TryWrite(item);
                }
            }

            return;
        }

        if (!long.TryParse(Encoding.UTF8.GetString(message.Id), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            || !_pending.TryGetValue(id, out var pending))
        {
            Close(descriptors);
            return;
        }

        if (message.Kind == IpcMessageKind.Error)
        {
            Close(descriptors);
            pending.Completion.TrySetException(
                new IpcCallException(pending.Method, message.ErrorCode ?? IpcErrorCodes.Internal, message.ErrorMessage ?? string.Empty));
            return;
        }

        var handles = new SafeFileHandle[descriptors.Length];
        for (var i = 0; i < descriptors.Length; i++)
        {
            handles[i] = new SafeFileHandle(descriptors[i], ownsHandle: true);
        }

        var result = new IpcResult(pending.Method, message.Payload.ToArray(), handles);
        if (!pending.Completion.TrySetResult(result))
        {
            result.Dispose();
        }
    }

    private void Fail(Exception failure)
    {
        _failure ??= failure;
        foreach (var pending in _pending.Values)
        {
            pending.Completion.TrySetException(new IOException($"The connection to the compositor failed. {failure.Message}", failure));
        }

        lock (_subscriberGate)
        {
            foreach (var subscriber in _subscribers)
            {
                subscriber.Writer.TryComplete(failure is ObjectDisposedException ? null : failure);
            }
        }
    }

    private static void Close(int[] descriptors)
    {
        foreach (var fd in descriptors)
        {
            _ = UnixSocket.Close(fd);
        }
    }

    private sealed class Pending(string method)
    {
        public string Method { get; } = method;

        public TaskCompletionSource<IpcResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
