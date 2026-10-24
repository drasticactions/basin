using Basin.Scene;
using Basin.Shell.River.Protocol;
using Wayland.Server;

namespace Basin.Shell.River;

internal sealed class RiverDecoration : IRiverDecorationV1Handler
{
    internal const string RoleName = "river_decoration_v1";

    private readonly RiverWindowManager _manager;
    private readonly RiverDecorationV1Resource _resource;

    internal RiverDecoration(
        RiverWindowManager manager,
        WlClient client,
        uint version,
        uint id,
        RiverWindow window,
        Surface surface,
        SceneTree parent,
        bool above)
    {
        _manager = manager;
        Window = window;
        Surface = surface;
        IsAbove = above;
        Tree = new SceneTree(parent);
        Scene = new SceneSurface(Tree, surface);
        _resource = new RiverDecorationV1Resource(client, version, id, this);

        surface.Destroyed += Destroy;
        surface.CommitRequested += () => AwaitingCommit = false;
    }

    internal RiverWindow Window { get; }

    internal Surface Surface { get; }

    internal SceneSurface Scene { get; }

    internal SceneTree Tree { get; }

    internal bool IsAbove { get; }

    internal Point Offset { get; private set; }

    internal bool IsDestroyed { get; private set; }

    internal bool AwaitingCommit { get; set; }

    internal void PostMissingCommit() => _resource.PostError(
        (uint)RiverDecorationV1.Error.NoCommit,
        "No wl_surface.commit came between sync_next_commit and render_finish.");

    internal void Destroy()
    {
        if (IsDestroyed)
        {
            return;
        }

        IsDestroyed = true;
        Surface.Destroyed -= Destroy;
        _manager.ForgetDecoration(this);
        Scene.Destroy();
        Tree.Destroy();
    }

    void IRiverDecorationV1Handler.SetOffset(RiverDecorationV1Resource resource, in RiverDecorationV1Resource.SetOffsetEventArgs args)
    {
        if (_manager.EnsureRendering())
        {
            Offset = new Point(args.X, args.Y);
        }
    }

    void IRiverDecorationV1Handler.SyncNextCommit(RiverDecorationV1Resource resource)
    {
        if (_manager.EnsureRendering())
        {
            _manager.HoldNextCommit(this);
        }
    }

    void IRiverDecorationV1Handler.Destroy(RiverDecorationV1Resource resource) => Destroy();
}
