using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Pixman;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

/// <summary>
/// Serves the <c>wp_alpha_modifier_v1</c> global. It applies the committed alpha factor of each surface through <see cref="ISurfaceAppearance"/>.
/// </summary>
public sealed class AlphaModifierManager : IDisposable, IWpAlphaModifierV1Handler
{
    /// <summary>
    /// The protocol version that the global advertises.
    /// </summary>
    public const int Version = 1;

    private const uint ErrorAlreadyConstructed = 0;
    private const uint ErrorNoSurface = 0;

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly ISurfaceAppearance _appearance;
    private readonly HashSet<Surface> _claimed = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="AlphaModifierManager"/> class and creates the global.
    /// </summary>
    /// <param name="display">The display that holds the global.</param>
    /// <param name="compositor">The compositor global that resolves surfaces.</param>
    /// <param name="appearance">The appearance store that receives the opacity.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public AlphaModifierManager(WlServerDisplay display, CompositorGlobal compositor, ISurfaceAppearance appearance)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(compositor);
        ArgumentNullException.ThrowIfNull(appearance);
        _compositor = compositor;
        _appearance = appearance;
        _global = display.CreateGlobal(WpAlphaModifierV1.Interface, Version, OnBind);
    }

    /// <summary>
    /// Occurs when a commit applies a new alpha factor, and with 1 when the client removes it. The factor goes from 0 to 1.
    /// </summary>
    public event Action<Surface, double>? AlphaChanged;

    /// <summary>
    /// Gets the appearance store that receives the opacity.
    /// </summary>
    public ISurfaceAppearance Appearance => _appearance;

    /// <summary>
    /// Removes the global. Bound clients keep their resources.
    /// </summary>
    public void Dispose() => _global.Dispose();

    /// <summary>
    /// Gets the opacity of a surface from the appearance store.
    /// </summary>
    /// <param name="surface">The surface to look up.</param>
    /// <returns>The opacity from 0 to 1.</returns>
    public double AlphaOf(Surface surface) => _appearance.OpacityOf(surface);

    private void OnBind(WlClient client, uint version, uint id) =>
        _ = new WpAlphaModifierV1Resource(client, version, id, this);

    void IWpAlphaModifierV1Handler.GetSurface(WpAlphaModifierV1Resource resource, in WpAlphaModifierV1Resource.GetSurfaceEventArgs args)
    {
        var surface = _compositor.ResolveSurface(args.Surface);
        if (surface is null)
        {
            _ = new WpAlphaModifierSurfaceV1Resource(resource.Client, resource.Version, args.Id, IgnoredSurface.Instance);
            return;
        }

        if (!_claimed.Add(surface))
        {
            _ = new WpAlphaModifierSurfaceV1Resource(resource.Client, resource.Version, args.Id, IgnoredSurface.Instance);
            resource.PostError(ErrorAlreadyConstructed, "The surface already has an alpha modifier.");
            return;
        }

        _ = new AlphaSurface(this, resource.Client, resource.Version, args.Id, surface);
    }

    void IWpAlphaModifierV1Handler.Destroy(WpAlphaModifierV1Resource resource)
    {
    }

    private sealed class AlphaSurface : IWpAlphaModifierSurfaceV1Handler
    {
        private readonly AlphaModifierManager _owner;
        private readonly Surface _surface;
        private readonly Action _committed;
        private bool _alive = true;

        public AlphaSurface(AlphaModifierManager owner, WlClient client, uint version, uint id, Surface surface)
        {
            _owner = owner;
            _surface = surface;
            var resource = new WpAlphaModifierSurfaceV1Resource(client, version, id, this);
            _committed = OnCommitted;
            surface.Committed += _committed;
            resource.Destroyed += OnResourceDestroyed;
            surface.Destroyed += OnSurfaceDestroyed;
        }

        void IWpAlphaModifierSurfaceV1Handler.SetMultiplier(WpAlphaModifierSurfaceV1Resource resource, in WpAlphaModifierSurfaceV1Resource.SetMultiplierEventArgs args)
        {
            if (!_alive)
            {
                resource.PostError(ErrorNoSurface, "The wl_surface is destroyed.");
                return;
            }

            _surface.Pending.SetExtension(new PendingAlpha(args.Factor / (double)uint.MaxValue));
        }

        void IWpAlphaModifierSurfaceV1Handler.Destroy(WpAlphaModifierSurfaceV1Resource resource)
        {
        }

        private void OnCommitted()
        {
            if (_surface.Current.TakeExtension<PendingAlpha>() is { } pending)
            {
                _owner._appearance.SetOpacity(_surface, pending.Alpha);
                _owner.AlphaChanged?.Invoke(_surface, pending.Alpha);
            }
        }

        private void OnResourceDestroyed(object? sender, EventArgs e)
        {
            _owner._claimed.Remove(_surface);
            if (_alive)
            {
                _surface.Pending.SetExtension(new PendingAlpha(1.0));
            }
        }

        private void OnSurfaceDestroyed()
        {
            _alive = false;
            _owner._claimed.Remove(_surface);
            _surface.Committed -= _committed;
        }
    }

    private sealed class IgnoredSurface : IWpAlphaModifierSurfaceV1Handler
    {
        public static readonly IgnoredSurface Instance = new();

        void IWpAlphaModifierSurfaceV1Handler.SetMultiplier(WpAlphaModifierSurfaceV1Resource resource, in WpAlphaModifierSurfaceV1Resource.SetMultiplierEventArgs args)
        {
        }

        void IWpAlphaModifierSurfaceV1Handler.Destroy(WpAlphaModifierSurfaceV1Resource resource)
        {
        }
    }

    private sealed class PendingAlpha(double alpha) : IDisposable
    {
        public double Alpha { get; } = alpha;

        public void Dispose()
        {
        }
    }
}
