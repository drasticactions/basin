using Basin.Scene;

namespace Basin.Host;

public sealed class WindowsFadeOutro : IHandoffOutro
{
    private readonly SceneTree[] _fade;
    private readonly float[] _from;
    private bool _begun;

    public WindowsFadeOutro(SceneLayers layers, TimeSpan duration)
        : this(duration, Layers(layers))
    {
    }

    public WindowsFadeOutro(TimeSpan duration, params SceneTree[] fade)
    {
        ArgumentNullException.ThrowIfNull(fade);
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);
        Duration = duration;
        _fade = fade;
        _from = new float[fade.Length];
    }

    public TimeSpan Duration { get; }

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

    public void Step(OutputView view, double progress)
    {
        var remaining = (float)(1 - CrossfadeTransition.OutCubic(Math.Clamp(progress, 0, 1)));
        for (var i = 0; i < _fade.Length; i++)
        {
            _fade[i].Alpha = _from[i] * remaining;
        }
    }

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
