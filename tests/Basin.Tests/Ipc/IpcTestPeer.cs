using System.Text;
using Basin.Ipc;

using Xunit;

namespace Basin.Tests;

internal sealed class IpcTestPeer : IDisposable
{
    private readonly Action _pump;
    private readonly List<byte> _received = [];
    private readonly List<int> _fds = [];

    public IpcTestPeer(int fd, Action pump)
    {
        Fd = fd;
        _pump = pump;
    }

    public int Fd { get; private set; }

    public bool Closed { get; private set; }

    public IReadOnlyList<int> ReceivedFds => _fds;

    public void SendRaw(ReadOnlySpan<byte> bytes)
    {
        var sent = 0;
        while (sent < bytes.Length)
        {
            var written = UnixSocket.Send(Fd, bytes[sent..], default);
            if (written < 0)
            {
                if (UnixSocket.LastError == UnixSocket.EAgain)
                {
                    _pump();
                    continue;
                }

                throw new IOException($"send failed with errno {UnixSocket.LastError}");
            }

            sent += (int)written;
        }
    }

    public void Send(string json) => SendRaw(Frame(json));

    public static byte[] Frame(string json)
    {
        var body = Encoding.UTF8.GetBytes(json);
        var frame = new byte[IpcProtocol.HeaderBytes + body.Length];
        IpcProtocol.WriteLength(frame, body.Length);
        body.CopyTo(frame, IpcProtocol.HeaderBytes);
        return frame;
    }

    public string Receive(int rounds = 200)
    {
        for (var i = 0; i < rounds; i++)
        {
            if (TryTakeFrame(out var frame))
            {
                return frame;
            }

            _pump();
            Drain();
            if (TryTakeFrame(out frame))
            {
                return frame;
            }

            if (Closed)
            {
                throw new IOException("the server closed the connection");
            }

            Thread.Sleep(1);
        }

        throw new TimeoutException("no frame arrived");
    }

    public string Call(string json)
    {
        Send(json);
        return Receive();
    }

    public bool WaitClosed(int rounds = 200)
    {
        for (var i = 0; i < rounds && !Closed; i++)
        {
            _pump();
            Drain();
        }

        return Closed;
    }

    public bool HasFrame()
    {
        _pump();
        Drain();
        return _received.Count >= IpcProtocol.HeaderBytes;
    }

    public void Dispose()
    {
        foreach (var fd in _fds)
        {
            _ = UnixSocket.Close(fd);
        }

        _fds.Clear();
        if (Fd >= 0)
        {
            _ = UnixSocket.Close(Fd);
            Fd = -1;
        }
    }

    private void Drain()
    {
        Span<byte> chunk = stackalloc byte[65536];
        Span<int> fds = stackalloc int[UnixSocket.MaxFds];
        while (true)
        {
            var read = UnixSocket.Receive(Fd, chunk, fds, out var count);
            for (var i = 0; i < count; i++)
            {
                _fds.Add(fds[i]);
            }

            if (read < 0)
            {
                return;
            }

            if (read == 0)
            {
                Closed = true;
                return;
            }

            for (var i = 0; i < read; i++)
            {
                _received.Add(chunk[i]);
            }
        }
    }

    private bool TryTakeFrame(out string frame)
    {
        frame = string.Empty;
        if (_received.Count < IpcProtocol.HeaderBytes)
        {
            return false;
        }

        var header = _received.GetRange(0, IpcProtocol.HeaderBytes).ToArray();
        var length = (int)IpcProtocol.ReadLength(header);
        if (_received.Count < IpcProtocol.HeaderBytes + length)
        {
            return false;
        }

        frame = Encoding.UTF8.GetString(_received.GetRange(IpcProtocol.HeaderBytes, length).ToArray());
        _received.RemoveRange(0, IpcProtocol.HeaderBytes + length);
        return true;
    }
}
