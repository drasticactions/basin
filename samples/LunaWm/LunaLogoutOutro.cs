using Basin;
using Basin.Host;
using Basin.Scene;

namespace LunaWm;

internal sealed class LunaLogoutOutro : IHandoffOutro
{
    public const double EndScale = 0.55;

    private readonly Dictionary<LunaView, Shrink> _shrinks = [];
    private readonly Action<string> _report;

    public LunaLogoutOutro(Action<string> report)
    {
        _report = report;
    }

    public TimeSpan Duration { get; } = TimeSpan.FromMilliseconds(LunaMotion.CardMinimize.DurationMs);

    public void Begin(OutputView view)
    {
        var luna = LunaShell.ViewOf(view);
        if (_shrinks.ContainsKey(luna))
        {
            return;
        }

        var zoom = new SceneTransform(luna.Root);
        zoom.PlaceBelow(luna.Cards);
        luna.Cards.Reparent(zoom);
        SceneTree[] fade = [luna.Launcher, luna.QuickLaunch, luna.Dashboard, luna.Menus, luna.LayerTop, luna.LayerOverlay, luna.InputMethod];
        var from = new float[fade.Length];
        for (var i = 0; i < fade.Length; i++)
        {
            from[i] = fade[i].Alpha;
        }

        _shrinks[luna] = new Shrink(zoom, view.Box.Width / 2.0, view.Box.Height / 2.0, fade, from);
        _report($"OUTRO logout {view.Output.Name}");
    }

    public void Step(OutputView view, double progress)
    {
        if (!_shrinks.TryGetValue(LunaShell.ViewOf(view), out var shrink))
        {
            return;
        }

        var eased = QtEasing.Evaluate(LunaMotion.CardMinimize.Curve, Math.Clamp(progress, 0, 1));
        var scale = 1 - ((1 - EndScale) * eased);
        var remaining = (float)(1 - eased);
        shrink.Zoom.Matrix = new RenderTransform(scale, 0, shrink.CenterX * (1 - scale), 0, scale, shrink.CenterY * (1 - scale), 0, 0, 1);
        shrink.Zoom.Alpha = remaining;
        for (var i = 0; i < shrink.Fade.Length; i++)
        {
            shrink.Fade[i].Alpha = shrink.From[i] * remaining;
        }
    }

    public void End(OutputView view)
    {
        if (!_shrinks.TryGetValue(LunaShell.ViewOf(view), out var shrink))
        {
            return;
        }

        shrink.Zoom.Alpha = 0f;
        foreach (var tree in shrink.Fade)
        {
            tree.Alpha = 0f;
        }
    }

    private sealed record Shrink(SceneTransform Zoom, double CenterX, double CenterY, SceneTree[] Fade, float[] From);
}
