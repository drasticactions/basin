using Basin.Shell.Xdg.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Shell.Xdg;

public sealed class XdgDecorationManager : IDisposable
{
    public const int Version = 2;

    private readonly WlGlobal _global;
    private readonly Dictionary<XdgToplevelWindow, Decoration> _decorations = [];
    private readonly Dictionary<XdgToplevelWindow, RetainedMode> _retained = [];
    private readonly Dictionary<XdgToplevelWindow, DecorationMode?> _imposed = [];

    public XdgDecorationManager(WlServerDisplay display)
    {
        _global = display.CreateGlobal(ZxdgDecorationManagerV1.Interface, Version, OnBind);
    }

    public DecorationMode DefaultMode { get; set; } = DecorationMode.ClientSide;

    public Func<XdgToplevelWindow, DecorationMode?, DecorationMode>? ChooseMode { get; set; }

    public event Action<XdgToplevelWindow, DecorationMode>? ModeChanged;

    public event Action<XdgToplevelWindow, DecorationMode?>? PreferenceChanged;

    public DecorationMode ModeOf(XdgToplevelWindow toplevel) =>
        _decorations.TryGetValue(toplevel, out var decoration) ? decoration.Mode : DecorationMode.ClientSide;

    public bool TryGetPreference(XdgToplevelWindow toplevel, out DecorationMode? preference)
    {
        ArgumentNullException.ThrowIfNull(toplevel);
        if (_decorations.TryGetValue(toplevel, out var decoration))
        {
            preference = decoration.Preference;
            return true;
        }

        preference = null;
        return false;
    }

    public void SetMode(XdgToplevelWindow toplevel, DecorationMode? mode)
    {
        ArgumentNullException.ThrowIfNull(toplevel);
        if (!_imposed.ContainsKey(toplevel))
        {
            toplevel.Destroyed += () => _imposed.Remove(toplevel);
        }

        _imposed[toplevel] = mode;

        if (_decorations.TryGetValue(toplevel, out var decoration))
        {
            decoration.Reconsider();
        }
    }

    public void Dispose()
    {
        foreach (var retained in _retained.Values)
        {
            retained.Unsubscribe();
        }

        _retained.Clear();
        _global.Dispose();
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new ZxdgDecorationManagerV1Resource(client, version, id);
        manager.GetToplevelDecoration += (_, e) =>
        {
            var resource = new ZxdgToplevelDecorationV1Resource(client, manager.Version, e.Id);
            var toplevel = e.Toplevel is { } toplevelResource ? XdgToplevelRegistry.Resolve(toplevelResource) : null;
            if (toplevel is null)
            {
                resource.PostError(
                    (uint)ZxdgToplevelDecorationV1.Error.Orphaned,
                    "unknown xdg_toplevel");
                return;
            }

            if (_decorations.ContainsKey(toplevel))
            {
                resource.PostError(
                    (uint)ZxdgToplevelDecorationV1.Error.AlreadyConstructed,
                    "the toplevel already has a decoration object");
                return;
            }

            _decorations[toplevel] = new Decoration(this, resource, toplevel);
        };
    }

    private void Retain(XdgToplevelWindow toplevel, DecorationMode mode)
    {
        if (_retained.TryGetValue(toplevel, out var existing))
        {
            existing.Mode = mode;
            return;
        }

        _retained[toplevel] = new RetainedMode(this, toplevel, mode);
    }

    private void Forget(XdgToplevelWindow toplevel)
    {
        if (_retained.Remove(toplevel, out var retained))
        {
            retained.Unsubscribe();
        }
    }

    private void Expire(XdgToplevelWindow toplevel)
    {
        if (_retained.Remove(toplevel, out var retained))
        {
            retained.Unsubscribe();
            if (retained.Mode == DecorationMode.ServerSide && !_decorations.ContainsKey(toplevel))
            {
                ModeChanged?.Invoke(toplevel, DecorationMode.ClientSide);
            }
        }
    }

