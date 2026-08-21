using Basin.Scene;
using Basin.Shell.River.Protocol;

namespace Basin.Shell.River;

internal sealed class RiverNode
{
    private readonly RiverWindowManager _manager;

    internal RiverNode(RiverWindowManager manager, RiverNodeV1Resource resource, Func<SceneTree?> tree)
    {
        _manager = manager;
        Resource = resource;
        Tree = tree;

        resource.SetPosition += (_, e) =>
        {
            if (_manager.EnsureRendering())
            {
                RequestedPosition = new Point(e.X, e.Y);
            }
        };
        resource.PlaceTop += (_, _) =>
        {
            if (_manager.EnsureRendering())
            {
                _manager.RenderOrder.PlaceTop(this);
            }
        };
        resource.PlaceBottom += (_, _) =>
        {
            if (_manager.EnsureRendering())
            {
                _manager.RenderOrder.PlaceBottom(this);
            }
        };
        resource.PlaceAbove += (_, e) =>
        {
            if (_manager.EnsureRendering() && _manager.ResolveNode(e.Other) is { } other)
            {
                _manager.RenderOrder.PlaceAbove(this, other);
            }
        };
        resource.PlaceBelow += (_, e) =>
        {
            if (_manager.EnsureRendering() && _manager.ResolveNode(e.Other) is { } other)
            {
                _manager.RenderOrder.PlaceBelow(this, other);
            }
        };
        resource.DestroyRequest += (_, _) => _manager.ForgetNode(this);
    }

    internal RiverNodeV1Resource Resource { get; }

    internal Func<SceneTree?> Tree { get; }

    internal Point? RequestedPosition { get; private set; }
}
