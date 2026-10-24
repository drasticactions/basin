using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

/// <summary>
/// Serves the <c>zxdg_output_manager_v1</c> global. It reports the logical box of each output, with optional overrides for single clients.
/// </summary>
public sealed class XdgOutputManager : IDisposable, Basin.Capabilities.ILogicalOutputOverrides
{
    /// <summary>
    /// The protocol version that the global advertises.
    /// </summary>
    public const int Version = 3;

    private readonly WlGlobal _global;
    private readonly OutputLayout _layout;
    private readonly List<Entry> _outputs = [];
    private readonly Dictionary<(WlClient Client, OutputGlobal Output), Box> _overrides = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="XdgOutputManager"/> class and creates the global. It sends the logical boxes again when the layout changes.
    /// </summary>
    /// <param name="display">The display that holds the global.</param>
    /// <param name="layout">The layout that supplies the logical boxes.</param>
    public XdgOutputManager(WlServerDisplay display, OutputLayout layout)
    {
        _layout = layout;
        _global = display.CreateGlobal(ZxdgOutputManagerV1.Interface, Version, OnBind);
        layout.Changed += OnLayoutChanged;
    }

    /// <inheritdoc/>
    public void SetClientOverride(WlClient client, OutputGlobal output, Box logical)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(output);
        var key = (client, output);
        if (_overrides.TryGetValue(key, out var existing) && existing == logical)
        {
            return;
        }

        if (!_overrides.ContainsKey(key))
        {
            client.Destroyed += () => _overrides.Remove(key);
        }

        _overrides[key] = logical;
        Resend(client, output);
    }

    /// <inheritdoc/>
    public void ClearClientOverride(WlClient client, OutputGlobal output)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(output);
        if (!_overrides.Remove((client, output)))
        {
            return;
        }

        Resend(client, output);
    }

    private void Resend(WlClient client, OutputGlobal output)
    {
        foreach (var entry in _outputs)
        {
            if (entry.Resource.Client == client && ReferenceEquals(entry.Output, output))
            {
                entry.Box = BoxFor(entry.Resource, entry.Output);
                SendBox(entry);
                SendDone(entry);
            }
        }
    }

    private Box BoxFor(ZxdgOutputV1Resource resource, OutputGlobal output) =>
        _overrides.TryGetValue((resource.Client, output), out var logical)
            ? logical
            : _layout.BoxOf(output.Output);

    /// <summary>
    /// Stops following the layout, drops every override and removes the global.
    /// </summary>
    public void Dispose()
    {
        _layout.Changed -= OnLayoutChanged;
        _overrides.Clear();
        _global.Dispose();
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new ZxdgOutputManagerV1Resource(client, version, id);
        manager.GetXdgOutput += (_, e) =>
        {
            var resource = new ZxdgOutputV1Resource(client, manager.Version, e.Id);
            var output = OutputGlobal.FromResource(e.Output);
            if (output is null)
            {
                return;
            }

            var entry = new Entry(resource, output, BoxFor(resource, output));
            _outputs.Add(entry);
            resource.Destroyed += (_, _) => _outputs.RemoveAll(candidate => candidate.Resource == resource);
            SendBox(entry);
            if (resource.Version >= 2)
            {
                resource.SendName(output.Output.Name);
                resource.SendDescription(output.Output.Description);
            }

            SendDone(entry);
        };
    }

    private void OnLayoutChanged()
    {
        foreach (var entry in _outputs)
        {
            var box = BoxFor(entry.Resource, entry.Output);
            if (box == entry.Box)
            {
                continue;
            }

            entry.Box = box;
            SendBox(entry);
            SendDone(entry);
        }
    }

    private static void SendBox(Entry entry)
    {
        entry.Resource.SendLogicalPosition(entry.Box.X, entry.Box.Y);
        entry.Resource.SendLogicalSize(entry.Box.Width, entry.Box.Height);
    }

    private static void SendDone(Entry entry)
    {
        if (entry.Resource.Version < 3)
        {
#pragma warning disable CS0618
            entry.Resource.SendDone();
#pragma warning restore CS0618
        }
        else
        {
            entry.Output.SendDone(entry.Resource.Client);
        }
    }

    private sealed class Entry(ZxdgOutputV1Resource resource, OutputGlobal output, Box box)
    {
        public ZxdgOutputV1Resource Resource { get; } = resource;

        public OutputGlobal Output { get; } = output;

        public Box Box { get; set; } = box;
    }
}
