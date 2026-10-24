using Basin.Scene;
using Basin.Shell.River.Protocol;
using Wayland.Server;

namespace Basin.Shell.River;

internal sealed class RiverShellSurface : IRiverShellSurfaceV1Handler
{
    internal const string RoleName = "river_shell_surface_v1";

    private readonly RiverWindowManager _manager;
    private readonly RiverShellSurfaceV1Resource _resource;
    private RiverNode? _node;

    internal RiverShellSurface(
        RiverWindowManager manager,
        WlClient client,
        uint version,
        uint id,
        Surface surface,
        SceneTree parent)
    {
        _manager = manager;
        Surface = surface;
        Tree = new SceneTree(parent);
        Scene = new SceneSurface(Tree, surface);
        _resource = new RiverShellSurfaceV1Resource(client, version, id, this);

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
        "No wl_surface.commit came between sync_next_commit and render_finish.");

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

    void IRiverShellSurfaceV1Handler.GetNode(RiverShellSurfaceV1Resource resource, in RiverShellSurfaceV1Resource.GetNodeEventArgs args)
    {
        if (_node is not null)
        {
            resource.PostError(
                (uint)RiverShellSurfaceV1.Error.NodeExists,
                "This shell surface already has a node.");
            return;
        }

        _node = new RiverNode(_manager, resource.Client, resource.Version, args.Id, () => Tree);
        _manager.RegisterNode(_node);
    }

    void IRiverShellSurfaceV1Handler.SyncNextCommit(RiverShellSurfaceV1Resource resource)
    {
        if (_manager.EnsureRendering())
        {
            _manager.HoldNextCommit(this);
        }
    }

    void IRiverShellSurfaceV1Handler.Destroy(RiverShellSurfaceV1Resource resource) => Destroy();
}
