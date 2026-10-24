using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

/// <summary>
/// Serves the <c>wp_color_representation_manager_v1</c> global. It tracks the alpha mode, YUV coefficients, range and chroma location that each surface commits.
/// </summary>
public sealed class ColorRepresentationManager : IDisposable, IWpColorRepresentationManagerV1Handler
{
    /// <summary>
    /// The protocol version that the global advertises.
    /// </summary>
    public const int Version = 1;

    private const uint ErrorSurfaceExists = 1;
    private const uint ErrorInert = 4;

    /// <summary>
    /// Holds the color representation of one surface.
    /// </summary>
    /// <param name="AlphaMode">How the alpha channel relates to the color channels.</param>
    /// <param name="Coefficients">The matrix coefficients that convert YUV to RGB.</param>
    /// <param name="Range">The quantization range of the YUV values.</param>
    /// <param name="ChromaLocation">The chroma sample location, or <see langword="null"/> when the client did not set one.</param>
    public readonly record struct Representation(
        WpColorRepresentationSurfaceV1.AlphaMode AlphaMode,
        WpColorRepresentationSurfaceV1.Coefficients Coefficients,
        WpColorRepresentationSurfaceV1.Range Range,
        WpColorRepresentationSurfaceV1.ChromaLocation? ChromaLocation)
    {
        /// <summary>
        /// Gets the representation of a surface without state: premultiplied electrical alpha, identity coefficients, full range and no chroma location.
        /// </summary>
        public static Representation Default { get; } = new(
            WpColorRepresentationSurfaceV1.AlphaMode.PremultipliedElectrical,
            WpColorRepresentationSurfaceV1.Coefficients.Identity,
            WpColorRepresentationSurfaceV1.Range.Full,
            null);
    }

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly Dictionary<Surface, SurfaceRepresentation> _surfaces = [];
    private readonly HashSet<Surface> _claimed = [];

    private readonly Basin.Capabilities.Defaults.SurfaceHints? _hints;

    /// <summary>
    /// Initializes a new instance of the <see cref="ColorRepresentationManager"/> class and creates the global.
    /// </summary>
    /// <param name="display">The display that holds the global.</param>
    /// <param name="compositor">The compositor global whose surfaces get representation state.</param>
    /// <param name="hints">The surface hints that receive the YUV encoding of each surface, or <see langword="null"/>.</param>
    public ColorRepresentationManager(
        WlServerDisplay display, CompositorGlobal compositor, Basin.Capabilities.Defaults.SurfaceHints? hints = null)
    {
        _compositor = compositor;
        _hints = hints;
        _global = display.CreateGlobal(WpColorRepresentationManagerV1.Interface, Version, OnBind);
    }

    /// <summary>
    /// Occurs when a surface commit applies a different representation. The argument is the new representation.
    /// </summary>
    public event Action<Surface, Representation>? RepresentationChanged;

    /// <summary>
    /// Removes the global. Bound clients keep their resources.
    /// </summary>
    public void Dispose() => _global.Dispose();

    /// <summary>
    /// Gets the representation that the surface's last applied commit set.
    /// </summary>
    /// <param name="surface">The surface to look up.</param>
    /// <returns>The current representation, or <see cref="Representation.Default"/> when the surface has none.</returns>
    public Representation RepresentationOf(Surface surface) =>
        _surfaces.TryGetValue(surface, out var state) ? state.Current : Representation.Default;

    private sealed class SurfaceRepresentation
    {
        public Representation Current = Representation.Default;
        public bool HasPending;
        public Representation Pending = Representation.Default;
        public bool HasLatched;
        public Representation Latched = Representation.Default;
    }

    private SurfaceRepresentation StateFor(Surface surface)
    {
        if (_surfaces.TryGetValue(surface, out var state))
        {
            return state;
        }

        state = new SurfaceRepresentation();
        _surfaces[surface] = state;

        surface.CommitRequested += () => Latch(state);
        surface.Committed += () => ApplyLatched(surface, state);
        surface.Destroyed += () =>
        {
            _claimed.Remove(surface);
            _surfaces.Remove(surface);
            _hints?.SetYuvEncoding(surface, null);
        };
        return state;
    }

    private static void Latch(SurfaceRepresentation state)
    {
        if (!state.HasPending)
        {
            return;
        }

        state.HasPending = false;
        state.HasLatched = true;
        state.Latched = state.Pending;
    }

    private void ApplyLatched(Surface surface, SurfaceRepresentation state)
    {
        if (!state.HasLatched)
        {
            return;
        }

        state.HasLatched = false;
        var changed = state.Current != state.Latched;
        state.Current = state.Latched;
        if (changed)
        {
            _hints?.SetYuvEncoding(surface, YuvEncodingOf(state.Current));
            RepresentationChanged?.Invoke(surface, state.Current);
        }
    }

