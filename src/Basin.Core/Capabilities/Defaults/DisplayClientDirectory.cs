using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Basin.Diagnostics;
using Wayland.Server;

namespace Basin.Capabilities.Defaults;

/// <summary>
/// Lists the clients of a Wayland display with their credentials. On libwayland it can also count the objects of each client.
/// </summary>
public sealed unsafe class DisplayClientDirectory : IClientDirectory, IDisposable
{
    private const string Library = "libwayland-server.so.0";
    private const int IteratorContinue = 1;

    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly WlServerDisplay _display;
    private readonly List<Entry> _clients = [];
    private readonly Dictionary<nint, string> _names = [];
    private readonly Dictionary<(nint Interface, uint Version), int> _tally = [];
    private ulong _nextId = 1;
    private GCHandle _self;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="DisplayClientDirectory"/> class and starts to track the clients of a display.
    /// </summary>
    /// <param name="display">The display whose clients to list.</param>
    public DisplayClientDirectory(WlServerDisplay display)
    {
        ArgumentNullException.ThrowIfNull(display);
        _display = display;
        CanListObjects = display.Transport is LibWaylandTransport && NativeLibrary.TryLoad(Library, out _);
        try
        {
            foreach (var client in display.Clients)
            {
                Track(client);
            }
        }
        catch (NotSupportedException)
        {
        }

        try
        {
            display.ClientCreated += Track;
        }
        catch (NotSupportedException)
        {
        }
    }

    /// <summary>
    /// Gets a value indicating whether <see cref="Objects"/> can count objects. It is <see langword="true"/> only on the libwayland transport.
    /// </summary>
    public bool CanListObjects { get; }

    /// <summary>
    /// Gets the number of clients that are connected now.
    /// </summary>
    public int Count => _clients.Count;

    /// <inheritdoc/>
    public int Enumerate(Span<ClientInfo> clients)
    {
        _thread.Assert();
        if (clients.Length < _clients.Count)
        {
            return -1;
        }

        for (var i = 0; i < _clients.Count; i++)
        {
            var entry = _clients[i];
            clients[i] = new ClientInfo(entry.Id, entry.Credentials.Pid, entry.Credentials.Uid, entry.Credentials.Gid, entry.Client);
        }

        return _clients.Count;
    }

    /// <inheritdoc/>
    public int Objects(ulong clientId, Span<ClientObjectInfo> objects)
    {
        _thread.Assert();
        if (!CanListObjects || Find(clientId) is not { } entry || entry.Client.IsDestroyed)
        {
            return 0;
        }

        _tally.Clear();
        if (!_self.IsAllocated)
        {
            _self = GCHandle.Alloc(this);
        }

        wl_client_for_each_resource(entry.Client.RawHandle, &Visit, (void*)GCHandle.ToIntPtr(_self));
        if (objects.Length < _tally.Count)
        {
            return -1;
        }

        var count = 0;
        foreach (var ((name, version), tally) in _tally)
        {
            objects[count++] = new ClientObjectInfo(NameOf(name), version, tally);
        }

        return count;
    }

    /// <summary>
    /// Stops tracking new clients and clears the list. A second call has no effect.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _display.ClientCreated -= Track;
        }
        catch (NotSupportedException)
        {
        }

        _clients.Clear();
        if (_self.IsAllocated)
        {
            _self.Free();
        }
    }

    private void Track(WlClient client)
    {
        if (_disposed)
        {
            return;
        }

        var credentials = client.TryGetCredentials(out var found) ? found : default;
        var entry = new Entry(_nextId++, client, credentials);
        _clients.Add(entry);
        client.Destroyed += () => _clients.Remove(entry);
    }

    private Entry? Find(ulong id)
    {
        foreach (var entry in _clients)
        {
            if (entry.Id == id)
            {
                return entry;
            }
        }

        return null;
    }

    private string NameOf(nint name)
    {
        if (!_names.TryGetValue(name, out var text))
        {
            text = Marshal.PtrToStringUTF8(name) ?? "?";
            _names[name] = text;
        }

        return text;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Visit(nint resource, void* data)
    {
        try
        {
            if (GCHandle.FromIntPtr((nint)data).Target is DisplayClientDirectory directory)
            {
                var key = (wl_resource_get_class(resource), (uint)wl_resource_get_version(resource));
                directory._tally[key] = directory._tally.GetValueOrDefault(key) + 1;
            }
        }
        catch (Exception error)
        {
            BasinLog.For("clients").Warn($"walking a client's objects failed: {error.Message}");
        }

        return IteratorContinue;
    }

    [DllImport(Library)]
    private static extern void wl_client_for_each_resource(nint client, delegate* unmanaged[Cdecl]<nint, void*, int> iterator, void* data);

    [DllImport(Library)]
    private static extern nint wl_resource_get_class(nint resource);

    [DllImport(Library)]
    private static extern int wl_resource_get_version(nint resource);

    private sealed record Entry(ulong Id, WlClient Client, WlClientCredentials Credentials);
}
