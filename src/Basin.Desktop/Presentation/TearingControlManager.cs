using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

public sealed class TearingControlManager : IDisposable
{
    public const int Version = 1;

    private const uint ErrorExists = 0;

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly Dictionary<Surface, uint> _hints = [];
    private readonly HashSet<Surface> _claimed = [];

    private readonly Basin.Capabilities.Defaults.SurfaceHints? _store;

    public TearingControlManager(
        WlServerDisplay display, CompositorGlobal compositor, Basin.Capabilities.Defaults.SurfaceHints? hints = null)
    {
        _compositor = compositor;
        _store = hints;
        _global = display.CreateGlobal(WpTearingControlManagerV1.Interface, Version, OnBind);
    }

    public event Action<Surface, bool>? HintChanged;

    public void Dispose() => _global.Dispose();

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
