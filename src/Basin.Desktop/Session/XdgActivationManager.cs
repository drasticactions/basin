using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

/// <summary>
/// Serves the <c>xdg_activation_v1</c> global. It mints tokens and redeems them through <see cref="IActivationTokens"/>.
/// </summary>
public sealed class XdgActivationManager : IDisposable
{
    /// <summary>
    /// The protocol version that the global advertises.
    /// </summary>
    public const int Version = 1;

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly IActivationTokens? _tokens;

    /// <summary>
    /// Initializes a new instance of the <see cref="XdgActivationManager"/> class and creates the global.
    /// </summary>
    /// <param name="display">The display that holds the global.</param>
    /// <param name="compositor">The compositor global that resolves surfaces.</param>
    /// <param name="tokens">The token store, or <see langword="null"/> to hand out empty tokens and refuse every activation.</param>
    /// <exception cref="ArgumentNullException"><paramref name="display"/> is <see langword="null"/>.</exception>
    public XdgActivationManager(WlServerDisplay display, CompositorGlobal compositor, IActivationTokens? tokens)
    {
        ArgumentNullException.ThrowIfNull(display);
        _compositor = compositor;
        _tokens = tokens;
        _global = display.CreateGlobal(XdgActivationV1.Interface, Version, OnBind);
    }

    /// <summary>
    /// Occurs when a client activates a surface with a token that the store redeems. The consumer decides whether to focus the surface.
    /// </summary>
    public event Action<Surface>? ActivationRequested;

    /// <summary>
    /// Removes the global. Bound clients keep their resources.
    /// </summary>
    public void Dispose() => _global.Dispose();

    private void OnBind(WlClient client, uint version, uint id)
    {
        var activation = new XdgActivationV1Resource(client, version, id);
        activation.GetActivationToken += (_, e) =>
        {
            var resource = new XdgActivationTokenV1Resource(client, activation.Version, e.Id);
            Surface? requesting = null;
            uint serial = 0;
            string? appId = null;
            resource.SetSurface += (_, se) => requesting = _compositor.ResolveSurface(se.Surface);
            resource.SetSerial += (_, se) => serial = se.Serial;
            resource.SetAppId += (_, ae) => appId = ae.AppId;
            resource.Commit += (_, _) =>
            {
                var token = _tokens?.Mint(client, requesting, serial, appId) ?? string.Empty;
                resource.SendDone(token);
            };
        };
        activation.Activate += (_, e) =>
        {
            var surface = _compositor.ResolveSurface(e.Surface);
            if (surface is not null && _tokens is { } store && store.Redeem(e.Token, surface))
            {
                ActivationRequested?.Invoke(surface);
            }
        };
    }
}
