using Basin.Capabilities;

namespace Basin.Scene;

/// <summary>
/// Answers capture-exclusion questions from the scene, and the keyboard-focus question through a callback.
/// </summary>
public sealed class SceneCaptureExclusion : ICaptureExclusion
{
    private readonly Scene _scene;

    /// <summary>
    /// Initializes a new instance of the <see cref="SceneCaptureExclusion"/> class.
    /// </summary>
    /// <param name="scene">The scene to hit-test.</param>
    /// <exception cref="ArgumentNullException"><paramref name="scene"/> is <see langword="null"/>.</exception>
    public SceneCaptureExclusion(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        _scene = scene;
    }

    /// <summary>
    /// Gets or sets the callback that tells whether the focused surface is excluded from capture. <see langword="null"/> means not excluded.
    /// </summary>
    public Func<bool>? KeyboardFocusExcluded { get; set; }

    /// <inheritdoc/>
    public bool IsKeyboardFocusExcluded => KeyboardFocusExcluded?.Invoke() ?? false;

    /// <inheritdoc/>
    public bool IsExcludedAt(double x, double y) => _scene.IsCaptureExcludedAt(x, y);
}
