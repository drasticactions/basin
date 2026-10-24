using Basin.Scene;

namespace Basin.Host;

/// <summary>
/// Fades a set of scene trees from their current alpha to zero on a cubic ease-out before the compositor retains its last frame.
/// </summary>
public sealed class WindowsFadeOutro : IHandoffOutro
{
    private readonly SceneTree[] _fade;
    private readonly float[] _from;
    private bool _begun;

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowsFadeOutro"/> class that fades the windows, top and overlay layers. The background, bottom and lock layers stay.
    /// </summary>
    /// <param name="layers">The compositor's scene layers.</param>
    /// <param name="duration">The length of the fade.</param>
    /// <exception cref="ArgumentNullException"><paramref name="layers"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="duration"/> is negative.</exception>
    public WindowsFadeOutro(SceneLayers layers, TimeSpan duration)
        : this(duration, Layers(layers))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowsFadeOutro"/> class that fades the given trees.
    /// </summary>
    /// <param name="duration">The length of the fade.</param>
    /// <param name="fade">The trees to fade.</param>
    /// <exception cref="ArgumentNullException"><paramref name="fade"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="duration"/> is negative.</exception>
    public WindowsFadeOutro(TimeSpan duration, params SceneTree[] fade)
    {
        ArgumentNullException.ThrowIfNull(fade);
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);
        Duration = duration;
        _fade = fade;
        _from = new float[fade.Length];
    }

    /// <inheritdoc/>
    public TimeSpan Duration { get; }

    /// <summary>
    /// Records the alpha of each tree. Only the first call has an effect, because the trees are shared by all outputs.
    /// </summary>
    /// <param name="view">The primary output. The fade does not use it.</param>
    public void Begin(OutputView view)
    {
        if (_begun)
        {
            return;
        }

        _begun = true;
        for (var i = 0; i < _fade.Length; i++)
        {
            _from[i] = _fade[i].Alpha;
        }
    }

    /// <summary>
    /// Sets each tree's alpha to its recorded alpha times the remaining part of the eased fade.
    /// </summary>
    /// <param name="view">The output that repaints. The fade does not use it.</param>
    /// <param name="progress">The progress. Values outside [0, 1] are clamped.</param>
    public void Step(OutputView view, double progress)
    {
        var remaining = (float)(1 - CrossfadeTransition.OutCubic(Math.Clamp(progress, 0, 1)));
        for (var i = 0; i < _fade.Length; i++)
        {
            _fade[i].Alpha = _from[i] * remaining;
        }
    }

    /// <summary>
    /// Sets the alpha of each tree to zero.
    /// </summary>
    /// <param name="view">The primary output. The fade does not use it.</param>
    public void End(OutputView view)
    {
        foreach (var tree in _fade)
        {
            tree.Alpha = 0f;
        }
    }

    private static SceneTree[] Layers(SceneLayers layers)
    {
        ArgumentNullException.ThrowIfNull(layers);
        return [layers.Windows, layers.Top, layers.Overlay];
    }
}