    /// <summary>
    /// Maps a representation to the YUV encoding that the renderer understands.
    /// </summary>
    /// <param name="representation">The representation to map.</param>
    /// <returns>The coefficients and range, or <see langword="null"/> for identity or other coefficients that are not BT.601, BT.709 or BT.2020.</returns>
    public static (YuvCoefficients Coefficients, YuvRange Range)? YuvEncodingOf(Representation representation)
    {
        YuvCoefficients? coefficients = representation.Coefficients switch
        {
            WpColorRepresentationSurfaceV1.Coefficients.Bt601 => YuvCoefficients.Bt601,
            WpColorRepresentationSurfaceV1.Coefficients.Bt709 => YuvCoefficients.Bt709,
            WpColorRepresentationSurfaceV1.Coefficients.Bt2020 => YuvCoefficients.Bt2020,
            _ => null,
        };
        return coefficients is { } value
            ? (value, representation.Range == WpColorRepresentationSurfaceV1.Range.Full ? YuvRange.Full : YuvRange.Limited)
            : null;
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new WpColorRepresentationManagerV1Resource(client, version, id, this);
        manager.SendSupportedAlphaMode(WpColorRepresentationSurfaceV1.AlphaMode.PremultipliedElectrical);
        manager.SendSupportedAlphaMode(WpColorRepresentationSurfaceV1.AlphaMode.Straight);
        foreach (var (coefficients, range) in (ReadOnlySpan<(WpColorRepresentationSurfaceV1.Coefficients, WpColorRepresentationSurfaceV1.Range)>)
            [(WpColorRepresentationSurfaceV1.Coefficients.Identity, WpColorRepresentationSurfaceV1.Range.Full),
             (WpColorRepresentationSurfaceV1.Coefficients.Bt709, WpColorRepresentationSurfaceV1.Range.Limited),
             (WpColorRepresentationSurfaceV1.Coefficients.Bt709, WpColorRepresentationSurfaceV1.Range.Full),
             (WpColorRepresentationSurfaceV1.Coefficients.Bt601, WpColorRepresentationSurfaceV1.Range.Limited),
             (WpColorRepresentationSurfaceV1.Coefficients.Bt2020, WpColorRepresentationSurfaceV1.Range.Limited)])
        {
            manager.SendSupportedCoefficientsAndRanges(coefficients, range);
        }

        manager.SendDone();
    }

    void IWpColorRepresentationManagerV1Handler.Destroy(WpColorRepresentationManagerV1Resource resource)
    {
    }

    void IWpColorRepresentationManagerV1Handler.GetSurface(
        WpColorRepresentationManagerV1Resource resource,
        in WpColorRepresentationManagerV1Resource.GetSurfaceEventArgs args)
    {
        var handler = new SurfaceRepresentationObject(this, resource.Client, resource.Version, args.Id);
        var surface = _compositor.ResolveSurface(args.Surface);
        if (surface is null)
        {
            return;
        }

        if (!_claimed.Add(surface))
        {
            resource.PostError(ErrorSurfaceExists, "The surface already has a color representation object.");
            return;
        }

        handler.Attach(surface, StateFor(surface));
    }

    private sealed class SurfaceRepresentationObject : IWpColorRepresentationSurfaceV1Handler
    {
        private readonly ColorRepresentationManager _manager;
        private Surface? _surface;
        private SurfaceRepresentation? _state;
        private bool _inert;

        public SurfaceRepresentationObject(ColorRepresentationManager manager, WlClient client, uint version, uint id)
        {
            _manager = manager;
            Resource = new WpColorRepresentationSurfaceV1Resource(client, version, id, this);
        }

        public WpColorRepresentationSurfaceV1Resource Resource { get; }

        public void Attach(Surface surface, SurfaceRepresentation state)
        {
            _surface = surface;
            _state = state;
            surface.Destroyed += OnSurfaceDestroyed;
            Resource.Destroyed += OnResourceDestroyed;
        }

        void IWpColorRepresentationSurfaceV1Handler.Destroy(WpColorRepresentationSurfaceV1Resource resource)
        {
        }

        void IWpColorRepresentationSurfaceV1Handler.SetAlphaMode(
            WpColorRepresentationSurfaceV1Resource resource,
            in WpColorRepresentationSurfaceV1Resource.SetAlphaModeEventArgs args)
        {
            if (_state is not { } state || Basis(state) is not { } basis)
            {
                return;
            }

            state.Pending = basis with { AlphaMode = args.AlphaMode };
            state.HasPending = true;
        }

        void IWpColorRepresentationSurfaceV1Handler.SetCoefficientsAndRange(
            WpColorRepresentationSurfaceV1Resource resource,
            in WpColorRepresentationSurfaceV1Resource.SetCoefficientsAndRangeEventArgs args)
        {
            if (_state is not { } state || Basis(state) is not { } basis)
            {
                return;
            }

            state.Pending = basis with { Coefficients = args.Coefficients, Range = args.Range };
            state.HasPending = true;
        }

        void IWpColorRepresentationSurfaceV1Handler.SetChromaLocation(
            WpColorRepresentationSurfaceV1Resource resource,
            in WpColorRepresentationSurfaceV1Resource.SetChromaLocationEventArgs args)
        {
            if (_state is not { } state || Basis(state) is not { } basis)
            {
                return;
            }

            state.Pending = basis with { ChromaLocation = args.ChromaLocation };
            state.HasPending = true;
        }

        private Representation? Basis(SurfaceRepresentation state)
        {
            if (_inert)
            {
                Resource.PostError(ErrorInert, "The surface of this object is destroyed.");
                return null;
            }

            return state.HasPending ? state.Pending : state.HasLatched ? state.Latched : state.Current;
        }

        private void OnSurfaceDestroyed() => _inert = true;

        private void OnResourceDestroyed(object? sender, EventArgs e)
        {
            _manager._claimed.Remove(_surface!);
            if (_inert)
            {
                return;
            }

            var state = _state!;
            state.Pending = Representation.Default;
            state.HasPending = true;
        }
    }
}
