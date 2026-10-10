using System.Net.Sockets;

namespace Basin.Transport.Waypipe.Windows.Tests;

internal static class HyperVFacts
{
    private static readonly Lazy<string?> Loopback = new(Probe);

    public static string? LoopbackReason() => Loopback.Value;

    public static uint FreshPort() => (uint)Random.Shared.Next(0x10000, 0x7FFF0000);

    public static Socket Connect(HyperVEndPoint endpoint)
    {
        var socket = new Socket(HyperVEndPoint.HyperVFamily, SocketType.Stream, HyperVEndPoint.RawProtocol);
        try
        {
            socket.Connect(endpoint);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static string? Probe()
    {
        if (!OperatingSystem.IsWindows())
        {
            return "Hyper-V sockets are Windows only";
        }

        try
        {
            using var listener = new Socket(HyperVEndPoint.HyperVFamily, SocketType.Stream, HyperVEndPoint.RawProtocol);
            listener.Bind(HyperVEndPoint.ForVsockPort(HyperVEndPoint.Loopback, FreshPort()));
            listener.Listen(1);
            return null;
        }
        catch (SocketException ex) when (ex.SocketErrorCode is SocketError.AddressFamilyNotSupported or SocketError.ProtocolNotSupported)
        {
            return $"AF_HYPERV is not available here ({ex.SocketErrorCode}); it needs the Hyper-V or WSL platform";
        }
        catch (SocketException ex)
        {
            return $"a loopback Hyper-V bind failed: {ex.SocketErrorCode}";
        }
    }
}
