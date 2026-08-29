using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

public sealed class ColorRepresentationManager : IDisposable
{
    public const int Version = 1;

    private const uint ErrorSurfaceExists = 1;
    private const uint ErrorInert = 4;

    public readonly record struct Representation(
        WpColorRepresentationSurfaceV1.AlphaMode AlphaMode,
        WpColorRepresentationSurfaceV1.Coefficients Coefficients,
        WpColorRepresentationSurfaceV1.Range Range,
        WpColorRepresentationSurfaceV1.ChromaLocation? ChromaLocation)
    {
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

    public ColorRepresentationManager(WlServerDisplay display, CompositorGlobal compositor)
    {
        _compositor = compositor;
        _global = display.CreateGlobal(WpColorRepresentationManagerV1.Interface, Version, OnBind);
    }

    public event Action<Surface, Representation>? RepresentationChanged;

    public void Dispose() => _global.Dispose();

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
            RepresentationChanged?.Invoke(surface, state.Current);
        }
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new WpColorRepresentationManagerV1Resource(client, version, id);
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

        manager.GetSurface += (_, e) =>
        {
            var resource = new WpColorRepresentationSurfaceV1Resource(client, manager.Version, e.Id);
            var surface = _compositor.ResolveSurface(e.Surface);
            if (surface is null)
            {
                return;
            }

            if (!_claimed.Add(surface))
            {
                manager.PostError(ErrorSurfaceExists, "surface already has a color representation object");
                return;
            }

            var state = StateFor(surface);
            var inert = false;
            surface.Destroyed += () => inert = true;

            Representation? Basis()
            {
                if (inert)
                {
                    resource.PostError(ErrorInert, "the surface this object was created for is gone");
                    return null;
                }

                return state.HasPending ? state.Pending : state.HasLatched ? state.Latched : state.Current;
            }

            resource.SetAlphaMode += (_, ae) =>
            {
                if (Basis() is not { } basis)
                {
                    return;
                }

                state.Pending = basis with { AlphaMode = ae.AlphaMode };
                state.HasPending = true;
            };
            resource.SetCoefficientsAndRange += (_, ce) =>
            {
                if (Basis() is not { } basis)
                {
                    return;
                }

                state.Pending = basis with { Coefficients = ce.Coefficients, Range = ce.Range };
                state.HasPending = true;
            };
            resource.SetChromaLocation += (_, le) =>
            {
                if (Basis() is not { } basis)
                {
                    return;
                }

                state.Pending = basis with { ChromaLocation = le.ChromaLocation };
                state.HasPending = true;
            };
            resource.Destroyed += (_, _) =>
            {
                _claimed.Remove(surface);
                if (inert)
                {
                    return;
                }

                state.Pending = Representation.Default;
                state.HasPending = true;
            };
        };
    }
}
