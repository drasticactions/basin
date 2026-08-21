using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Basin.Shell.Xdg;
using Basin.Shell.Xdg.Protocol;
using Wayland.Server;

namespace Basin.Desktop;

public sealed class SessionManager : IDisposable
{
    public const int Version = 1;

    private readonly WlGlobal _global;
    private readonly ISessionStore? _store;
    private readonly Dictionary<string, Session> _live = [];

    public SessionManager(WlServerDisplay display, ISessionStore? store)
    {
        ArgumentNullException.ThrowIfNull(display);
        _store = store;
        _global = display.CreateGlobal(XdgSessionManagerV1.Interface, Version, OnBind);
    }

    public event Action<string, string, XdgToplevelWindow>? ToplevelAdded;

    public event Action<string, string>? ToplevelRemoved;

    public void Dispose() => _global.Dispose();

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new XdgSessionManagerV1Resource(client, version, id);
        manager.GetSession += (_, e) => OnGetSession(manager, client, e);
    }

    private void OnGetSession(
        XdgSessionManagerV1Resource manager,
        WlClient client,
        XdgSessionManagerV1Resource.GetSessionEventArgs e)
    {
        var resource = new XdgSessionV1Resource(client, manager.Version, e.Id);
        if (e.Reason is not (XdgSessionManagerV1.Reason.Launch
            or XdgSessionManagerV1.Reason.Recover
            or XdgSessionManagerV1.Reason.SessionRestore))
        {
            manager.PostError((uint)XdgSessionManagerV1.Error.InvalidReason, $"invalid reason {(uint)e.Reason}");
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
                    manager.PostError((uint)XdgSessionManagerV1.Error.InUse, $"session '{existing}' is already in use");
                    return;
                }

                holder.Replace();
            }

            var restored = new Session(this, resource, existing, reason);
            _live[existing] = restored;
            resource.SendRestored();
            return;
        }

        var minted = _store?.CreateSessionId();
        if (minted is null)
        {
            _ = new Session(this, resource, sessionId: null, reason);
            return;
        }

        var session = new Session(this, resource, minted, reason);
        _live[minted] = session;
        resource.SendCreated(minted);
    }

    private sealed class Session
    {
        private readonly SessionManager _owner;
        private readonly SessionRestoreReason _reason;
        private readonly Dictionary<string, ToplevelSession> _toplevels = [];
        private readonly HashSet<XdgToplevelWindow> _added = [];
        private bool _inert;

        public Session(SessionManager owner, XdgSessionV1Resource resource, string? sessionId, SessionRestoreReason reason)
        {
            _owner = owner;
            _reason = reason;
            Resource = resource;
            SessionId = sessionId;

            resource.AddToplevel += (_, e) => Add(e.Id, e.Toplevel, e.Name, restore: false);
            resource.RestoreToplevel += (_, e) => Add(e.Id, e.Toplevel, e.Name, restore: true);
            resource.RemoveToplevel += (_, e) => Remove(e.Name);
            resource.Destroyed += (_, _) => Detach();
            resource.Remove += (_, _) =>
            {
                if (SessionId is { } id)
                {
                    _owner._store?.Forget(id);
                }

                Detach();
            };
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
            var handle = new XdgToplevelSessionV1Resource(Resource.Client, Resource.Version, newId);
            if (XdgToplevels.Resolve(toplevelResource) is not { } toplevel)
            {
                return;
            }

            if (name.Length == 0)
            {
                Resource.PostError((uint)XdgSessionV1.Error.InvalidName, "toplevel name is empty");
                return;
            }

            if (_toplevels.ContainsKey(name))
            {
                Resource.PostError((uint)XdgSessionV1.Error.NameInUse, $"toplevel name '{name}' is already in this session");
                return;
            }

            if (!_added.Add(toplevel))
            {
                Resource.PostError((uint)XdgSessionV1.Error.AlreadyAdded, "this toplevel is already in the session");
                return;
            }

            if (restore && toplevel.Xdg.HasCommitted)
            {
                Resource.PostError((uint)XdgSessionV1.Error.AlreadyMapped, "restore_toplevel after the surface's first commit");
                return;
            }

            var entry = new ToplevelSession(this, handle, toplevel, name);
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
                Resource.PostError((uint)XdgSessionV1.Error.NameInUse, $"toplevel name '{name}' is already in this session");
                return false;
            }

            _toplevels.Remove(entry.Name);
            _toplevels[name] = entry;
            return true;
        }

        private sealed class ToplevelSession
        {
            private readonly Session _session;
            private readonly XdgToplevelSessionV1Resource _resource;
            private readonly XdgToplevelWindow _toplevel;
            private bool _inert;

            public ToplevelSession(
                Session session,
                XdgToplevelSessionV1Resource resource,
                XdgToplevelWindow toplevel,
                string name)
            {
                _session = session;
                _resource = resource;
                _toplevel = toplevel;
                Name = name;

                resource.Rename += (_, e) =>
                {
                    if (!_inert && e.Name.Length > 0 && _session.TryRename(this, e.Name))
                    {
                        Name = e.Name;
                    }
                };
            }

            public string Name { get; private set; }

            public void MakeInert() => _inert = true;

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
