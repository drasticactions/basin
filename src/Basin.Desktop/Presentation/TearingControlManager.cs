using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

/// <summary>
/// Serves the <c>wp_tearing_control_manager_v1</c> global. It tracks which surfaces prefer async page flips.
/// </summary>
public sealed class TearingControlManager : IDisposable
{
    /// <summary>
    /// The protocol version that the global advertises.
    /// </summary>
    public const int Version = 1;

    private const uint ErrorExists = 0;

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly Dictionary<Surface, uint> _hints = [];
    private readonly HashSet<Surface> _claimed = [];

    private readonly Basin.Capabilities.Defaults.SurfaceHints? _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="TearingControlManager"/> class and creates the global.
    /// </summary>
    /// <param name="display">The display that holds the global.</param>
    /// <param name="compositor">The compositor global whose surfaces get a hint.</param>
    /// <param name="hints">The surface hints that receive each hint, or <see langword="null"/>.</param>
    public TearingControlManager(
        WlServerDisplay display, CompositorGlobal compositor, Basin.Capabilities.Defaults.SurfaceHints? hints = null)
    {
        _compositor = compositor;
        _store = hints;
        _global = display.CreateGlobal(WpTearingControlManagerV1.Interface, Version, OnBind);
    }

    /// <summary>
    /// Occurs when a commit applies a different hint, and with <see langword="false"/> when the client destroys the hint object. The argument is <see langword="true"/> for async.
    /// </summary>
    public event Action<Surface, bool>? HintChanged;

    /// <summary>
    /// Removes the global. Bound clients keep their resources.
    /// </summary>
    public void Dispose() => _global.Dispose();

    /// <summary>
    /// Tests whether the surface's last applied commit asked for async page flips.
    /// </summary>
    /// <param name="surface">The surface to test, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the hint is async, otherwise <see langword="false"/>.</returns>
    public bool PrefersTearing(Surface? surface) =>
        surface is not null && _hints.GetValueOrDefault(surface) == 1;

    private void Apply(Surface surface, uint hint)
    {
        if (_hints.TryGetValue(surface, out var current) && current == hint)
        {
            return;
        }

        _hints[surface] = hint;
        _store?.SetTearing(surface, hint == 1);
        HintChanged?.Invoke(surface, hint == 1);
    }

    private sealed class PendingHint(uint hint) : IDisposable
    {
        public uint Hint { get; } = hint;

        public void Dispose()
        {
        }
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new WpTearingControlManagerV1Resource(client, version, id);
        manager.GetTearingControl += (_, e) =>
        {
            var resource = new WpTearingControlV1Resource(client, manager.Version, e.Id);
            var surface = _compositor.ResolveSurface(e.Surface);
            if (surface is null)
            {
                return;
            }

            if (!_claimed.Add(surface))
            {
                manager.PostError(ErrorExists, "surface already has a tearing control");
                return;
            }

            uint? pending = null;
            resource.SetPresentationHint += (_, he) => pending = (uint)he.Hint;
            Action onCommitRequested = () =>
            {
                if (pending is { } hint)
                {
                    pending = null;
                    surface.Pending.SetExtension(new PendingHint(hint));
                }
            };
            Action onCommitted = () =>
            {
                if (surface.Current.TakeExtension<PendingHint>() is { } carried)
                {
                    using (carried)
                    {
                        Apply(surface, carried.Hint);
                    }
                }
            };
            surface.CommitRequested += onCommitRequested;
            surface.Committed += onCommitted;
            resource.Destroyed += (_, _) =>
            {
                surface.CommitRequested -= onCommitRequested;
                surface.Committed -= onCommitted;
                _claimed.Remove(surface);
                _store?.SetTearing(surface, false);
                if (_hints.Remove(surface))
                {
                    HintChanged?.Invoke(surface, false);
                }
            };
            surface.Destroyed += () =>
            {
                _claimed.Remove(surface);
                _hints.Remove(surface);
                _store?.SetTearing(surface, false);
            };
        };
    }
}
