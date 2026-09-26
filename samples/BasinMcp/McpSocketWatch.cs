namespace BasinMcp;

internal static class McpSocketWatch
{
    public static async Task RunAsync(McpBridge bridge, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(bridge.Options.PollInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                if (bridge.Client is not { IsConnected: true } && (bridge.Options.Reconnect || !bridge.HasConnected))
                {
                    _ = await bridge.TryConnectAsync(cancellationToken).ConfigureAwait(false);
                }

                await bridge.EnsureRelayAsync(null, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}
