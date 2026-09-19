using System.Net.Sockets;
using Basin;
using Basin.Transport.Waypipe;

namespace Tarn;

public static class TarnLink
{
    public static string DefaultEndpoint => PlatformFacts.HasDescriptors ? "127.0.0.1:9800" : "ws://127.0.0.1:9801";

    public static bool IsWebSocket(string endpoint) =>
        endpoint.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) ||
        endpoint.StartsWith("wss://", StringComparison.OrdinalIgnoreCase);

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

        throw new PlatformNotSupportedException("this host reaches a session over a WebSocket; give a ws:// or wss:// endpoint");
    }

    private static async Task<Stream> OpenWebSocketAsync(string endpoint, CancellationToken cancellation) =>
        await WebSocketStream.ConnectAsync(new Uri(endpoint), cancellation).ConfigureAwait(false);

    [System.Runtime.Versioning.UnsupportedOSPlatform("browser")]
    private static async Task<Stream> OpenTcpAsync(string endpoint, CancellationToken cancellation)
    {
        var colon = endpoint.LastIndexOf(':');
        if (colon <= 0 || !int.TryParse(endpoint.AsSpan(colon + 1), out var port))
        {
            throw new ArgumentException("a TCP endpoint is host:port", nameof(endpoint));
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
