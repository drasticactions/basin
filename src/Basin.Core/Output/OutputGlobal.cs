using Wayland;
using Wayland.Server;

namespace Basin;

public sealed class OutputGlobal : IDisposable
{
    public const int Version = 4;

    private readonly WlServerDisplay _display;
    private readonly WlGlobal _global;
    private readonly IOutput _output;
    private readonly List<WlOutputResource> _resources = [];
    private bool _disposed;

    public OutputGlobal(WlServerDisplay display, IOutput output)
    {
        ByOutput[output] = this;
        _display = display;
        _output = output;
        _advertised = Snapshot();
        _global = display.CreateGlobal(WlOutput.Interface, Version, OnBind);
        _output.Committed += OnOutputCommitted;
    }

    public IOutput Output => _output;

    public uint NameFor(WlClient client) => _global.NameFor(client);

    public void SendDone(WlClient client)
    {
        foreach (var resource in ResourcesOf(client))
        {
            if (resource.Version >= 2)
            {
                resource.SendDone();
            }
        }
    }

    public IReadOnlyList<WlOutputResource> Resources => _resources;

    public IEnumerable<WlOutputResource> ResourcesOf(WlClient client)
    {
        foreach (var resource in _resources)
        {
            if (resource.Client == client)
            {
                yield return resource;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _output.Committed -= OnOutputCommitted;
        if (ByOutput.TryGetValue(_output, out var current) && current == this)
        {
            ByOutput.Remove(_output);
        }

        _global.Dispose();
    }

    public void Retire(int graceMillis = GlobalRetirement.DefaultGraceMillis) =>
        GlobalRetirement.Retire(_display, _global, Dispose, graceMillis);

    private static readonly Dictionary<WlOutputResource, OutputGlobal> ByResource = [];
    private static readonly Dictionary<IOutput, OutputGlobal> ByOutput = [];

    public static OutputGlobal? For(IOutput output) =>
        output is null ? null : ByOutput.GetValueOrDefault(output);

    public static OutputGlobal? FromResource(WlOutputResource? resource) =>
        resource is not null && ByResource.TryGetValue(resource, out var global) ? global : null;

    public event Action<WlOutputResource>? ResourceBound;

    private Point _position;

    internal void NotifyPosition(int x, int y)
    {
        if (_position.X == x && _position.Y == y)
        {
            return;
        }

        _position = new Point(x, y);
        foreach (var resource in _resources)
        {
            SendState(resource, sendName: false, sendDescription: false);
        }
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var resource = new WlOutputResource(client, version, id);
        _resources.Add(resource);
        ByResource[resource] = this;
        resource.Destroyed += (_, _) =>
        {
            _resources.Remove(resource);
            ByResource.Remove(resource);
        };
        SendState(resource, sendName: true, sendDescription: true);
        ResourceBound?.Invoke(resource);
    }

    private (int PhysicalWidth, int PhysicalHeight, OutputMode Mode, int Scale, OutputTransform Transform, string Description) _advertised;

    private (int PhysicalWidth, int PhysicalHeight, OutputMode Mode, int Scale, OutputTransform Transform, string Description) Snapshot()
    {
        var (physicalWidth, physicalHeight) = _output.PhysicalSize;
        return (physicalWidth, physicalHeight, _output.CurrentMode, OutputScaling.CeilScale(_output.Scale), _output.Transform, _output.Description);
    }

    private void OnOutputCommitted(OutputStateFields fields)
    {
        var current = Snapshot();
        if (current == _advertised)
        {
            return;
        }

        var descriptionChanged = current.Description != _advertised.Description;
        _advertised = current;
        foreach (var resource in _resources)
        {
            SendState(resource, sendName: false, sendDescription: descriptionChanged);
        }
    }

    private void SendState(WlOutputResource resource, bool sendName, bool sendDescription)
    {
        var (physicalWidth, physicalHeight) = _output.PhysicalSize;
        resource.SendGeometry(
            _position.X,
            _position.Y,
            physicalWidth,
            physicalHeight,
            WlOutput.Subpixel.Unknown,
            _output.Make,
            _output.Model,
            (WlOutput.Transform)_output.Transform);

        var mode = _output.CurrentMode;
        resource.SendMode(WlOutput.Mode.Current | WlOutput.Mode.Preferred, mode.Width, mode.Height, mode.RefreshMilliHz);

        if (resource.Version >= 2)
        {
            resource.SendScale(OutputScaling.CeilScale(_output.Scale));
        }

        if (resource.Version >= 4)
        {
            if (sendName)
            {
                resource.SendName(_output.Name);
            }

            if (sendDescription && _output.Description.Length > 0)
            {
                resource.SendDescription(_output.Description);
            }
        }

        if (resource.Version >= 2)
        {
            resource.SendDone();
        }
    }
}
