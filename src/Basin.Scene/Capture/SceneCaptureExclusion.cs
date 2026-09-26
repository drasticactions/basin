using Basin.Capabilities;

namespace Basin.Scene;

public sealed class SceneCaptureExclusion : ICaptureExclusion
{
    private readonly Scene _scene;

    public SceneCaptureExclusion(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        _scene = scene;
    }

    public Func<bool>? KeyboardFocusExcluded { get; set; }

    public bool IsKeyboardFocusExcluded => KeyboardFocusExcluded?.Invoke() ?? false;

    public bool IsExcludedAt(double x, double y) => _scene.IsCaptureExcludedAt(x, y);
}
