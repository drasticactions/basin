using Basin.Scene;
using Basin.Shell.River.Protocol;

namespace Basin.Shell.River;

internal sealed class RiverDecoration
{
    internal const string RoleName = "river_decoration_v1";

    private readonly RiverWindowManager _manager;
    private readonly RiverDecorationV1Resource _resource;

    internal RiverDecoration(
        RiverWindowManager manager,
        RiverDecorationV1Resource resource,
        RiverWindow window,
        Surface surface,
        SceneTree parent,
        bool above)
    {
        _manager = manager;
        _resource = resource;
        Window = window;
        Surface = surface;
        IsAbove = above;
        Tree = new SceneTree(parent);
        Scene = new SceneSurface(Tree, surface);

        resource.SetOffset += (_, e) =>
        {
            if (_manager.EnsureRendering())
            {
                Offset = new Point(e.X, e.Y);
            }
        };

        resource.SyncNextCommit += (_, _) =>
        {
            if (_manager.EnsureRendering())
            {
                _manager.HoldNextCommit(this);
            }
        };

        resource.DestroyRequest += (_, _) => Destroy();
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
        "sync_next_commit was not followed by a wl_surface.commit before render_finish");

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
}
