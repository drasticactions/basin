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
    private readonly OpenCloseRunner _runner = new();
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

    public bool IsRunning => _runner.IsRunning || _fading;

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

        var curve = kind == ShellAnimation.Zoom ? ZoomCurve : FadeCurve;
        _runner.BeginClose(window.Tree, parent, (snapshot, stack) =>
        {
            snapshot.Tree.SetPosition(window.X, window.Y);
            var animation = new OpenCloseAnimation(
                kind == ShellAnimation.Zoom ? OpenCloseKind.Zoom : OpenCloseKind.Fade,
                curve);
            animation.Begin(stack, hiding: true, DurationOf(curve));
            return animation.Step;
        });
        Changed?.Invoke();
    }

    public void Step(in FrameTick tick)
    {
        if (_disposed)
        {
            return;
        }

        _runner.Step(tick);

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
        _runner.Dispose();
        _curtain?.Destroy();
        _curtain = null;
    }

    private void Begin(ShellWindow window, OpenCloseKind kind, bool hiding)
    {
        var curve = kind == OpenCloseKind.Zoom ? ZoomCurve : FadeCurve;
        _runner.BeginOpen(window.Tree, stack =>
        {
            var animation = new OpenCloseAnimation(kind, curve);
            animation.Begin(stack, hiding, DurationOf(curve));
            return animation.Step;
        });
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
