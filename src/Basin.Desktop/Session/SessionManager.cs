using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Basin.Shell.Xdg;
using Basin.Shell.Xdg.Protocol;
using Wayland.Server;

namespace Basin.Desktop;

/// <summary>
/// Serves the <c>xdg_session_manager_v1</c> global. It names toplevel sessions and restores saved state through <see cref="ISessionStore"/>.
/// </summary>
public sealed class SessionManager : IDisposable, IXdgSessionManagerV1Handler
{
    /// <summary>
    /// The protocol version that the global advertises.
    /// </summary>
    public const int Version = 1;

    private readonly WlGlobal _global;
    private readonly ISessionStore? _store;
    private readonly Dictionary<string, Session> _live = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionManager"/> class and creates the global.
    /// </summary>
    /// <param name="display">The display that holds the global.</param>
    /// <param name="store">The session store, or <see langword="null"/> to give clients sessions that are not saved.</param>
    /// <exception cref="ArgumentNullException"><paramref name="display"/> is <see langword="null"/>.</exception>
    public SessionManager(WlServerDisplay display, ISessionStore? store)
    {
        ArgumentNullException.ThrowIfNull(display);
        _store = store;
        _global = display.CreateGlobal(XdgSessionManagerV1.Interface, Version, OnBind);
    }

    /// <summary>
    /// Occurs when a client adds or restores a toplevel in a live session. The arguments are the session id, the toplevel name and the toplevel.
    /// </summary>
    public event Action<string, string, XdgToplevelWindow>? ToplevelAdded;

    /// <summary>
    /// Occurs when a client removes a toplevel from a session, after the store forgets it. The arguments are the session id and the toplevel name.
    /// </summary>
    public event Action<string, string>? ToplevelRemoved;

    /// <summary>
    /// Removes the global. Bound clients keep their resources.
    /// </summary>
    public void Dispose() => _global.Dispose();

    private void OnBind(WlClient client, uint version, uint id) =>
        _ = new XdgSessionManagerV1Resource(client, version, id, this);

    void IXdgSessionManagerV1Handler.Destroy(XdgSessionManagerV1Resource resource)
    {
    }

    void IXdgSessionManagerV1Handler.GetSession(XdgSessionManagerV1Resource resource, in XdgSessionManagerV1Resource.GetSessionEventArgs args) =>
        OnGetSession(resource, resource.Client, args);

    private void OnGetSession(
        XdgSessionManagerV1Resource manager,
        WlClient client,
        in XdgSessionManagerV1Resource.GetSessionEventArgs e)
    {
        if (e.Reason is not (XdgSessionManagerV1.Reason.Launch
            or XdgSessionManagerV1.Reason.Recover
            or XdgSessionManagerV1.Reason.SessionRestore))
        {
            _ = new XdgSessionV1Resource(client, manager.Version, e.Id);
            manager.PostError((uint)XdgSessionManagerV1.Error.InvalidReason, $"Reason {(uint)e.Reason} is not a known value.");
            return;
        }

        var reason = (SessionRestoreReason)(uint)e.Reason;
        var requested = e.SessionId;

        if (requested is { Length: > 0 } && _store is { } store && !store.IsValidSessionId(requested))
        {
            requested = null;
        }

        if (requested is { Length: > 0 } existing)
        {
            if (_live.TryGetValue(existing, out var holder))
            {
                if (holder.Resource.Client == client)
                {
                    _ = new XdgSessionV1Resource(client, manager.Version, e.Id);
                    manager.PostError((uint)XdgSessionManagerV1.Error.InUse, $"Session '{existing}' is already in use.");
                    return;
                }

                holder.Replace();
            }

            var restored = new Session(this, client, manager.Version, e.Id, existing, reason);
            _live[existing] = restored;
            restored.Resource.SendRestored();
            return;
        }

        var minted = _store?.CreateSessionId();
        if (minted is null)
        {
            _ = new Session(this, client, manager.Version, e.Id, sessionId: null, reason);
            return;
        }

        var session = new Session(this, client, manager.Version, e.Id, minted, reason);
        _live[minted] = session;
        session.Resource.SendCreated(minted);
    }

    private sealed class Session : IXdgSessionV1Handler
    {
        private readonly SessionManager _owner;
        private readonly SessionRestoreReason _reason;
        private readonly Dictionary<string, ToplevelSession> _toplevels = [];
        private readonly HashSet<XdgToplevelWindow> _added = [];
        private bool _inert;

        public Session(SessionManager owner, WlClient client, uint version, uint id, string? sessionId, SessionRestoreReason reason)
        {
            _owner = owner;
            _reason = reason;
            Resource = new XdgSessionV1Resource(client, version, id, this);
            SessionId = sessionId;

            Resource.Destroyed += (_, _) => Detach();
        }

        public XdgSessionV1Resource Resource { get; }

        public string? SessionId { get; }

        public void Replace()
        {
            if (!_inert && !Resource.IsDestroyed)
            {
                Resource.SendReplaced();
            }

            _inert = true;
            Detach();
        }

        void IXdgSessionV1Handler.Destroy(XdgSessionV1Resource resource)
        {
        }