    private sealed class RetainedMode
    {
        private readonly XdgDecorationManager _owner;
        private readonly XdgToplevelWindow _toplevel;

        internal RetainedMode(XdgDecorationManager owner, XdgToplevelWindow toplevel, DecorationMode mode)
        {
            _owner = owner;
            _toplevel = toplevel;
            Mode = mode;
            toplevel.Surface.Committed += OnCommitted;
            toplevel.Destroyed += OnDestroyed;
        }

        internal DecorationMode Mode { get; set; }

        internal void Unsubscribe()
        {
            _toplevel.Surface.Committed -= OnCommitted;
            _toplevel.Destroyed -= OnDestroyed;
        }

        private void OnCommitted() => _owner.Expire(_toplevel);

        private void OnDestroyed() => _owner.Forget(_toplevel);
    }

    private sealed class Decoration
    {
        private readonly XdgDecorationManager _owner;
        private readonly ZxdgToplevelDecorationV1Resource _resource;
        private readonly XdgToplevelWindow _toplevel;
        private DecorationMode? _preference;
        private bool _configured;

        internal Decoration(XdgDecorationManager owner, ZxdgToplevelDecorationV1Resource resource, XdgToplevelWindow toplevel)
        {
            _owner = owner;
            _resource = resource;
            _toplevel = toplevel;
            Mode = Decide();

            resource.SetMode += (_, e) =>
            {
                if (e.Mode is not (ZxdgToplevelDecorationV1.Mode.ClientSide or ZxdgToplevelDecorationV1.Mode.ServerSide))
                {
                    resource.PostError((uint)ZxdgToplevelDecorationV1.Error.InvalidMode, $"invalid decoration mode {(uint)e.Mode}");
                    return;
                }

                _preference = (DecorationMode)e.Mode;
                owner.PreferenceChanged?.Invoke(toplevel, _preference);
                Reconsider();
            };

            resource.UnsetMode += (_, _) =>
            {
                _preference = null;
                owner.PreferenceChanged?.Invoke(toplevel, null);
                Reconsider();
            };

            resource.Destroyed += (_, _) =>
            {
                _owner.Retain(_toplevel, Mode);
                Detach();
            };
            toplevel.Destroyed += OnToplevelDestroyed;
            toplevel.Configuring += OnConfiguring;
            toplevel.RequestConfigure();

            owner.PreferenceChanged?.Invoke(toplevel, _preference);
        }

        internal DecorationMode Mode { get; private set; }

        internal DecorationMode? Preference => _preference;

        private DecorationMode Decide() =>
            _owner.ChooseMode?.Invoke(_toplevel, _preference)
            ?? (_owner._imposed.TryGetValue(_toplevel, out var imposed) ? imposed : null)
            ?? _preference
            ?? (_owner._retained.TryGetValue(_toplevel, out var retained) ? retained.Mode : (DecorationMode?)null)
            ?? _owner.DefaultMode;

        internal void Reconsider()
        {
            var mode = Decide();
            if (mode != Mode || !_configured)
            {
                Mode = mode;
                _configured = false;
                _toplevel.RequestConfigure();
            }
        }

        private void OnConfiguring()
        {
            if (!_configured && !_resource.IsDestroyed)
            {
                _configured = true;
                _resource.SendConfigure((ZxdgToplevelDecorationV1.Mode)Mode);
                _owner.ModeChanged?.Invoke(_toplevel, Mode);
            }
        }

        private void OnToplevelDestroyed()
        {
            if (!_resource.IsDestroyed)
            {
                _resource.PostError(
                    (uint)ZxdgToplevelDecorationV1.Error.Orphaned,
                    "xdg_toplevel destroyed before its decoration object");
            }

            Detach();
        }

        private void Detach()
        {
            _toplevel.Destroyed -= OnToplevelDestroyed;
            _toplevel.Configuring -= OnConfiguring;
            _owner._decorations.Remove(_toplevel);
        }
    }
}
