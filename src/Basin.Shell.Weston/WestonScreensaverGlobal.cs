using Basin.Shell.Weston.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Shell.Weston;

/// <summary>
/// The <c>weston_screensaver</c> global. It forwards the screensaver surface of one bound client to <see cref="IShellRoles.SetScreensaverSurface"/>.
/// </summary>
public sealed class WestonScreensaverGlobal : IDisposable
{
    /// <summary>
    /// The version of <c>weston_screensaver</c> that the global advertises.
    /// </summary>
    public const int Version = 1;

    private const uint ErrorInvalidArgument = 0;

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly IShellRoles _roles;
    private WestonScreensaverResource? _bound;

    /// <summary>
    /// Initializes a new instance of the <see cref="WestonScreensaverGlobal"/> class. It creates the global on <paramref name="display"/> at once. Any client can bind, but only one at a time.
    /// </summary>
    /// <param name="display">The display that advertises the global.</param>
    /// <param name="compositor">The compositor global that resolves the client's surfaces.</param>
    /// <param name="roles">The shell policy that receives the screensaver surfaces.</param>
    public WestonScreensaverGlobal(WlServerDisplay display, CompositorGlobal compositor, IShellRoles roles)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(compositor);
        ArgumentNullException.ThrowIfNull(roles);
        _compositor = compositor;
        _roles = roles;
        _global = display.CreateGlobal(WestonScreensaver.Interface, Version, OnBind);
    }

    /// <summary>
    /// Destroys the global. A client that is bound keeps its resource until it disconnects.
    /// </summary>
    public void Dispose() => _global.Dispose();

    private void OnBind(WlClient client, uint version, uint id)
    {
        var screensaver = new WestonScreensaverResource(client, version, id);
        if (_bound is { IsDestroyed: false })
        {
            screensaver.PostError(ErrorInvalidArgument, "weston_screensaver is already bound.");
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
                screensaver.PostError(ErrorInvalidArgument, "The output or the surface is not known.");
                return;
            }

            _roles.SetScreensaverSurface(output, surface);
        };
    }
}
