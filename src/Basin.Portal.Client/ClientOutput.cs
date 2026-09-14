using Basin;
using Basin.Portal.Client.Protocol;
using Wayland;

namespace Basin.Portal.Client;

public sealed class ClientOutput : IOutput
{
    private string _pendingName = "";
    private string _pendingDescription = "";
    private OutputMode _pendingMode;
    private double _pendingScale = 1;
    private OutputTransform _pendingTransform;
    private (int Width, int Height) _pendingPhysical;
    private (int X, int Y) _pendingPosition;
    private (int Width, int Height) _pendingLogical;
    private string _pendingMake = "";
    private string _pendingModel = "";

    public ClientOutput(uint registryName, WlOutput proxy)
    {
        RegistryName = registryName;
        Proxy = proxy;
        proxy.Geometry += (_, e) =>
        {
            _pendingPosition = (e.X, e.Y);
            _pendingPhysical = (e.PhysicalWidth, e.PhysicalHeight);
            _pendingTransform = (OutputTransform)(int)e.Transform;
            _pendingMake = e.Make;
            _pendingModel = e.Model;
        };
        proxy.ModeEvent += (_, e) =>
        {
            if ((e.Flags & WlOutput.Mode.Current) != 0)
            {
                _pendingMode = new OutputMode(e.Width, e.Height, e.Refresh);
            }
        };
        proxy.Scale += (_, e) => _pendingScale = Math.Max(1, e.Factor);
        proxy.Name += (_, e) => _pendingName = e.Name;
        proxy.Description += (_, e) => _pendingDescription = e.Description;
        proxy.Done += (_, _) => Apply();
    }

    public uint RegistryName { get; }

    public WlOutput Proxy { get; }

    public ZxdgOutputV1? XdgOutput { get; private set; }

    public bool IsReady { get; private set; }

    public (int X, int Y) Position { get; private set; }

    public string Name { get; private set; } = "";

    public string Description { get; private set; } = "";

    public string Make { get; private set; } = "";

    public string Model { get; private set; } = "";

    public string Serial => "";

    public (int Width, int Height) PhysicalSize { get; private set; }

    public OutputMode CurrentMode { get; private set; }

    public bool Enabled => true;

    public double Scale { get; private set; } = 1;

    public OutputTransform Transform { get; private set; }

    public bool AdaptiveSync => false;

    public event Action? Frame
    {
        add
        {
        }

        remove
        {
        }
    }

    public event Action? RepaintRequested
    {
        add
        {
        }

        remove
        {
        }
    }

    public event Action<OutputStateFields>? Committed;

    public event Action? Destroyed;

    public event Action? Ready;

    public void AttachXdgOutput(ZxdgOutputManagerV1 manager)
    {
        if (XdgOutput is not null)
        {
            return;
        }

        var xdg = manager.GetXdgOutput(Proxy);
        XdgOutput = xdg;
        xdg.LogicalPosition += (_, e) => _pendingPosition = (e.X, e.Y);
        xdg.LogicalSize += (_, e) => _pendingLogical = (e.Width, e.Height);
        xdg.Name += (_, e) => _pendingName = e.Name;
        xdg.Description += (_, e) => _pendingDescription = e.Description;
#pragma warning disable CS0618
        if (manager.Version < 3)
        {
            xdg.Done += (_, _) => Apply();
        }
#pragma warning restore CS0618
    }

    public void RequestRepaint()
    {
    }

    public bool TestCommit(OutputState state) => false;

    public bool Commit(OutputState state) => false;

    public void NotifyRemoved()
    {
        Destroyed?.Invoke();
        if (XdgOutput is { IsDestroyed: false } xdg)
        {
            xdg.Destroy();
        }

        if (!Proxy.IsDestroyed)
        {
            Proxy.Dispose();
        }
    }

    private void Apply()
    {
        if (_pendingMode.Width <= 0 || _pendingMode.Height <= 0)
        {
            return;
        }

        var scale = _pendingScale;
        if (_pendingLogical.Width > 0)
        {
            var rotated = _pendingTransform is OutputTransform.Rotate90 or OutputTransform.Rotate270 or OutputTransform.Flipped90 or OutputTransform.Flipped270;
            var pixelWidth = rotated ? _pendingMode.Height : _pendingMode.Width;
            scale = Math.Max(0.25, (double)pixelWidth / _pendingLogical.Width);
        }

        var fields = OutputStateFields.None;
        if (CurrentMode != _pendingMode)
        {
            fields |= OutputStateFields.Mode;
        }

        if (Math.Abs(Scale - scale) > 0.0001)
        {
            fields |= OutputStateFields.Scale;
        }

        if (Transform != _pendingTransform)
        {
            fields |= OutputStateFields.Transform;
        }

        if (!IsReady)
        {
            fields |= OutputStateFields.Enabled;
        }

        Name = string.IsNullOrEmpty(_pendingName) ? $"output-{RegistryName}" : _pendingName;
        Description = _pendingDescription;
        Make = _pendingMake;
        Model = _pendingModel;
        PhysicalSize = _pendingPhysical;
        CurrentMode = _pendingMode;
        Scale = scale;
        Transform = _pendingTransform;
        Position = _pendingPosition;
        var first = !IsReady;
        IsReady = true;
        if (first)
        {
            Ready?.Invoke();
        }
        else if (fields != OutputStateFields.None)
        {
            Committed?.Invoke(fields);
        }
    }
}
