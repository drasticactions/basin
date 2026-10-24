using Basin.Shell.River.Protocol;
using Wayland.Server;

namespace Basin.Shell.River;

internal sealed class RiverOutput : IRiverOutputV1Handler
{
    private readonly RiverWindowManager _manager;
    private Point _sentPosition;
    private Size _sentDimensions;
    private bool _positionSent;
    private bool _dimensionsSent;
    private bool _wlOutputSent;
    private uint _sentCaptureSessions;
    private bool _captureSessionsSent;

    internal RiverOutput(RiverWindowManager manager, OutputGlobal global)
    {
        _manager = manager;
        Global = global;
    }

    internal OutputGlobal Global { get; }

    internal IOutput Output => Global.Output;

    internal RiverOutputV1Resource? Resource { get; private set; }

    internal Point Position { get; set; }

    internal Size Dimensions { get; set; }

    internal int Width => Dimensions.Width;

    internal int Height => Dimensions.Height;

    internal bool IsRemoved { get; private set; }

    internal RiverOutputV1.PresentationMode PresentationMode { get; private set; }

    internal uint CaptureSessions { get; set; }

    internal void NotifyOutputCommitted(OutputStateFields fields)
    {
        var mode = Output.CurrentMode;
        var scale = Output.Scale;
        if (mode == _lastMode && Math.Abs(scale - _lastScale) < 0.0001)
        {
            return;
        }

        _lastMode = mode;
        _lastScale = scale;
        _manager.MarkManageDirty();
    }

    private OutputMode _lastMode;
    private double _lastScale;

    internal RiverOutputV1Resource Bind(WlClient client, uint version, uint id)
    {
        var resource = new RiverOutputV1Resource(client, version, id, this);
        Resource = resource;
        _positionSent = false;
        _dimensionsSent = false;
        _wlOutputSent = false;
        _captureSessionsSent = false;
        return resource;
    }

    void IRiverOutputV1Handler.SetPresentationMode(RiverOutputV1Resource resource, in RiverOutputV1Resource.SetPresentationModeEventArgs args)
    {
        if (!_manager.EnsureRendering())
        {
            return;
        }

        if (args.Mode is not (RiverOutputV1.PresentationMode.Vsync or RiverOutputV1.PresentationMode.Async))
        {
            resource.PostError(
                (uint)RiverOutputV1.Error.InvalidPresentationMode,
                $"Presentation mode {(uint)args.Mode} is not a known value.");
            return;
        }

        PresentationMode = args.Mode;
    }

    void IRiverOutputV1Handler.Destroy(RiverOutputV1Resource resource)
    {
        Resource = null;
        _manager.ForgetOutputResource(resource);
    }

    internal void SendChanges(uint version)
    {
        if (Resource is not { IsDestroyed: false } resource)
        {
            return;
        }

        if (!_wlOutputSent)
        {
            _wlOutputSent = true;
            resource.SendWlOutput(Global.NameFor(resource.Client));
        }

        if (!_positionSent || _sentPosition != Position)
        {
            _positionSent = true;
            _sentPosition = Position;
            resource.SendPosition(Position.X, Position.Y);
        }

        if (!_dimensionsSent || _sentDimensions != Dimensions)
        {
            _dimensionsSent = true;
            _sentDimensions = Dimensions;
            resource.SendDimensions(Dimensions.Width, Dimensions.Height);
        }

        if (version >= 5 && (!_captureSessionsSent || _sentCaptureSessions != CaptureSessions))
        {
            _captureSessionsSent = true;
            _sentCaptureSessions = CaptureSessions;
            resource.SendCaptureSessions(CaptureSessions);
        }
    }

    internal void SendRemoved()
    {
        IsRemoved = true;
        if (Resource is { IsDestroyed: false } resource)
        {
            resource.SendRemoved();
        }
    }

    internal void ResetForNewManager()
    {
        Resource = null;
        PresentationMode = RiverOutputV1.PresentationMode.Vsync;
    }
}
