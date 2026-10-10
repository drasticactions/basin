using System.Runtime.Versioning;

namespace Basin.Transport.Waypipe;

public static class WaypipeListeners
{
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
                WaypipeLog.Log.Debug($"--hyperv-vm {ignored} is ignored off Windows");
            }

            return Task.FromResult<IWaypipeListener>(listener);
        }

        if (OperatingSystem.IsWindows())
        {
            ArgumentNullException.ThrowIfNull(host.Post);
            if (WaypipeEndpoint.ForListening(endpoint) is not VsockEndPoint vsock)
            {
                throw new PlatformNotSupportedException(
                    "on Windows a listener takes vsock:PORT; a TCP or unix listener needs descriptors");
            }

            return BindHyperVAsync(vsock, vm ?? HyperVVm.Wsl, host.Post, compression, limits, options, cancellation);
        }

        throw new PlatformNotSupportedException(
            "this host binds no listening socket; attach a connected stream with AttachChannel instead");
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
