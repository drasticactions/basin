using Basin.Diagnostics;
using Basin.Scene;

namespace Basin.Effects;

public sealed class FeedbackOverlay : IDisposable
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly SceneTree _tree;
    private readonly Dictionary<object, SceneMesh> _meshes = [];
    private bool _disposed;

    public FeedbackOverlay(SceneTree layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        _tree = new SceneTree(layer);
        BasinCounters.Track();
    }

    public SceneTree Tree => _tree;

    public SceneMesh Claim(object owner, IMeshSource source)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(source);
        _thread.Assert();
        if (_meshes.TryGetValue(owner, out var existing) && !existing.IsDestroyed)
        {
            existing.Source = source;
            return existing;
        }

        var mesh = new SceneMesh(_tree) { Source = source };
        _meshes[owner] = mesh;
        return mesh;
    }

    public void Release(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _thread.Assert();
        if (_meshes.Remove(owner, out var mesh) && !mesh.IsDestroyed)
        {
            mesh.Destroy();
        }
    }

    public void Dispose()
    {
        _thread.Assert();
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        BasinCounters.Untrack();
        foreach (var mesh in _meshes.Values)
        {
            if (!mesh.IsDestroyed)
            {
                mesh.Destroy();
            }
        }

        _meshes.Clear();
        if (!_tree.IsDestroyed)
        {
            _tree.Destroy();
        }
    }
}
