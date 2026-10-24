using Basin.Diagnostics;
using Basin.Scene;

namespace Basin.Effects;

/// <summary>
/// Holds one mesh per owner in a tree of its own, for input visualizers that draw above the shell.
/// </summary>
public sealed class FeedbackOverlay : IDisposable
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly SceneTree _tree;
    private readonly Dictionary<object, SceneMesh> _meshes = [];
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="FeedbackOverlay"/> class and creates its tree under a layer.
    /// </summary>
    /// <param name="layer">The parent layer, usually <see cref="SceneLayers.Feedback"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="layer"/> is <see langword="null"/>.</exception>
    public FeedbackOverlay(SceneTree layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        _tree = new SceneTree(layer);
        BasinCounters.Track();
    }

    /// <summary>
    /// Gets the tree that holds every mesh of the overlay. <see cref="Dispose"/> destroys it.
    /// </summary>
    public SceneTree Tree => _tree;

    /// <summary>
    /// Returns the mesh of an owner with a new source. The method creates the mesh when the owner has none or its mesh was destroyed.
    /// </summary>
    /// <param name="owner">The key of the visualizer that owns the mesh.</param>
    /// <param name="source">The source that writes the vertices.</param>
    /// <returns>The mesh, which the overlay continues to own.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="owner"/> or <paramref name="source"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Destroys the mesh of an owner. An owner with no mesh is ignored.
    /// </summary>
    /// <param name="owner">The key that <see cref="Claim"/> took.</param>
    /// <exception cref="ArgumentNullException"><paramref name="owner"/> is <see langword="null"/>.</exception>
    public void Release(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _thread.Assert();
        if (_meshes.Remove(owner, out var mesh) && !mesh.IsDestroyed)
        {
            mesh.Destroy();
        }
    }

    /// <summary>
    /// Destroys every mesh and the tree. A second call does nothing.
    /// </summary>
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
