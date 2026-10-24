using Basin.Capabilities;

namespace Basin.Scene;

/// <summary>
/// The <see cref="IScreenCurtain"/> of a scene. Its outputs present black, and captures, screenshots and screencasts of the scene are black, while the curtain is drawn.
/// </summary>
public sealed class SceneCurtain : IScreenCurtain
{
    private readonly Scene _scene;
    private int _passes;

    /// <summary>
    /// Initializes a new instance of the <see cref="SceneCurtain"/> class and makes it the curtain of the scene. The curtain starts lifted.
    /// </summary>
    /// <param name="scene">The scene.</param>
    public SceneCurtain(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        _scene = scene;
        scene.Curtain = this;
    }

    /// <inheritdoc/>
    public bool IsDrawn { get; private set; }

    /// <inheritdoc/>
    public bool CapturesSee => !IsDrawn || _passes > 0;

    /// <inheritdoc/>
    public event Action? Changed;

    /// <inheritdoc/>
    public void Set(bool drawn)
    {
        if (drawn == IsDrawn)
        {
            return;
        }

        IsDrawn = drawn;
        foreach (var output in _scene.Outputs)
        {
            output.Repaint();
        }

        _scene.NotifyDamage(null, new Box(-1_000_000, -1_000_000, 2_000_000, 2_000_000));

        Changed?.Invoke();
    }

    /// <inheritdoc/>
    public void BeginPass() => _passes++;

    /// <inheritdoc/>
    public void EndPass() => _passes = Math.Max(0, _passes - 1);
}
