using Basin.Shell.Xdg.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Shell.Xdg;

/// <summary>
/// The <c>zxdg_decoration_manager_v1</c> global. It negotiates the decoration mode of each toplevel. Drawing a server-side frame is the consumer's work.
/// </summary>
public sealed class XdgDecorationManager : IDisposable, IZxdgDecorationManagerV1Handler
{
    /// <summary>
    /// The version of <c>zxdg_decoration_manager_v1</c> that the global advertises.
    /// </summary>
    public const int Version = 2;

    private readonly WlGlobal _global;
    private readonly Dictionary<XdgToplevelWindow, Decoration> _decorations = [];
    private readonly Dictionary<XdgToplevelWindow, RetainedMode> _retained = [];
    private readonly Dictionary<XdgToplevelWindow, DecorationMode?> _imposed = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="XdgDecorationManager"/> class. It creates the global on <paramref name="display"/> at once.
    /// </summary>
    /// <param name="display">The display that advertises the global.</param>
    public XdgDecorationManager(WlServerDisplay display)
    {
        _global = display.CreateGlobal(ZxdgDecorationManagerV1.Interface, Version, OnBind);
    }

    /// <summary>
    /// Gets or sets the mode for a toplevel when no policy, client preference or retained mode decides.
    /// </summary>
    public DecorationMode DefaultMode { get; set; } = DecorationMode.ClientSide;

    /// <summary>
    /// Gets or sets the policy that decides the mode. It gets the toplevel and the client's preference. When it is set, <see cref="SetMode"/> and <see cref="DefaultMode"/> have no effect.
    /// </summary>
    public Func<XdgToplevelWindow, DecorationMode?, DecorationMode>? ChooseMode { get; set; }

    /// <summary>
    /// Occurs when a decoration configure goes out, with the mode it sends. It also occurs with client-side when a retained server-side mode expires and no decoration object exists.
    /// </summary>
    public event Action<XdgToplevelWindow, DecorationMode>? ModeChanged;

    /// <summary>
    /// Occurs when a decoration object is created and when the client sets or unsets its mode. The argument is the client's preference, or <see langword="null"/> when it has none.
    /// </summary>
    public event Action<XdgToplevelWindow, DecorationMode?>? PreferenceChanged;

    /// <summary>
    /// Finds the mode that the decoration object of a toplevel decided, or the mode it left behind.
    /// </summary>
    /// <param name="toplevel">The toplevel.</param>
    /// <returns>The decoration object's mode. With no decoration object, the mode that a destroyed one retained until the next commit, else <see cref="DecorationMode.ClientSide"/>.</returns>
    public DecorationMode ModeOf(XdgToplevelWindow toplevel) =>
        _decorations.TryGetValue(toplevel, out var decoration) ? decoration.Mode
        : _retained.TryGetValue(toplevel, out var retained) ? retained.Mode
        : DecorationMode.ClientSide;

    /// <summary>
    /// Finds the client's preference for a toplevel.
    /// </summary>
    /// <param name="toplevel">The toplevel.</param>
    /// <param name="preference">The preference, or <see langword="null"/> when the client has none.</param>
    /// <returns><see langword="true"/> when the toplevel has a decoration object.</returns>
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

    /// <summary>
    /// Imposes a mode on one toplevel over the client's preference, and sends a new configure when the decided mode changes. <see cref="ChooseMode"/> still decides first.
    /// </summary>
    /// <param name="toplevel">The toplevel.</param>
    /// <param name="mode">The mode to impose, or <see langword="null"/> to return to the negotiated mode.</param>
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

    /// <summary>
    /// Destroys the global and drops every retained mode.
    /// </summary>
    public void Dispose()
    {
        foreach (var retained in _retained.Values)
        {
            retained.Unsubscribe();
        }

        _retained.Clear();
        _global.Dispose();
    }

    private void OnBind(WlClient client, uint version, uint id) => _ = new ZxdgDecorationManagerV1Resource(client, version, id, this);

    void IZxdgDecorationManagerV1Handler.Destroy(ZxdgDecorationManagerV1Resource resource)
    {
    }

    void IZxdgDecorationManagerV1Handler.GetToplevelDecoration(ZxdgDecorationManagerV1Resource resource, in ZxdgDecorationManagerV1Resource.GetToplevelDecorationEventArgs args)
    {
        var toplevel = args.Toplevel is { } toplevelResource ? XdgToplevelRegistry.Resolve(toplevelResource) : null;
        if (toplevel is null)
        {
            new ZxdgToplevelDecorationV1Resource(resource.Client, resource.Version, args.Id).PostError(
                (uint)ZxdgToplevelDecorationV1.Error.Orphaned,
                "The xdg_toplevel is not known.");
            return;
        }

        if (_decorations.ContainsKey(toplevel))
        {
            new ZxdgToplevelDecorationV1Resource(resource.Client, resource.Version, args.Id).PostError(
                (uint)ZxdgToplevelDecorationV1.Error.AlreadyConstructed,
                "The toplevel already has a decoration object.");
            return;
        }

        _decorations[toplevel] = new Decoration(this, resource.Client, resource.Version, args.Id, toplevel);
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

    private sealed class Decoration : IZxdgToplevelDecorationV1Handler
    {
        private readonly XdgDecorationManager _owner;
        private readonly ZxdgToplevelDecorationV1Resource _resource;
        private readonly XdgToplevelWindow _toplevel;
        private DecorationMode? _preference;
        private bool _configured;

        internal Decoration(XdgDecorationManager owner, WlClient client, uint version, uint id, XdgToplevelWindow toplevel)
        {
            _owner = owner;
            var resource = new ZxdgToplevelDecorationV1Resource(client, version, id, this);
            _resource = resource;
            _toplevel = toplevel;
            Mode = Decide();

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

        void IZxdgToplevelDecorationV1Handler.Destroy(ZxdgToplevelDecorationV1Resource resource)
        {
        }

        void IZxdgToplevelDecorationV1Handler.SetMode(ZxdgToplevelDecorationV1Resource resource, in ZxdgToplevelDecorationV1Resource.SetModeEventArgs args)
        {
            if (args.Mode is not (ZxdgToplevelDecorationV1.Mode.ClientSide or ZxdgToplevelDecorationV1.Mode.ServerSide))
            {
                resource.PostError((uint)ZxdgToplevelDecorationV1.Error.InvalidMode, $"Decoration mode {(uint)args.Mode} is not a known value.");
                return;
            }

            _preference = (DecorationMode)args.Mode;
            _owner.PreferenceChanged?.Invoke(_toplevel, _preference);
            Reconsider();
        }

        void IZxdgToplevelDecorationV1Handler.UnsetMode(ZxdgToplevelDecorationV1Resource resource)
        {
            _preference = null;
            _owner.PreferenceChanged?.Invoke(_toplevel, null);
            Reconsider();
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
                    "The xdg_toplevel was destroyed before its decoration object.");
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