        void IXdgSessionV1Handler.AddToplevel(XdgSessionV1Resource resource, in XdgSessionV1Resource.AddToplevelEventArgs args) =>
            Add(args.Id, args.Toplevel, args.Name, restore: false);

        void IXdgSessionV1Handler.RestoreToplevel(XdgSessionV1Resource resource, in XdgSessionV1Resource.RestoreToplevelEventArgs args) =>
            Add(args.Id, args.Toplevel, args.Name, restore: true);

        void IXdgSessionV1Handler.RemoveToplevel(XdgSessionV1Resource resource, in XdgSessionV1Resource.RemoveToplevelEventArgs args) =>
            Remove(args.Name);

        void IXdgSessionV1Handler.Remove(XdgSessionV1Resource resource)
        {
            if (SessionId is { } id)
            {
                _owner._store?.Forget(id);
            }

            Detach();
        }

        private void Detach()
        {
            if (SessionId is { } id && _owner._live.TryGetValue(id, out var live) && ReferenceEquals(live, this))
            {
                _owner._live.Remove(id);
            }

            _toplevels.Clear();
            _added.Clear();
        }

        private void Add(uint newId, XdgToplevelResource? toplevelResource, string name, bool restore)
        {
            if (XdgToplevels.Resolve(toplevelResource) is not { } toplevel)
            {
                _ = new XdgToplevelSessionV1Resource(Resource.Client, Resource.Version, newId);
                return;
            }

            if (name.Length == 0)
            {
                _ = new XdgToplevelSessionV1Resource(Resource.Client, Resource.Version, newId);
                Resource.PostError((uint)XdgSessionV1.Error.InvalidName, "The toplevel name is empty.");
                return;
            }

            if (_toplevels.ContainsKey(name))
            {
                _ = new XdgToplevelSessionV1Resource(Resource.Client, Resource.Version, newId);
                Resource.PostError((uint)XdgSessionV1.Error.NameInUse, $"Toplevel name '{name}' is already in this session.");
                return;
            }

            if (!_added.Add(toplevel))
            {
                _ = new XdgToplevelSessionV1Resource(Resource.Client, Resource.Version, newId);
                Resource.PostError((uint)XdgSessionV1.Error.AlreadyAdded, "This toplevel is already in the session.");
                return;
            }

            if (restore && toplevel.Xdg.HasCommitted)
            {
                _ = new XdgToplevelSessionV1Resource(Resource.Client, Resource.Version, newId);
                Resource.PostError((uint)XdgSessionV1.Error.AlreadyMapped, "restore_toplevel came after the first commit of the surface.");
                return;
            }

            var entry = new ToplevelSession(this, newId, toplevel, name);
            _toplevels[name] = entry;

            if (_inert || SessionId is not { } id)
            {
                return;
            }

            _owner.ToplevelAdded?.Invoke(id, name, toplevel);

            if (restore && _owner._store is { } store && store.TryRestore(id, name, _reason, out var state))
            {
                toplevel.Restore(new ToplevelRestore(id, name, _reason, state));
                entry.AnnounceOnFirstConfigure();
            }
        }

        private void Remove(string name)
        {
            if (!_toplevels.Remove(name, out var entry))
            {
                return;
            }

            entry.MakeInert();
            if (SessionId is { } id)
            {
                _owner._store?.ForgetToplevel(id, name);
                _owner.ToplevelRemoved?.Invoke(id, name);
            }
        }

        private bool TryRename(ToplevelSession entry, string name)
        {
            if (_toplevels.ContainsKey(name))
            {
                Resource.PostError((uint)XdgSessionV1.Error.NameInUse, $"Toplevel name '{name}' is already in this session.");
                return false;
            }

            _toplevels.Remove(entry.Name);
            _toplevels[name] = entry;
            return true;
        }

        private sealed class ToplevelSession : IXdgToplevelSessionV1Handler
        {
            private readonly Session _session;
            private readonly XdgToplevelSessionV1Resource _resource;
            private readonly XdgToplevelWindow _toplevel;
            private bool _inert;

            public ToplevelSession(
                Session session,
                uint id,
                XdgToplevelWindow toplevel,
                string name)
            {
                _session = session;
                _resource = new XdgToplevelSessionV1Resource(session.Resource.Client, session.Resource.Version, id, this);
                _toplevel = toplevel;
                Name = name;
            }

            public string Name { get; private set; }

            public void MakeInert() => _inert = true;

            void IXdgToplevelSessionV1Handler.Destroy(XdgToplevelSessionV1Resource resource)
            {
            }

            void IXdgToplevelSessionV1Handler.Rename(XdgToplevelSessionV1Resource resource, in XdgToplevelSessionV1Resource.RenameEventArgs args)
            {
                if (!_inert && args.Name.Length > 0 && _session.TryRename(this, args.Name))
                {
                    Name = args.Name;
                }
            }

            public void AnnounceOnFirstConfigure()
            {
                void OnConfiguring()
                {
                    _toplevel.Configuring -= OnConfiguring;
                    if (!_inert && !_resource.IsDestroyed)
                    {
                        _resource.SendRestored();
                    }
                }

                _toplevel.Configuring += OnConfiguring;
            }
        }
    }
}
