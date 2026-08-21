using System.Runtime.InteropServices;
using Basin.Cli;
using Basin.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace Basin.Tests;

internal sealed class DamTestHost : IDisposable
{
    private const int AF_UNIX = 1;
    private const int SOCK_STREAM = 1;

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int socketpair(int domain, int type, int protocol, int* sv);

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int poll(PollFd* fds, nuint nfds, int timeout);

    [StructLayout(LayoutKind.Sequential)]
    private struct PollFd
    {
        public int Fd;
        public short Events;
        public short REvents;
    }

    private readonly List<ShmTestClient> _clients = [];

    public DamTestHost(bool serverDecorations = false, bool lastOutputOnly = false)
    {
        CompositorTestHost.SkipWithoutWaylandClient();
        BasinCounters.Reset();
        Console.Out.Write(string.Empty);
        Console.Error.Write(string.Empty);
        Console.Out.Flush();
        Console.Error.Flush();

        Dam = new global::Dam.Dam(
            new global::Dam.DamOptions
            {
                Backend = BackendKind.Headless,
                Renderer = "pixman",
                ServerDecorations = serverDecorations,
                LastOutputOnly = lastOutputOnly,
            },
            NullLogger.Instance);
        Client = ConnectClient();
    }

    public global::Dam.Dam Dam { get; }

    public ShmTestClient Client { get; }

    public ShmTestClient ConnectClient()
    {
        int serverFd, clientFd;
        unsafe
        {
            var fds = stackalloc int[2];
            if (socketpair(AF_UNIX, SOCK_STREAM, 0, fds) != 0)
            {
                throw new InvalidOperationException("socketpair failed.");
            }

            serverFd = fds[0];
            clientFd = fds[1];
        }

        Dam.Host.Display.CreateClient(serverFd);
        var client = new ShmTestClient(clientFd);
        _clients.Add(client);
        client.BindGlobals(() => PumpToClient(client));
        return client;
    }

    public void PumpToServer()
    {
        foreach (var client in _clients)
        {
            client.Display.Flush();
        }

        Dam.Host.Loop.Dispatch(0);
    }

    public void PumpToClient() => PumpToClient(null);

    private void PumpToClient(ShmTestClient? only)
    {
        PumpToServer();
        Dam.Host.Display.FlushClients();

        foreach (var client in _clients)
        {
            if (only is not null && client != only)
            {
                continue;
            }

            while (SocketReadable(client))
            {
                client.Display.Dispatch();
            }

            client.Display.DispatchPending();
        }
    }

    public void PumpUntil(Func<bool> condition, int rounds = 20)
    {
        for (var i = 0; i < rounds && !condition(); i++)
        {
            PumpToClient();
        }

        if (!condition())
        {
            throw new TimeoutException("condition not reached while pumping");
        }
    }

    private static bool SocketReadable(ShmTestClient client)
    {
        unsafe
        {
            var pollFd = new PollFd { Fd = client.Display.Fd, Events = 1 };
            return poll(&pollFd, 1, 0) > 0 && (pollFd.REvents & 1) != 0;
        }
    }

    public void Dispose()
    {
        foreach (var client in _clients)
        {
            client.Dispose();
        }

        Dam.Host.Loop.Dispatch(0);
        Dam.Host.Loop.Dispatch(0);
        Dam.Dispose();

        if (BasinCounters.Enabled)
        {
            if (BasinCounters.LiveObjects != 0)
            {
                throw new InvalidOperationException(
                    $"{BasinCounters.LiveObjects} objects still live at teardown.{Environment.NewLine}{BasinCounters.CensusReport()}");
            }

            if (BasinCounters.PendingFrees != 0)
            {
                throw new InvalidOperationException(
                    $"{BasinCounters.PendingFrees} deferred frees still pending at teardown.");
            }
        }
    }
}
