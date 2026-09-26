using System.Text.Encodings.Web;
using System.Text.Json;
using Basin.Diagnostics;
using static Basin.Ipc.IpcLog;

namespace Basin.Ipc;

internal sealed class IpcConnection : IIpcReplySink, IIpcEventSubscriber, IDisposable
{
    public const int MaxQueuedBytes = 64 * 1024 * 1024;

    private const int ReceiveChunk = 4096;
    private const int IdleCapacity = 4096;

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        SkipValidation = true,
    };

    private readonly IpcServer _server;
    private readonly IpcByteBuffer _receive = new(ReceiveChunk);
    private readonly IpcByteBuffer _send = new(ReceiveChunk);
    private readonly List<FdMark> _marks = [];
    private readonly Utf8JsonWriter _writer;
    private readonly IpcReply _reply;
    private readonly int[] _fdScratch = new int[UnixSocket.MaxFds];
    private IEventSource? _source;
    private bool _writable;
    private bool _readClosed;

    public IpcConnection(IpcServer server, int fd, ICompositorEventLoop loop)
    {
        _server = server;
        Fd = fd;
        _writer = new Utf8JsonWriter(_send, WriterOptions);
        _reply = new IpcReply(this);
        _source = loop.AddFd(fd, FdReadiness.Readable, OnReady);
        BasinCounters.Track();
    }

    public int Fd { get; private set; }

    public bool IsOpen => Fd >= 0;

    public IpcClientState State { get; } = new();

    public int QueuedBytes => _send.Length;

    public void Deliver(IpcReply reply)
    {
        if (!IsOpen)
        {
            reply.ReleaseFds();
            return;
        }

        var start = _send.Length;
        WriteReply(reply, start);
        if (_send.Length - start - IpcProtocol.HeaderBytes > IpcProtocol.MaxMessageBytes)
        {
            _send.Truncate(start);
            reply.ReleaseFds();
            reply.Fail(IpcErrorCodes.TooLarge, "the reply is larger than a frame can carry; ask for a path or an fd");
            WriteReply(reply, start);
        }

        if (reply.FdCount > 0)
        {
            var fds = new int[reply.FdCount];
            _ = reply.TakeFds(fds);
            _marks.Add(new FdMark { Offset = start, End = _send.Length, Fds = fds });
        }

        Flush();
    }

    public void OnEvent(string name, ReadOnlySpan<byte> message)
    {
        if (!IsOpen)
        {
            return;
        }

        var start = _send.Length;
        _ = _send.GetSpan(IpcProtocol.HeaderBytes + message.Length);
        _send.Advance(IpcProtocol.HeaderBytes);
        _send.Append(message);
        IpcProtocol.WriteLength(_send.WrittenSpan.Slice(start, IpcProtocol.HeaderBytes), message.Length);
        Flush();
    }

    public void Dispose()
    {
        if (!IsOpen)
        {
            return;
        }

        _source?.Remove();
        _source = null;
        _ = UnixSocket.Close(Fd);
        Fd = -1;
        foreach (var mark in _marks)
        {
            foreach (var fd in mark.Fds)
            {
                _ = UnixSocket.Close(fd);
            }
        }

        _marks.Clear();
        _reply.ReleaseFds();
        _server.Events.UnsubscribeAll(this);
        State.Dispose();
        _writer.Dispose();
        _server.Forget(this);
        BasinCounters.Untrack();
    }

    private void WriteReply(IpcReply reply, int start)
    {
        _ = _send.GetSpan(IpcProtocol.HeaderBytes);
        _send.Advance(IpcProtocol.HeaderBytes);
        _writer.Reset(_send);
        _writer.WriteStartObject();
        if (!reply.Id.IsEmpty)
        {
            _writer.WritePropertyName("id"u8);
            _writer.WriteRawValue(reply.Id, skipInputValidation: true);
        }

        if (reply.IsError)
        {
            _writer.WritePropertyName("error"u8);
            JsonSerializer.Serialize(_writer, new IpcErrorBody(reply.ErrorCode!, reply.ErrorMessage!), IpcJsonContext.Default.IpcErrorBody);
        }
        else
        {
            _writer.WritePropertyName("result"u8);
            _writer.WriteRawValue(reply.ResultJson, skipInputValidation: true);
        }

        _writer.WriteEndObject();
        _writer.Flush();
        var length = _send.Length - start - IpcProtocol.HeaderBytes;
        IpcProtocol.WriteLength(_send.WrittenSpan.Slice(start, IpcProtocol.HeaderBytes), length);
    }

    private void OnReady(int fd, FdReadiness readiness)
    {
        try
        {
            if ((readiness & FdReadiness.Writable) != 0)
            {
                Flush();
            }

            if (IsOpen && _readClosed && (readiness & (FdReadiness.Hangup | FdReadiness.Error)) != 0)
            {
                Dispose();
                return;
            }

            if (IsOpen && !_readClosed && (readiness & (FdReadiness.Readable | FdReadiness.Hangup | FdReadiness.Error)) != 0)
            {
                Read();
            }
        }
        catch (Exception exception)
        {
            Log.Error($"connection {Fd} failed: {exception}");
            Dispose();
        }
    }

    private void Read()
    {
        while (IsOpen)
        {
            var space = _receive.GetSpan(ReceiveChunk);
            var read = UnixSocket.Receive(Fd, space, _fdScratch, out var fdCount);
            for (var i = 0; i < fdCount; i++)
            {
                _ = UnixSocket.Close(_fdScratch[i]);
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

                Log.Debug($"connection {Fd} read failed with errno {error}");
                Dispose();
                return;
            }

            if (read == 0)
            {
                _readClosed = true;
                break;
            }

            _receive.Advance((int)read);
            if (!ProcessFrames())
            {
                return;
            }
        }

        if (!IsOpen)
        {
            return;
        }

        if (_readClosed)
        {
            if (!ProcessFrames())
            {
                return;
            }

            UpdateInterest();
            if (_send.Length == 0)
            {
                Dispose();
            }
        }
        else if (_receive.Length == 0)
        {
            _receive.Release(ReceiveChunk);
        }
    }

    private bool ProcessFrames()
    {
        var consumed = 0;
        while (IsOpen)
        {
            var pending = _receive.Written[consumed..];
            if (pending.Length < IpcProtocol.HeaderBytes)
            {
                break;
            }

            var length = IpcProtocol.ReadLength(pending);
            if (length > IpcProtocol.MaxRequestBytes)
            {
                Log.Warn($"connection {Fd} sent a {length}-byte request, over the limit; closing it");
                Dispose();
                return false;
            }

            if (pending.Length < IpcProtocol.HeaderBytes + (int)length)
            {
                break;
            }

            Dispatch(pending.Slice(IpcProtocol.HeaderBytes, (int)length));
            consumed += IpcProtocol.HeaderBytes + (int)length;
        }

        if (!IsOpen)
        {
            return false;
        }

        _receive.Consume(consumed);
        return true;
    }

    private void Dispatch(ReadOnlySpan<byte> frame)
    {
        if (!IpcRequest.TryParse(frame, out var request, out var error))
        {
            _reply.Begin(request.Id, request.Method ?? string.Empty);
            _reply.Error(IpcErrorCodes.ParseError, error ?? "malformed request");
            Deliver(_reply);
            return;
        }

        _server.Invoke(request.Method!, request.Id, request.Params, _reply);
        if (!_reply.IsDeferred)
        {
            Deliver(_reply);
        }
    }

    private void Flush()
    {
        while (IsOpen && _send.Length > 0)
        {
            var limit = _send.Length;
            ReadOnlySpan<int> fds = default;
            if (_marks.Count > 0)
            {
                if (_marks[0].Offset == 0)
                {
                    fds = _marks[0].Fds;
                    limit = _marks[0].End;
                }
                else
                {
                    limit = _marks[0].Offset;
                }
            }

            var sent = UnixSocket.Send(Fd, _send.Written[..limit], fds);
            if (sent < 0)
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

                Log.Debug($"connection {Fd} write failed with errno {error}");
                Dispose();
                return;
            }

            if (!fds.IsEmpty && sent > 0)
            {
                foreach (var fd in _marks[0].Fds)
                {
                    _ = UnixSocket.Close(fd);
                }

                _marks.RemoveAt(0);
            }

            _send.Consume((int)sent);
            foreach (var mark in _marks)
            {
                mark.Offset -= (int)sent;
                mark.End -= (int)sent;
            }
        }

        if (!IsOpen)
        {
            return;
        }

        if (_send.Length > MaxQueuedBytes)
        {
            Log.Warn($"connection {Fd} has {_send.Length} bytes queued and is not reading; closing it");
            Dispose();
            return;
        }

        UpdateInterest();
        if (_send.Length == 0)
        {
            _send.Release(IdleCapacity);
            if (_readClosed)
            {
                Dispose();
            }
        }
    }

    private void UpdateInterest()
    {
        var wantWrite = _send.Length > 0;
        if (wantWrite == _writable && !_readClosed)
        {
            return;
        }

        _writable = wantWrite;
        var interest = (_readClosed ? FdReadiness.None : FdReadiness.Readable) | (wantWrite ? FdReadiness.Writable : FdReadiness.None);
        _source?.UpdateFd(interest);
    }

    private sealed class FdMark
    {
        public int Offset { get; set; }

        public int End { get; set; }

        public int[] Fds { get; init; } = [];
    }
}
