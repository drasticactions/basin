using System.Runtime.Versioning;

namespace Basin.Transport.Waypipe;

/// <summary>
/// Selects and binds the right <see cref="IWaypipeListener"/> for the platform.
/// </summary>
public static class WaypipeListeners
{
    /// <summary>
    /// Binds a listener for an endpoint string. A host with descriptors gets a <see cref="WaypipeListener"/> and a completed task. Windows gets a <see cref="HyperVWaypipeListener"/>.
    /// </summary>
    /// <param name="endpoint">The endpoint as <see cref="WaypipeEndpoint.ForListening"/> reads it. On Windows it must be "vsock:PORT".</param>
    /// <param name="host">The loop and the post delegate. The bind uses the one that the platform needs.</param>
    /// <param name="vm">The Hyper-V VM to bind on Windows, or <see langword="null"/> for <see cref="HyperVVm.Wsl"/>. Other platforms ignore it.</param>
    /// <param name="compression">The compression that every channel expects from its peer.</param>
    /// <param name="limits">The limits for each channel, or <see langword="null"/> for the defaults.</param>
    /// <param name="options">The content that each channel accepts, or <see langword="null"/> for the defaults.</param>
    /// <param name="cancellation">Cancels a Hyper-V bind that has not completed.</param>
    /// <returns>A task that completes with the bound listener, which the caller owns.</returns>
    /// <exception cref="PlatformNotSupportedException">The platform binds no listening socket, or Windows got an endpoint other than vsock.</exception>
    public static Task<IWaypipeListener> BindAsync(
        string endpoint,
        WaypipeListenerHost host,
        HyperVVm? vm = null,
        WaypipeCompression compression = WaypipeCompression.Lz4,
        WaypipeLimits? limits = null,
        WaypipeChannelOptions? options = null,
        CancellationToken cancellation = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        if (PlatformFacts.HasDescriptors)
        {
            ArgumentNullException.ThrowIfNull(host.Loop);
#pragma warning disable CA1416
            var listener = WaypipeListener.Bind(host.Loop, WaypipeEndpoint.ForListening(endpoint), compression, limits, options);
#pragma warning restore CA1416
            if (vm is { } ignored)
            {
                WaypipeLog.Log.Debug($"--hyperv-vm {ignored} ignored, it applies only on Windows");
            }

            return Task.FromResult<IWaypipeListener>(listener);
        }

        if (OperatingSystem.IsWindows())
        {
            ArgumentNullException.ThrowIfNull(host.Post);
            if (WaypipeEndpoint.ForListening(endpoint) is not VsockEndPoint vsock)
            {
                throw new PlatformNotSupportedException(
                    "A TCP or unix listener needs file descriptors, which Windows does not have. Give the listener as vsock:PORT.");
            }

            return BindHyperVAsync(vsock, vm ?? HyperVVm.Wsl, host.Post, compression, limits, options, cancellation);
        }

        throw new PlatformNotSupportedException(
            "This host cannot bind a listening socket. Attach a connected stream with AttachChannel instead.");
    }

    [SupportedOSPlatform("windows")]
    private static async Task<IWaypipeListener> BindHyperVAsync(
        VsockEndPoint endpoint,
        HyperVVm vm,
        Action<Action> post,
        WaypipeCompression compression,
        WaypipeLimits? limits,
        WaypipeChannelOptions? options,
        CancellationToken cancellation) =>
        await HyperVWaypipeListener.BindAsync(endpoint, vm, post, compression, limits, options, cancellation).ConfigureAwait(false);
}
