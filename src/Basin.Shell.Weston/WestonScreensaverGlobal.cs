using Basin.Shell.Weston.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Shell.Weston;

public sealed class WestonScreensaverGlobal : IDisposable
{
    public const int Version = 1;

    private const uint ErrorInvalidArgument = 0;

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly IShellRoles _roles;
    private WestonScreensaverResource? _bound;

    public WestonScreensaverGlobal(WlServerDisplay display, CompositorGlobal compositor, IShellRoles roles)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(compositor);
        ArgumentNullException.ThrowIfNull(roles);
        _compositor = compositor;
        _roles = roles;
        _global = display.CreateGlobal(WestonScreensaver.Interface, Version, OnBind);
    }

    public void Dispose() => _global.Dispose();

    private void OnBind(WlClient client, uint version, uint id)
    {
        var screensaver = new WestonScreensaverResource(client, version, id);
        if (_bound is { IsDestroyed: false })
        {
            screensaver.PostError(ErrorInvalidArgument, "weston_screensaver is already bound");
            return;
        }

        _bound = screensaver;
        screensaver.Destroyed += (_, _) =>
        {
            if (ReferenceEquals(_bound, screensaver))
            {
                _bound = null;
            }
        };

        screensaver.SetSurface += (_, e) =>
        {
            var output = OutputGlobal.FromResource(e.Output)?.Output;
            var surface = _compositor.ResolveSurface(e.Surface);
            if (output is null || surface is null)
            {
                screensaver.PostError(ErrorInvalidArgument, "unknown output or surface");
                return;
            }

            _roles.SetScreensaverSurface(output, surface);
        };
    }
}
