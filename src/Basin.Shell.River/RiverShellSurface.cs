using Basin.Scene;
using Basin.Shell.River.Protocol;

namespace Basin.Shell.River;

internal sealed class RiverShellSurface
{
    internal const string RoleName = "river_shell_surface_v1";

    private readonly RiverWindowManager _manager;
    private readonly RiverShellSurfaceV1Resource _resource;
    private RiverNode? _node;

    internal RiverShellSurface(
        RiverWindowManager manager,
        RiverShellSurfaceV1Resource resource,
        Surface surface,
        SceneTree parent)
    {
        _manager = manager;
        _resource = resource;
        Surface = surface;
        Tree = new SceneTree(parent);
        Scene = new SceneSurface(Tree, surface);

        resource.GetNode += (_, e) =>
        {
            if (_node is not null)
            {
                resource.PostError(
                    (uint)RiverShellSurfaceV1.Error.NodeExists,
                    "this shell surface already has a node");
                return;
            }

            var nodeResource = new RiverNodeV1Resource(resource.Client, resource.Version, e.Id);
            _node = new RiverNode(_manager, nodeResource, () => Tree);
            _manager.RegisterNode(_node);
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

    internal Surface Surface { get; }

    internal RiverShellSurfaceV1Resource Resource => _resource;

    internal SceneSurface Scene { get; }

    internal SceneTree Tree { get; }

    internal RiverNode? Node => _node;

    internal bool IsDestroyed { get; private set; }

    internal bool AwaitingCommit { get; set; }

    internal void PostMissingCommit() => _resource.PostError(
        (uint)RiverShellSurfaceV1.Error.NoCommit,
        "sync_next_commit was not followed by a wl_surface.commit before render_finish");

    internal void Destroy()
    {
        if (IsDestroyed)
        {
            return;
        }

        IsDestroyed = true;
        Surface.Destroyed -= Destroy;
        if (_node is { } node)
        {
            _manager.ForgetNode(node);
            _node = null;
        }

        _manager.ForgetShellSurface(this);
        Scene.Destroy();
        Tree.Destroy();
    }
}
