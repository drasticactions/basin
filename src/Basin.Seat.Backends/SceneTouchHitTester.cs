using Basin.Scene;

namespace Basin.Seat.Backends;

public sealed class SceneTouchHitTester : ITouchHitTester
{
    private readonly Scene.Scene _scene;

    public SceneTouchHitTester(Scene.Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        _scene = scene;
    }

    public Func<bool>? Suppressed { get; set; }

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
