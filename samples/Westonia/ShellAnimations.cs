using Basin;
using Basin.Effects;
using Basin.Scene;

namespace Westonia;

internal sealed class ShellAnimations : IDisposable
{
    private static readonly EasingCurve FadeCurve = EasingCurve.Spring(1000.0, 4000.0, 0.1);

    private static readonly EasingCurve ZoomCurve = EasingCurve.Spring(300.0, 1400.0, 0.03);

    private readonly ShellLayers _layers;
    private readonly WestonIni _ini;
    private readonly List<(ShellWindow Window, TransformStack Stack, OpenCloseAnimation Animation)> _running = [];
    private readonly List<(SceneSnapshot Snapshot, TransformStack Stack, OpenCloseAnimation Animation)> _closing = [];
    private SceneRect? _curtain;
    private Spring _sessionFade;
    private bool _fading;
    private bool _fadingOut;
    private bool _disposed;

    public ShellAnimations(ShellLayers layers, WestonIni ini)
    {
        _layers = layers;
        _ini = ini;
    }

    public Func<Box>? Area { get; set; }

    public Action? Changed { get; set; }

    public bool IsRunning => _running.Count > 0 || _closing.Count > 0 || _fading;

    public void BeginSessionFade(bool toBlack)
    {
        EnsureCurtain();
        _fadingOut = toBlack;
        _sessionFade = new Spring(150.0, toBlack ? 0.0 : 1.0, toBlack ? 1.0 : 0.0) { Clip = SpringClip.Clamp };
        _fading = true;
        ApplyCurtain(toBlack ? 0.0 : 1.0);
    }

    public void BeginMap(ShellWindow window)
    {
        var kind = _ini.Shell.Animation;
        if (kind is ShellAnimation.None or ShellAnimation.DimLayer)
        {
            return;
        }

        Begin(window, kind == ShellAnimation.Zoom ? OpenCloseKind.Zoom : OpenCloseKind.Fade, hiding: false);
    }

    public void BeginUnmap(ShellWindow window)
    {
        var kind = _ini.Shell.CloseAnimation;
        if (kind is ShellAnimation.None or ShellAnimation.DimLayer || window.Tree.IsDestroyed)
        {
            return;
        }

        var parent = window.Tree.Parent;
        if (parent is null)
        {
            return;
        }

        var snapshot = SceneSnapshot.Capture(window.Tree, parent);
        snapshot.Tree.SetPosition(window.X, window.Y);
        var stack = new TransformStack(snapshot.Tree);
        var curve = kind == ShellAnimation.Zoom ? ZoomCurve : FadeCurve;
        var animation = new OpenCloseAnimation(
            kind == ShellAnimation.Zoom ? OpenCloseKind.Zoom : OpenCloseKind.Fade,
            curve);
        animation.Begin(stack, hiding: true, DurationOf(curve));
        _closing.Add((snapshot, stack, animation));
        Changed?.Invoke();
    }

    public void Step(in FrameTick tick)
    {
        if (_disposed)
        {
            return;
        }

        for (var i = _running.Count - 1; i >= 0; i--)
        {
            var entry = _running[i];
            if (!entry.Animation.Step(entry.Stack, tick))
            {
                _running.RemoveAt(i);
            }
        }

        for (var i = _closing.Count - 1; i >= 0; i--)
        {
            var entry = _closing[i];
            if (!entry.Animation.Step(entry.Stack, tick))
            {
                entry.Snapshot.Dispose();
                _closing.RemoveAt(i);
            }
        }

        if (_fading)
        {
            _sessionFade.Update(tick.TargetPresentNanos);
            ApplyCurtain(Math.Clamp(_sessionFade.Current, 0.0, 1.0));
            if (_sessionFade.IsDone)
            {
                _fading = false;
                if (!_fadingOut)
                {
                    _curtain?.Destroy();
                    _curtain = null;
                }
            }
        }

        if (IsRunning)
        {
            Changed?.Invoke();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        foreach (var entry in _closing)
        {
            entry.Snapshot.Dispose();
        }

        _closing.Clear();
        _running.Clear();
        _curtain?.Destroy();
        _curtain = null;
    }

    private void Begin(ShellWindow window, OpenCloseKind kind, bool hiding)
    {
        var stack = new TransformStack(window.Tree);
        var curve = kind == OpenCloseKind.Zoom ? ZoomCurve : FadeCurve;
        var animation = new OpenCloseAnimation(kind, curve);
        animation.Begin(stack, hiding, DurationOf(curve));
        _running.Add((window, stack, animation));
        Changed?.Invoke();
    }

    private static long DurationOf(EasingCurve curve) =>
        (long)(Math.Max(1.0, curve.SettleMillis) * 1_000_000.0);

    private void EnsureCurtain()
    {
        if (_curtain is { IsDestroyed: false })
        {
            return;
        }

        var box = Area?.Invoke() ?? new Box(0, 0, 1280, 720);
        _curtain = new SceneRect(_layers.Cursor, box.Width, box.Height, new RenderColor(0f, 0f, 0f, 0f));
        _curtain.SetPosition(box.X, box.Y);
        _curtain.LowerToBottom();
    }

    private void ApplyCurtain(double alpha)
    {
        if (_curtain is { IsDestroyed: false } curtain)
        {
            curtain.Color = new RenderColor(0f, 0f, 0f, (float)Math.Clamp(alpha, 0, 1));
        }
    }
}
