using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Wayland.Server;

namespace Basin.Desktop;

public sealed class ForeignToplevelListManager : IToplevelObserver, IDisposable
{
    public const int Version = 1;

    private static readonly Dictionary<nint, ulong> HandleToplevels = [];

    private readonly WlGlobal _global;
    private readonly IToplevelModel? _model;
    private readonly List<Binding> _lists = [];
    private readonly Dictionary<ulong, Tracked> _toplevels = [];
    private int _identifierCounter;

    private sealed class Binding
    {
        public required ExtForeignToplevelListV1Resource List;
        public bool Stopped;
    }

    private sealed class Tracked
    {
        public required ulong Id;
        public required string Identifier;
        public required List<ExtForeignToplevelHandleV1Resource> Handles;
        public string? SentTitle;
        public string? SentAppId;
    }

    public ForeignToplevelListManager(WlServerDisplay display, IToplevelModel? model)
    {
        ArgumentNullException.ThrowIfNull(display);
        _model = model;
        _global = display.CreateGlobal(ExtForeignToplevelListV1.Interface, Version, OnBind);
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

    public string? IdentifierOf(ulong toplevelId) =>
        _toplevels.GetValueOrDefault(toplevelId)?.Identifier;

    private void OnAdded(ulong id)
    {
        if (_toplevels.ContainsKey(id))
        {
            return;
        }

        var tracked = new Tracked
        {
            Id = id,
            Identifier = NewIdentifier(),
            Handles = [],
        };
        _toplevels[id] = tracked;

        foreach (var binding in _lists)
        {
            if (!binding.Stopped && !binding.List.IsDestroyed)
            {
                CreateHandle(binding.List, tracked);
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
        if (!title && !appId)
        {
            return;
        }

        foreach (var handle in tracked.Handles)
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

            handle.SendDone();
        }

        tracked.SentTitle = info.Title;
        tracked.SentAppId = info.AppId;
    }

    private void OnRemoved(ulong id)
    {
        if (!_toplevels.Remove(id, out var tracked))
        {
            return;
        }

        foreach (var handle in tracked.Handles)
        {
            if (!handle.IsDestroyed)
            {
                handle.SendClosed();
            }
        }
    }

    private string NewIdentifier()
    {
        Span<byte> bytes = stackalloc byte[16];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);

        var counter = (uint)(++_identifierCounter);
        bytes[0] = (byte)counter;
        bytes[1] = (byte)(counter >> 8);
        bytes[2] = (byte)(counter >> 16);
        bytes[3] = (byte)(counter >> 24);
        return Convert.ToHexStringLower(bytes);
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var list = new ExtForeignToplevelListV1Resource(client, version, id);
        if (_model is null)
        {
            list.SendFinished();
            return;
        }

        var binding = new Binding { List = list };
        _lists.Add(binding);
        list.Stop += (_, _) =>
        {
            binding.Stopped = true;
            list.SendFinished();
        };
        list.Destroyed += (_, _) => _lists.Remove(binding);

        foreach (var tracked in _toplevels.Values)
        {
            CreateHandle(list, tracked);
        }
    }

    private void CreateHandle(ExtForeignToplevelListV1Resource list, Tracked tracked)
    {
        var handle = new ExtForeignToplevelHandleV1Resource(list.Client, list.Version, 0);
        list.SendToplevel(handle);
        tracked.Handles.Add(handle);
        var raw = handle.RawHandle;
        HandleToplevels[raw] = tracked.Id;
        handle.Destroyed += (_, _) =>
        {
            tracked.Handles.Remove(handle);
            HandleToplevels.Remove(raw);
        };

        handle.SendIdentifier(tracked.Identifier);
        if (_model is { } model && model.TryGet(tracked.Id, out var info))
        {
            handle.SendTitle(info.Title);
            handle.SendAppId(info.AppId);
            tracked.SentTitle = info.Title;
            tracked.SentAppId = info.AppId;
        }

        handle.SendDone();
    }
}
