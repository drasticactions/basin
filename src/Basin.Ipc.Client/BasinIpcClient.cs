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

    public string Path { get; }

    public bool IsConnected => _failure is null && !_closing.IsCancellationRequested;

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

    public static string[] Candidates(string? runtimeDirectory = null)
    {
        runtimeDirectory ??= Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (string.IsNullOrEmpty(runtimeDirectory) || !Directory.Exists(runtimeDirectory))
        {
            return [];
        }

        var candidates = Directory.GetFiles(runtimeDirectory, IpcProtocol.SocketPrefix + "*" + IpcProtocol.SocketSuffix);
        Array.Sort(candidates, StringComparer.Ordinal);
        return candidates;
    }

    public static Task<BasinIpcClient> ConnectAsync(string? path = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resolved = ResolvePath(path)
            ?? throw new IOException(Candidates() is { Length: > 1 } several
                ? $"several control sockets and no choice: {string.Join(", ", several)}; set {IpcProtocol.SocketVariable} or pass a path"
                : $"no control socket: set {IpcProtocol.SocketVariable}, pass a path, or run exactly one basin compositor");
        var fd = UnixSocket.Create();
        if (fd < 0)
        {
            throw new IOException($"socket() failed with errno {UnixSocket.LastError}");
        }

        if (UnixSocket.Connect(fd, resolved) != 0)
        {
            var error = UnixSocket.LastError;
            _ = UnixSocket.Close(fd);
            throw new IOException($"cannot connect to '{resolved}' (errno {error})");
        }

        var socket = new Socket(new SafeSocketHandle(fd, ownsHandle: true));
        return Task.FromResult(new BasinIpcClient(socket, fd, resolved));
    }

    public async Task<IpcResult> CallAsync(
        string method, Action<Utf8JsonWriter>? writeParams = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(method);
        ObjectDisposedException.ThrowIf(_closing.IsCancellationRequested, this);
        if (_failure is { } failure)
        {
            throw new IOException("the connection to the compositor failed", failure);
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

    public IAsyncEnumerable<IpcEvent> SubscribeAsync(IReadOnlyList<string> events, CancellationToken cancellationToken = default) =>
        SubscribeAsync(events, null, cancellationToken);

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

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
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
            throw new ArgumentException($"a {length}-byte request is over the {IpcProtocol.MaxRequestBytes}-byte limit");
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

                        throw new IOException($"recvmsg failed with errno {error}");
                    }

                    if (read == 0)
                    {
                        throw new IOException("the compositor closed the control connection");
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
                throw new IOException($"the compositor sent a {size}-byte frame, over the limit");
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
            pending.Completion.TrySetException(new IOException("the connection to the compositor failed", failure));
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
