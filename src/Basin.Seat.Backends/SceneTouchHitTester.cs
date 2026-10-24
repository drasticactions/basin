using Basin.Scene;

namespace Basin.Seat.Backends;

/// <summary>
/// Finds the client surface under a touch in a <see cref="Scene.Scene"/>. The hit token is the scene node of the surface.
/// </summary>
public sealed class SceneTouchHitTester : ITouchHitTester
{
    private readonly Scene.Scene _scene;

    /// <summary>
    /// Initializes a new instance of the <see cref="SceneTouchHitTester"/> class.
    /// </summary>
    /// <param name="scene">The scene to hit-test.</param>
    /// <exception cref="ArgumentNullException"><paramref name="scene"/> is <see langword="null"/>.</exception>
    public SceneTouchHitTester(Scene.Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        _scene = scene;
    }

    /// <summary>
    /// Gets or sets a callback that makes every hit fail while it returns <see langword="true"/>, for example during an overview.
    /// </summary>
    public Func<bool>? Suppressed { get; set; }

    /// <inheritdoc/>
    public bool TryHit(double layoutX, double layoutY, out TouchHit hit)
    {
        if (Suppressed?.Invoke() != true &&
            _scene.SurfaceAt(layoutX, layoutY) is { Surface: { } surface } at)
        {
            hit = new TouchHit(surface, at.X, at.Y, at.Node);
            return true;
        }

        hit = default;
        return false;
    }

    /// <inheritdoc/>
    public bool TryMap(object? token, double layoutX, double layoutY, out double localX, out double localY)
    {
        if (token is SceneNode { IsDestroyed: false } node &&
            node.TryMapSceneToLocal(layoutX, layoutY, out localX, out localY))
        {
            return true;
        }

        localX = 0;
        localY = 0;
        return false;
    }
}
