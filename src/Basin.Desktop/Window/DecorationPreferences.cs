using Basin.Shell.Xdg;

namespace Basin.Desktop;

/// <summary>
/// Collects each surface's decoration preference from xdg-decoration, KDE server decoration and the no-border request of xdg toplevels. The latest request wins.
/// </summary>
public sealed class DecorationPreferences
{
    private readonly Dictionary<Surface, bool> _serverSide = [];
    private readonly XdgDecorationManager? _xdg;
    private readonly KdeServerDecorationManager? _kde;

    /// <summary>
    /// Initializes a new instance of the <see cref="DecorationPreferences"/> class and follows each source that is not <see langword="null"/>.
    /// </summary>
    /// <param name="xdg">The xdg-decoration manager, or <see langword="null"/>.</param>
    /// <param name="kde">The KDE server decoration manager, or <see langword="null"/>.</param>
    /// <param name="toplevels">The toplevel source whose no-border requests count, or <see langword="null"/>.</param>
    public DecorationPreferences(
        XdgDecorationManager? xdg,
        KdeServerDecorationManager? kde = null,
        XdgToplevelSource? toplevels = null)
    {
        _xdg = xdg;
        _kde = kde;
        if (xdg is not null)
        {
            xdg.ModeChanged += (toplevel, mode) => Record(toplevel.Surface, mode == DecorationMode.ServerSide);
        }

        if (kde is not null)
        {
            kde.ModeRequested += (surface, mode) =>
                Record(surface, mode == KdeServerDecorationManager.DecorationMode.Server);
        }

        if (toplevels is not null)
        {
            toplevels.NoBorderRequested += (toplevel, noBorder) => Record(toplevel.Surface, !noBorder);
        }
    }

    /// <summary>
    /// Occurs each time a preference is recorded, also when the value is the same. The argument is <see langword="true"/> for server-side decorations.
    /// </summary>
    public event Action<Surface, bool>? Changed;

    /// <summary>
    /// Records a preference for a surface until the surface is destroyed, and raises <see cref="Changed"/>.
    /// </summary>
    /// <param name="surface">The surface.</param>
    /// <param name="serverSide"><see langword="true"/> when the surface wants server-side decorations.</param>
    /// <exception cref="ArgumentNullException"><paramref name="surface"/> is <see langword="null"/>.</exception>
    public void Record(Surface surface, bool serverSide)
    {
        ArgumentNullException.ThrowIfNull(surface);
        if (_serverSide.TryAdd(surface, serverSide))
        {
            surface.Destroyed += () => _serverSide.Remove(surface);
        }
        else
        {
            _serverSide[surface] = serverSide;
        }

        Changed?.Invoke(surface, serverSide);
    }

    /// <summary>
    /// Gets the last recorded preference of a surface.
    /// </summary>
    /// <param name="surface">The surface to look up.</param>
    /// <returns><see langword="true"/> for server-side, <see langword="false"/> for client-side, or <see langword="null"/> when nothing is recorded.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="surface"/> is <see langword="null"/>.</exception>
    public bool? Preference(Surface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        return _serverSide.TryGetValue(surface, out var serverSide) ? serverSide : null;
    }

    /// <summary>
    /// Decides whether a toplevel gets server-side decorations. Without a recorded preference, the current KDE or xdg mode decides.
    /// </summary>
    /// <param name="toplevel">The toplevel to test.</param>
    /// <returns><see langword="true"/> when the compositor must draw the decorations.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="toplevel"/> is <see langword="null"/>.</exception>
    public bool IsServerSide(XdgToplevelWindow toplevel)
    {
        ArgumentNullException.ThrowIfNull(toplevel);
        return Preference(toplevel.Surface)
            ?? (_kde?.ModeOf(toplevel.Surface) == KdeServerDecorationManager.DecorationMode.Server ||
                _xdg?.ModeOf(toplevel) == DecorationMode.ServerSide);
    }
}
