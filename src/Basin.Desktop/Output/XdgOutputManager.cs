using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

public sealed class XdgOutputManager : IDisposable
{
    public const int Version = 3;

    private readonly WlGlobal _global;
    private readonly OutputLayout _layout;
    private readonly List<Entry> _outputs = [];

    public XdgOutputManager(WlServerDisplay display, OutputLayout layout)
    {
        _layout = layout;
        _global = display.CreateGlobal(ZxdgOutputManagerV1.Interface, Version, OnBind);
        layout.Changed += OnLayoutChanged;
    }

    public void Dispose()
    {
        _layout.Changed -= OnLayoutChanged;
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

            var entry = new Entry(resource, output, _layout.BoxOf(output.Output));
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
            var box = _layout.BoxOf(entry.Output.Output);
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
