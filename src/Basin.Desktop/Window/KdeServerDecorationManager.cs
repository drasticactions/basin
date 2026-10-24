using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Pixman;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

/// <summary>
/// Serves the <c>org_kde_kwin_server_decoration_manager</c> global. It grants each mode that a client asks for and records it per surface.
/// </summary>
public sealed class KdeServerDecorationManager : IDisposable
{
    /// <summary>
    /// The protocol version that the global advertises.
    /// </summary>
    public const int Version = 1;

    /// <summary>
    /// Lists the decoration modes of the protocol, with the protocol's values.
    /// </summary>
    public enum DecorationMode : uint
    {
        /// <summary>
        /// No decorations at all.
        /// </summary>
        None = 0,
        /// <summary>
        /// The client draws its own decorations.
        /// </summary>
        Client = 1,
        /// <summary>
        /// The compositor draws the decorations.
        /// </summary>
        Server = 2,
    }

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly DecorationMode _defaultMode;
    private readonly Dictionary<Surface, DecorationMode> _modes = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="KdeServerDecorationManager"/> class and creates the global.
    /// </summary>
    /// <param name="display">The display that holds the global.</param>
    /// <param name="compositor">The compositor global that resolves surfaces.</param>
    /// <param name="defaultMode">The mode to advertise, and to give each new decoration object.</param>
    public KdeServerDecorationManager(WlServerDisplay display, CompositorGlobal compositor, DecorationMode defaultMode = DecorationMode.Server)
    {
        _compositor = compositor;
        _defaultMode = defaultMode;
        _global = display.CreateGlobal(OrgKdeKwinServerDecorationManager.Interface, Version, OnBind);
    }

    /// <summary>
    /// Occurs when a client asks for a mode, after the manager grants it. It does not occur for the default mode of a new decoration object.
    /// </summary>
    public event Action<Surface, DecorationMode>? ModeRequested;

    /// <summary>
    /// Removes the global. Bound clients keep their resources.
    /// </summary>
    public void Dispose() => _global.Dispose();

    /// <summary>
    /// Gets the mode that the surface has now.
    /// </summary>
    /// <param name="surface">The surface to look up.</param>
    /// <returns>The mode, or <see cref="DecorationMode.Client"/> when the surface has no decoration object.</returns>
    public DecorationMode ModeOf(Surface surface) => _modes.GetValueOrDefault(surface, DecorationMode.Client);

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new OrgKdeKwinServerDecorationManagerResource(client, version, id);
        manager.SendDefaultMode((uint)_defaultMode);
        manager.Create += (_, e) =>
        {
            var decoration = new OrgKdeKwinServerDecorationResource(client, manager.Version, e.Id);
            var surface = _compositor.ResolveSurface(e.Surface);
            decoration.SendMode((uint)_defaultMode);
            if (surface is not null)
            {
                _modes[surface] = _defaultMode;
                surface.Destroyed += () => _modes.Remove(surface);
            }

            decoration.RequestMode += (_, me) =>
            {
                decoration.SendMode((uint)me.Mode);
                if (surface is not null)
                {
                    _modes[surface] = (DecorationMode)me.Mode;
                    ModeRequested?.Invoke(surface, (DecorationMode)me.Mode);
                }
            };
            decoration.Destroyed += (_, _) =>
            {
                if (surface is not null)
                {
                    _modes.Remove(surface);
                }
            };
        };
    }
}
