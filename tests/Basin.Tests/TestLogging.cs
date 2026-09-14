using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Basin.Diagnostics;

namespace Basin.Tests;

internal static class TestLogging
{
    private static readonly StandardErrorLogSink Sink = new();

    [ModuleInitializer]
    internal static void Install()
    {
        BasinLog.Level = Environment.GetEnvironmentVariable("BASIN_TRACE") is null
            ? BasinLogLevel.Warn
            : BasinLogLevel.Debug;
        BasinLog.Sink = Sink;
        WarmStreams();
        WarmSockets();
    }

    internal static void WarmStreams()
    {
        BasinReport.Flush();
        Sink.Flush();
    }

    internal static void WarmSockets()
    {
        if (_socketsWarm)
        {
            return;
        }

        _socketsWarm = true;
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var buffer = new byte[1];
        var pending = socket.ReceiveAsync(buffer, SocketFlags.None);
        socket.SendTo(buffer, socket.LocalEndPoint!);
        pending.Wait(TimeSpan.FromSeconds(5));
    }

    private static bool _socketsWarm;
}
