using Basin.Scene;
using Basin.Shell.River.Protocol;
using Wayland.Server;

namespace Basin.Shell.River;

internal sealed class RiverNode : IRiverNodeV1Handler
{
    private readonly RiverWindowManager _manager;

    internal RiverNode(RiverWindowManager manager, WlClient client, uint version, uint id, Func<SceneTree?> tree)
    {
        _manager = manager;
        Tree = tree;
        Resource = new RiverNodeV1Resource(client, version, id, this);
    }

    internal RiverNodeV1Resource Resource { get; }

    internal Func<SceneTree?> Tree { get; }

    internal Point? RequestedPosition { get; private set; }

    void IRiverNodeV1Handler.SetPosition(RiverNodeV1Resource resource, in RiverNodeV1Resource.SetPositionEventArgs args)
    {
        if (_manager.EnsureRendering())
        {
            RequestedPosition = new Point(args.X, args.Y);
        }
    }

    void IRiverNodeV1Handler.PlaceTop(RiverNodeV1Resource resource)
    {
        if (_manager.EnsureRendering())
        {
            _manager.RenderOrder.PlaceTop(this);
        }
    }

    void IRiverNodeV1Handler.PlaceBottom(RiverNodeV1Resource resource)
    {
        if (_manager.EnsureRendering())
        {
            _manager.RenderOrder.PlaceBottom(this);
        }
    }

    void IRiverNodeV1Handler.PlaceAbove(RiverNodeV1Resource resource, in RiverNodeV1Resource.PlaceAboveEventArgs args)
    {
        if (_manager.EnsureRendering() && _manager.ResolveNode(args.Other) is { } other)
        {
            _manager.RenderOrder.PlaceAbove(this, other);
        }
    }

    void IRiverNodeV1Handler.PlaceBelow(RiverNodeV1Resource resource, in RiverNodeV1Resource.PlaceBelowEventArgs args)
    {
        if (_manager.EnsureRendering() && _manager.ResolveNode(args.Other) is { } other)
        {
            _manager.RenderOrder.PlaceBelow(this, other);
        }
    }

    void IRiverNodeV1Handler.Destroy(RiverNodeV1Resource resource) => _manager.ForgetNode(this);
}
