using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Wayland.Server;

namespace Basin.Desktop;

public sealed class ForeignToplevelManager : IToplevelObserver, IDisposable
{
    public const int Version = 3;

    private static readonly Dictionary<nint, ulong> HandleToplevels = [];

    private readonly WlGlobal _global;
    private readonly IToplevelModel? _model;
    private readonly List<ZwlrForeignToplevelManagerV1Resource> _managers = [];
    private readonly Dictionary<ulong, Tracked> _toplevels = [];

    private sealed class Tracked
    {
        public required ulong Id;
        public required Dictionary<ZwlrForeignToplevelManagerV1Resource, ZwlrForeignToplevelHandleV1Resource> Handles;
        public string? SentTitle;
        public string? SentAppId;
        public ulong SentParentId;
    }

    public ForeignToplevelManager(WlServerDisplay display, IToplevelModel? model)
    {
        ArgumentNullException.ThrowIfNull(display);
        _model = model;
        _global = display.CreateGlobal(ZwlrForeignToplevelManagerV1.Interface, Version, OnBind);
        if (_model is { } live)
        {
            live.AddObserver(this);
        }
    }

    public void OnToplevelAdded(ulong toplevelId) => OnAdded(toplevelId);

    public void OnToplevelChanged(ulong toplevelId) => OnChanged(toplevelId);

    public void OnToplevelRemoved(ulong toplevelId) => OnRemoved(toplevelId);

    public static ulong ToplevelOf(nint handleResource) =>
        HandleToplevels.GetValueOrDefault(handleResource);

    public void Dispose()
    {
        if (_model is { } live)
        {
            live.RemoveObserver(this);
        }

        _global.Dispose();
    }

    private void OnAdded(ulong id)
    {
        if (_toplevels.ContainsKey(id))
        {
            return;
        }

        var tracked = new Tracked { Id = id, Handles = [] };
        _toplevels[id] = tracked;
        foreach (var manager in _managers)
        {
            if (!manager.IsDestroyed)
            {
                CreateHandle(manager, tracked);
            }
        }
    }

    private void OnChanged(ulong id)
    {
        if (!_toplevels.TryGetValue(id, out var tracked) || _model is not { } model || !model.TryGet(id, out var info))
        {
            return;
        }

        var title = !string.Equals(tracked.SentTitle, info.Title, StringComparison.Ordinal);
        var appId = !string.Equals(tracked.SentAppId, info.AppId, StringComparison.Ordinal);
        var parent = info.ParentId != tracked.SentParentId;
        if (!title && !appId && !parent)
        {
            return;
        }

        foreach (var (manager, handle) in tracked.Handles)
        {
            if (handle.IsDestroyed)
            {
                continue;
            }

            if (title)
            {
                handle.SendTitle(info.Title);
            }

            if (appId)
            {
                handle.SendAppId(info.AppId);
            }

            if (parent)
            {
                SendParent(manager, handle, info.ParentId);
            }

            handle.SendDone();
        }

        tracked.SentTitle = info.Title;
        tracked.SentAppId = info.AppId;
        if (parent)
        {
            tracked.SentParentId = info.ParentId;
        }
    }

    private void SendParent(
        ZwlrForeignToplevelManagerV1Resource manager,
        ZwlrForeignToplevelHandleV1Resource handle,
        ulong parentId)
    {
        if (!handle.SupportsSendParent)
        {
            return;
        }

        ZwlrForeignToplevelHandleV1Resource? parentHandle = null;
        if (parentId != 0)
        {
            if (!_toplevels.TryGetValue(parentId, out var parent) ||
                !parent.Handles.TryGetValue(manager, out parentHandle) ||
                parentHandle.IsDestroyed)
            {
                return;
            }
        }

        handle.SendParent(parentHandle);
    }

    private void OnRemoved(ulong id)
    {
        if (!_toplevels.Remove(id, out var tracked))
        {
            return;
        }

        foreach (var handle in tracked.Handles.Values)
        {
            if (!handle.IsDestroyed)
            {
                handle.SendClosed();
            }
        }
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new ZwlrForeignToplevelManagerV1Resource(client, version, id);
        if (_model is null)
        {
            manager.SendFinished();
            return;
        }

        _managers.Add(manager);
        manager.Destroyed += (_, _) => _managers.Remove(manager);

        foreach (var tracked in _toplevels.Values)
        {
            CreateHandle(manager, tracked);
        }
    }

    private void CreateHandle(ZwlrForeignToplevelManagerV1Resource manager, Tracked tracked)
    {
        var model = _model!;
        var id = tracked.Id;
        var handle = new ZwlrForeignToplevelHandleV1Resource(manager.Client, manager.Version, 0);
        manager.SendToplevel(handle);
        tracked.Handles[manager] = handle;
        var raw = handle.RawHandle;
        HandleToplevels[raw] = id;
        handle.Destroyed += (_, _) =>
        {
            tracked.Handles.Remove(manager);
            HandleToplevels.Remove(raw);
        };

        if (model.TryGet(id, out var info))
        {
            handle.SendTitle(info.Title);
            handle.SendAppId(info.AppId);
            tracked.SentTitle = info.Title;
            tracked.SentAppId = info.AppId;
            if (info.ParentId != 0)
            {
                SendParent(manager, handle, info.ParentId);
                tracked.SentParentId = info.ParentId;
            }
        }

        handle.SendDone();

        handle.Activate += (_, _) => model.Request(id, new ToplevelRequest(ToplevelRequestKind.Activate));
        handle.Close += (_, _) => model.Request(id, new ToplevelRequest(ToplevelRequestKind.Close));
        handle.SetMaximized += (_, _) => model.Request(id, new ToplevelRequest(ToplevelRequestKind.Maximize));
        handle.UnsetMaximized += (_, _) => model.Request(id, new ToplevelRequest(ToplevelRequestKind.Unmaximize));
        handle.SetFullscreen += (_, _) => model.Request(id, new ToplevelRequest(ToplevelRequestKind.Fullscreen));
        handle.UnsetFullscreen += (_, _) => model.Request(id, new ToplevelRequest(ToplevelRequestKind.Unfullscreen));
        handle.SetMinimized += (_, _) => model.Request(id, new ToplevelRequest(ToplevelRequestKind.Minimize));
        handle.UnsetMinimized += (_, _) => model.Request(id, new ToplevelRequest(ToplevelRequestKind.Unminimize));
    }
}
