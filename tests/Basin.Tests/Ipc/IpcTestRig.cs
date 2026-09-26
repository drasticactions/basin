using Basin.Ipc;

using Xunit;

namespace Basin.Tests;

internal sealed class IpcTestRig : IDisposable
{
    private readonly List<IpcTestPeer> _peers = [];
    private readonly List<IDisposable> _owned = [];
    private bool _disposed;

    public IpcTestRig(Action<BasinServices>? register = null, IpcSessionInfo? session = null, string? path = null, bool listen = false)
        : this((services, _) => register?.Invoke(services), session, path, listen)
    {
    }

    public IpcTestRig(Action<BasinServices, CompositorTestHost> register, IpcSessionInfo? session = null, string? path = null, bool listen = false)
    {
        Host = new CompositorTestHost();
        Services = new BasinServices(Host.Loop);
        register(Services, Host);
        Services.Freeze();
        Server = new IpcServer(Host.Loop, Services, session ?? new IpcSessionInfo { Compositor = "test" }, path, listen);
    }

    public CompositorTestHost Host { get; }

    public BasinServices Services { get; }

    public IpcServer Server { get; }

    public void Pump() => Host.Loop.Dispatch(0);

    public T Own<T>(T resource)
        where T : IDisposable
    {
        _owned.Add(resource);
        return resource;
    }

    public IpcTestPeer Connect()
    {
        if (!Server.IsStarted)
        {
            Server.Start();
        }

        Assert.Equal(0, UnixSocket.Pair(out var mine, out var theirs));
        Server.Adopt(theirs);
        var peer = new IpcTestPeer(mine, Pump);
        _peers.Add(peer);
        return peer;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Server.Dispose();
        foreach (var resource in _owned)
        {
            resource.Dispose();
        }

        foreach (var peer in _peers)
        {
            peer.Dispose();
        }

        Services.Dispose();
        Host.Dispose();
    }
}
