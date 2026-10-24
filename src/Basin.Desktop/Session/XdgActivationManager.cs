using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

/// <summary>
/// Serves the <c>xdg_activation_v1</c> global. It mints tokens and redeems them through <see cref="IActivationTokens"/>.
/// </summary>
public sealed class XdgActivationManager : IDisposable, IXdgActivationV1Handler
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

    private void OnBind(WlClient client, uint version, uint id) =>
        _ = new XdgActivationV1Resource(client, version, id, this);

    void IXdgActivationV1Handler.Destroy(XdgActivationV1Resource resource)
    {
    }

    void IXdgActivationV1Handler.GetActivationToken(XdgActivationV1Resource resource, in XdgActivationV1Resource.GetActivationTokenEventArgs args) =>
        _ = new TokenRequest(this, resource.Client, resource.Version, args.Id);

    void IXdgActivationV1Handler.Activate(XdgActivationV1Resource resource, in XdgActivationV1Resource.ActivateEventArgs args)
    {
        var surface = _compositor.ResolveSurface(args.Surface);
        if (surface is not null && _tokens is { } store && store.Redeem(args.Token, surface))
        {
            ActivationRequested?.Invoke(surface);
        }
    }

    private sealed class TokenRequest : IXdgActivationTokenV1Handler
    {
        private readonly XdgActivationManager _owner;
        private Surface? _requesting;
        private uint _serial;
        private string? _appId;

        public TokenRequest(XdgActivationManager owner, WlClient client, uint version, uint id)
        {
            _owner = owner;
            _ = new XdgActivationTokenV1Resource(client, version, id, this);
        }

        void IXdgActivationTokenV1Handler.SetSurface(XdgActivationTokenV1Resource resource, in XdgActivationTokenV1Resource.SetSurfaceEventArgs args) =>
            _requesting = _owner._compositor.ResolveSurface(args.Surface);

        void IXdgActivationTokenV1Handler.SetSerial(XdgActivationTokenV1Resource resource, in XdgActivationTokenV1Resource.SetSerialEventArgs args) =>
            _serial = args.Serial;

        void IXdgActivationTokenV1Handler.SetAppId(XdgActivationTokenV1Resource resource, in XdgActivationTokenV1Resource.SetAppIdEventArgs args) =>
            _appId = args.AppId;

        void IXdgActivationTokenV1Handler.Commit(XdgActivationTokenV1Resource resource)
        {
            var token = _owner._tokens?.Mint(resource.Client, _requesting, _serial, _appId) ?? string.Empty;
            resource.SendDone(token);
        }

        void IXdgActivationTokenV1Handler.Destroy(XdgActivationTokenV1Resource resource)
        {
        }
    }
}
