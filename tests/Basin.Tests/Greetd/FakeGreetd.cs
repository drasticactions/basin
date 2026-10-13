using System.Net.Sockets;
using System.Text.Json;
using Basin.Greetd;

namespace Basin.Tests;

internal sealed class FakeGreetd : IDisposable
{
    private readonly object _lock = new();
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "basin-greetd-" + Guid.NewGuid().ToString("N"));
    private readonly Socket _listener;
    private readonly Thread _thread;
    private readonly Queue<GreetdResponse?> _responses = new();
    private readonly List<GreetdRequest> _requests = [];
    private readonly List<byte[]> _payloads = [];
    private readonly ManualResetEventSlim _startGate = new(true);
    private Socket? _peer;
    private volatile bool _stopping;

    public FakeGreetd()
    {
        Directory.CreateDirectory(_directory);
        SocketPath = Path.Combine(_directory, "greetd.sock");
        _listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        _listener.Bind(new UnixDomainSocketEndPoint(SocketPath));
        _listener.Listen(4);
        _thread = new Thread(Serve) { IsBackground = true, Name = "fake-greetd" };
        _thread.Start();
    }

    public string SocketPath { get; }

    public void Respond(params GreetdResponse?[] responses)
    {
        lock (_lock)
        {
            foreach (var response in responses)
            {
                _responses.Enqueue(response);
            }
        }
    }

    public void HoldStartSession() => _startGate.Reset();

    public void ReleaseStartSession() => _startGate.Set();

    public List<GreetdRequest> Requests()
    {
        lock (_lock)
        {
            return [.. _requests];
        }
    }

    public List<byte[]> Payloads()
    {
        lock (_lock)
        {
            return [.. _payloads];
        }
    }

    public void Drop()
    {
        lock (_lock)
        {
            _peer?.Shutdown(SocketShutdown.Both);
        }
    }

    public void Dispose()
    {
        _stopping = true;
        _startGate.Set();
        lock (_lock)
        {
            _peer?.Dispose();
        }

        _listener.Dispose();
        _thread.Join(2000);
        Directory.Delete(_directory, true);
    }

    private void Serve()
    {
        try
        {
            while (!_stopping)
            {
                var peer = _listener.Accept();
                lock (_lock)
                {
                    _peer = peer;
                }

                Converse(peer);
                peer.Dispose();
            }
        }
        catch (Exception error) when (error is SocketException or ObjectDisposedException)
        {
        }
    }

    private void Converse(Socket peer)
    {
        using var writer = new GreetdFrameWriter();
        var header = new byte[GreetdFrame.HeaderLength];
        while (!_stopping)
        {
            if (!ReadExactly(peer, header))
            {
                return;
            }

            var length = BitConverter.ToUInt32(header);
            var payload = new byte[length];
            if (!ReadExactly(peer, payload))
            {
                return;
            }

            var request = JsonSerializer.Deserialize(payload, GreetdJsonContext.Default.GreetdRequest)!;
            GreetdResponse? response;
            lock (_lock)
            {
                _requests.Add(request);
                _payloads.Add(payload);
                response = _responses.Count > 0
                    ? _responses.Dequeue()
                    : request is GreetdCancelSession ? new GreetdSuccess() : new GreetdError(GreetdErrorType.Error, "unscripted");
            }

            if (request is GreetdStartSession)
            {
                _startGate.Wait(TimeSpan.FromSeconds(30));
            }

            if (response is null)
            {
                peer.Shutdown(SocketShutdown.Both);
                return;
            }

            writer.Write(response);
            peer.Send(writer.Frame);
        }
    }

    private static bool ReadExactly(Socket peer, byte[] buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = peer.Receive(buffer, read, buffer.Length - read, SocketFlags.None);
            if (n == 0)
            {
                return false;
            }

            read += n;
        }

        return true;
    }
}
