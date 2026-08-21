using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

public sealed class XdgActivationManager : IDisposable
{
    public const int Version = 1;

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly IActivationTokens? _tokens;

    public XdgActivationManager(WlServerDisplay display, CompositorGlobal compositor, IActivationTokens? tokens)
    {
        ArgumentNullException.ThrowIfNull(display);
        _compositor = compositor;
        _tokens = tokens;
        _global = display.CreateGlobal(XdgActivationV1.Interface, Version, OnBind);
    }

    public event Action<Surface>? ActivationRequested;

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
