using System.Net;
using System.Net.Sockets;

namespace Basin.Transport.Waypipe;

/// <summary>
/// Reads the endpoint strings that a waypipe channel listens on or connects to.
/// </summary>
public static class WaypipeEndpoint
{
    private const string VsockPrefix = "vsock:";

    /// <summary>
    /// Tells whether an endpoint is a WebSocket URL.
    /// </summary>
    /// <param name="endpoint">The endpoint text.</param>
    /// <returns><see langword="true"/> when the text starts with "ws://" or "wss://", without regard to case.</returns>
    public static bool IsWebSocket(string endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return endpoint.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) ||
            endpoint.StartsWith("wss://", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads a listening endpoint: "vsock:PORT", an IP "ADDRESS:PORT", or else a unix socket path. A file that already exists at the path is deleted.
    /// </summary>
    /// <param name="endpoint">The endpoint text.</param>
    /// <returns>A <see cref="VsockEndPoint"/>, an <see cref="IPEndPoint"/> or a <see cref="UnixDomainSocketEndPoint"/>.</returns>
    /// <exception cref="ArgumentException">A vsock endpoint names a context id, has no port, or has a port that is not valid.</exception>
    /// <exception cref="FormatException">An IP endpoint does not parse.</exception>
    /// <exception cref="PlatformNotSupportedException">A vsock endpoint on a platform other than Linux and Windows.</exception>
    [System.Runtime.Versioning.UnsupportedOSPlatform("browser")]
    public static EndPoint ForListening(string endpoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        if (endpoint.StartsWith(VsockPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return ParseVsock(endpoint);
        }

        if (endpoint.Contains(':', StringComparison.Ordinal))
        {
            return IPEndPoint.Parse(endpoint);
        }

        if (File.Exists(endpoint))
        {
            File.Delete(endpoint);
        }

        return new UnixDomainSocketEndPoint(endpoint);
    }

    private static VsockEndPoint ParseVsock(string endpoint)
    {
        var port = endpoint.AsSpan(VsockPrefix.Length);
        if (port.Contains(':'))
        {
            throw new ArgumentException(
                OperatingSystem.IsWindows()
                    ? $"'{endpoint}' names a CID. On Windows, a vsock listener takes only the port, as vsock:PORT, and --hyperv-vm selects the VM."
                    : $"'{endpoint}' names a CID. A vsock listener binds every CID and takes only the port, as vsock:PORT.",
                nameof(endpoint));
        }

        if (port.IsEmpty)
        {
            throw new ArgumentException($"'{endpoint}' names no port. Give the listener as vsock:PORT.", nameof(endpoint));
        }

        if (!uint.TryParse(port, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            throw new ArgumentException($"'{endpoint}' has a port that is not a decimal number.", nameof(endpoint));
        }

        if (value == 0 || value == VsockEndPoint.AnyPort)
        {
            throw new ArgumentException($"'{endpoint}' has a port out of range. A vsock port must be 1 through 4294967294.", nameof(endpoint));
        }

        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("vsock channels are available only on Linux, and on Windows as Hyper-V sockets.");
        }

        return new VsockEndPoint(value);
    }

    /// <summary>
    /// Connects to a waypipe peer: a "ws://" or "wss://" URL opens a WebSocket, and "host:port" opens TCP where the host has descriptors.
    /// </summary>
    /// <param name="endpoint">The endpoint text.</param>
    /// <param name="cancellation">Cancels the connection attempt.</param>
    /// <returns>A task that completes with the connected stream, which the caller owns.</returns>
    /// <exception cref="PlatformNotSupportedException">The host has no descriptors and the endpoint is not a WebSocket URL.</exception>
    public static Task<Stream> OpenAsync(string endpoint, CancellationToken cancellation = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        endpoint = endpoint.Trim();
        if (IsWebSocket(endpoint))
        {
            return OpenWebSocketAsync(endpoint, cancellation);
        }

        if (PlatformFacts.HasDescriptors)
        {
#pragma warning disable CA1416
            return OpenTcpAsync(endpoint, cancellation);
#pragma warning restore CA1416
        }

        throw new PlatformNotSupportedException("This host reaches a session only over a WebSocket. Give a ws:// or wss:// endpoint.");
    }

    private static async Task<Stream> OpenWebSocketAsync(string endpoint, CancellationToken cancellation) =>
        await WebSocketStream.ConnectAsync(new Uri(endpoint), cancellation).ConfigureAwait(false);

    [System.Runtime.Versioning.UnsupportedOSPlatform("browser")]
    private static async Task<Stream> OpenTcpAsync(string endpoint, CancellationToken cancellation)
    {
        var colon = endpoint.LastIndexOf(':');
        if (colon <= 0 || !int.TryParse(endpoint.AsSpan(colon + 1), out var port))
        {
            throw new ArgumentException("The TCP endpoint is not valid. Give it as host:port.", nameof(endpoint));
        }

        var client = new TcpClient { NoDelay = true };
        try
        {
            await client.ConnectAsync(endpoint[..colon], port, cancellation).ConfigureAwait(false);
        }
        catch
        {
            client.Dispose();
            throw;
        }

        return new NetworkStream(client.Client, ownsSocket: true);
    }
}
