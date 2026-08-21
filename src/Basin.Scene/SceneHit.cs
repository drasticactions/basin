namespace Basin.Scene;

public readonly record struct SceneHit(SceneNode Node, double X, double Y)
{
    public Surface? Surface => (Node as SceneBuffer)?.InputSurface;
}
