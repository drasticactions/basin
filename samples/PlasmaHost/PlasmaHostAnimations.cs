using Basin;
using Basin.Effects;
using Basin.Scene;

namespace PlasmaHost;

internal sealed class PlasmaHostAnimations
{
    private static readonly string[] Blacklist =
        ["ksmserver", "ksmserver-logout-greeter", "ksplashqml", "org.kde.ksplashqml"];

    private readonly KwinEffectsConfig _config;
    private readonly OpenCloseRunner _runner = new();
    private readonly HashSet<PlasmaHostView> _modal = [];
    private readonly List<Minimizing> _minimizings = [];
    private readonly List<Stretching> _stretchings = [];
    private FrameTick _last;

    public PlasmaHostAnimations(KwinEffectsConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _config = config;
        ScaleEnabled = config.IsEnabled("scale", true);
        GlideEnabled = config.IsEnabled("glide", false);
        SheetEnabled = config.IsEnabled("sheet", false);
        FadeEnabled = config.IsEnabled("fade", false);
        FallApartEnabled = config.IsEnabled("fallapart", false);
        SquashEnabled = config.IsEnabled("squash", true);
        MagicLampEnabled = config.IsEnabled("magiclamp", false);
        StretchEnabled = config.IsEnabled("maximize", true);
        FullscreenStretchEnabled = config.IsEnabled("fullscreen", true);
    }

    public bool ScaleEnabled { get; }

    public bool GlideEnabled { get; }

    public bool SheetEnabled { get; }

    public bool FadeEnabled { get; }

    public bool FallApartEnabled { get; }

    public bool SquashEnabled { get; }

    public bool MagicLampEnabled { get; }

    public bool StretchEnabled { get; }

    public bool FullscreenStretchEnabled { get; }

    public bool IsRunning => _runner.IsRunning || _minimizings.Count > 0 || _stretchings.Count > 0;

    public Func<PlasmaHostView, Box>? IconGeometry { get; set; }

    public Func<PlasmaHostView, MinimizeEdge>? IconEdge { get; set; }

    public Func<PlasmaHostView, Box>? WindowGeometry { get; set; }

    public Func<PlasmaHostView, Box>? WindowFrame { get; set; }

    public Func<PlasmaHostView, int>? ParentTop { get; set; }

    public Func<(double X, double Y)>? CursorPosition { get; set; }

    public event Action<PlasmaHostView>? MinimizeSettled;

    public void SetModal(PlasmaHostView view, bool modal)
    {
        if (modal)
        {
            _modal.Add(view);
        }
        else
        {
            _modal.Remove(view);
        }
    }

    public void Forget(PlasmaHostView view)
    {
        _runner.Forget(view.Tree);
        _modal.Remove(view);
        for (var i = _stretchings.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_stretchings[i].View, view))
            {
                _stretchings[i].Stretch.Release();
                _stretchings.RemoveAt(i);
            }
        }

        for (var i = _minimizings.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_minimizings[i].View, view))
            {
                _minimizings.RemoveAt(i);
            }
        }
    }

    public bool OnMapped(PlasmaHostView view)
    {
        if (view.Tree.IsDestroyed || !IsAnimatable(view))
        {
            return false;
        }

        return _runner.BeginOpen(view.Tree, stack =>
        {
            if (SheetEnabled && _modal.Contains(view))
            {
                var sheet = new SheetEffect();
                var drop = (WindowGeometry?.Invoke(view).Y ?? view.Tree.Y) - (ParentTop?.Invoke(view) ?? view.Tree.Y);
                if (sheet.Begin(stack, hiding: false, drop, _config.Duration(SheetMillis)))
                {
                    return sheet.Step;
                }
            }

            if (GlideEnabled)
            {
                var glide = new GlideEffect(GlideOptions());
                if (glide.Begin(stack, hiding: false, _config.Duration(GlideMillis)))
                {
                    return glide.Step;
                }
            }

            if (ScaleEnabled || FadeEnabled)
            {
                var kind = ScaleEnabled ? OpenCloseKind.Zoom : OpenCloseKind.Fade;
                var animation = new OpenCloseAnimation(kind, ScaleEnabled ? EasingCurve.OutCubic : EasingCurve.Linear)
                {
                    InScale = _config.Number("scale", "InScale", 0.8),
                    OutScale = _config.Number("scale", "OutScale", 0.8),
                };
                var millis = ScaleEnabled ? _config.Integer("scale", "Duration", 200) : FadeMillis;
                if (animation.Begin(stack, hiding: false, _config.Duration(millis)))
                {
                    return animation.Step;
                }
            }

            return null;
        });
    }

    public bool OnClosing(PlasmaHostView view, SceneTree parent)
    {
        if (view.Tree.IsDestroyed || !IsAnimatable(view))
        {
            return false;
        }

        var modal = _modal.Contains(view);
        if (!SheetEnabled && !GlideEnabled && !ScaleEnabled && !FadeEnabled && !FallApartEnabled)
        {
            return false;
        }

        return _runner.BeginClose(view.Tree, parent, (snapshot, stack) =>
        {
            if (FallApartEnabled)
            {
                var fall = new FallApartEffect(
                    _config.Integer("fallapart", "BlockSize", FallApartEffect.DefaultBlockSize));
                if (fall.Begin(stack, _config.Duration(FallApartMillis)))
                {
                    return (TransformStack _, in FrameTick tick) => fall.Step(tick);
                }
            }

            if (SheetEnabled && modal)
            {
                var sheet = new SheetEffect();
                if (sheet.Begin(stack, hiding: true, 0, _config.Duration(SheetMillis)))
                {
                    return sheet.Step;
                }
            }

            if (GlideEnabled)
            {
                var glide = new GlideEffect(GlideOptions());
                if (glide.Begin(stack, hiding: true, _config.Duration(GlideMillis)))
                {
                    return glide.Step;
                }
            }

            if (ScaleEnabled || FadeEnabled)
            {
                var kind = ScaleEnabled ? OpenCloseKind.Zoom : OpenCloseKind.Fade;
                var animation = new OpenCloseAnimation(kind, ScaleEnabled ? EasingCurve.InCubic : EasingCurve.Linear)
                {
                    InScale = _config.Number("scale", "InScale", 0.8),
                    OutScale = _config.Number("scale", "OutScale", 0.8),
                };
                var millis = ScaleEnabled ? _config.Integer("scale", "Duration", 200) : FadeMillis;
                if (animation.Begin(stack, hiding: true, _config.Duration(millis)))
                {
                    return animation.Step;
                }
            }

            return null;
        });
    }

    public bool OnMinimize(PlasmaHostView view, bool minimized)
    {
        if (view.Tree.IsDestroyed || (!MagicLampEnabled && !SquashEnabled))
        {
            return false;
        }

        var window = WindowGeometry?.Invoke(view) ?? default;
        if (window.IsEmpty)
        {
            return false;
        }

        var icon = IconGeometry?.Invoke(view) ?? default;
        var edge = IconEdge?.Invoke(view) ?? MinimizeEdge.Bottom;
        if (icon.IsEmpty && CursorPosition is { } cursor)
        {
            var (x, y) = cursor();
            (icon, edge) = MagicLampEffect.FallbackTarget(window, x, y);
        }

        for (var i = _minimizings.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_minimizings[i].View, view))
            {
                _minimizings[i].Lamp?.End(_minimizings[i].Stack);
                _minimizings[i].Squash?.End(_minimizings[i].Stack);
                _minimizings.RemoveAt(i);
            }
        }

        var stack = _runner.StackFor(view.Tree);
        if (MagicLampEnabled)
        {
            var lamp = new MagicLampEffect();
            if (lamp.Begin(stack, window, icon, edge, restoring: !minimized, _config.Duration(MagicLampMillis)))
            {
                _minimizings.Add(new Minimizing { View = view, Stack = stack, Lamp = lamp, Hiding = minimized });
                return true;
            }
        }

        if (SquashEnabled && !icon.IsEmpty)
        {
            var squash = new SquashEffect();
            if (squash.Begin(stack, window, icon, restoring: !minimized, _config.Duration(SquashMillis)))
            {
                _minimizings.Add(new Minimizing { View = view, Stack = stack, Squash = squash, Hiding = minimized });
                return true;
            }
        }

        return false;
    }

    public bool OnMaximizeRequested(PlasmaHostView view, in Box from, in Box current) =>
        StretchEnabled && StretchRequested(view, from, current);

    public bool OnFullscreenRequested(PlasmaHostView view, in Box from, in Box current) =>
        FullscreenStretchEnabled && StretchRequested(view, from, current);

    private bool StretchRequested(PlasmaHostView view, in Box from, in Box current)
    {
        if (view.Tree.IsDestroyed)
        {
            return false;
        }

        DropStretch(view);
        var stack = _runner.StackFor(view.Tree);
        var stretch = new StretchEffect();
        if (!stretch.Capture(
            stack, FrameOf(view), from, current, view.Tree.X, view.Tree.Y, _config.Duration(StretchMillis)))
        {
            return false;
        }

        _stretchings.Add(new Stretching { View = view, Stack = stack, Stretch = stretch });
        return true;
    }

    public bool OnMaximized(PlasmaHostView view, in Box from, in Box to) =>
        StretchEnabled && StretchBegin(view, from, to);

    public bool OnFullscreened(PlasmaHostView view, in Box from, in Box to) =>
        FullscreenStretchEnabled && StretchBegin(view, from, to);

    private bool StretchBegin(PlasmaHostView view, in Box from, in Box to)
    {
        if (view.Tree.IsDestroyed)
        {
            return false;
        }

        foreach (var entry in _stretchings)
        {
            if (ReferenceEquals(entry.View, view))
            {
                return entry.Stretch.Begin(
                    entry.Stack, FrameOf(view), from, to, view.Tree.X, view.Tree.Y,
                    _config.Duration(StretchMillis));
            }
        }

        var stack = _runner.StackFor(view.Tree);
        var stretch = new StretchEffect();
        if (!stretch.Begin(
            stack, FrameOf(view), from, to, view.Tree.X, view.Tree.Y, _config.Duration(StretchMillis)))
        {
            return false;
        }

        _stretchings.Add(new Stretching { View = view, Stack = stack, Stretch = stretch });
        return true;
    }

    private void DropStretch(PlasmaHostView view)
    {
        for (var i = _stretchings.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_stretchings[i].View, view))
            {
                _stretchings[i].Stretch.End(_stretchings[i].Stack);
                _stretchings.RemoveAt(i);
            }
        }
    }

    public bool Step(in FrameTick tick)
    {
        if (tick.TargetPresentNanos <= _last.TargetPresentNanos)
        {
            return _runner.IsRunning || _minimizings.Count > 0 || _stretchings.Count > 0;
        }

        _last = tick;
        var running = _runner.Step(tick);
        for (var i = _minimizings.Count - 1; i >= 0; i--)
        {
            var entry = _minimizings[i];
            if (entry.View.Tree.IsDestroyed || !entry.Step(tick))
            {
                if (!entry.View.Tree.IsDestroyed)
                {
                    entry.Lamp?.End(entry.Stack);
                    entry.Squash?.End(entry.Stack);
                    if (entry.Hiding)
                    {
                        MinimizeSettled?.Invoke(entry.View);
                    }
                }

                _minimizings.RemoveAt(i);
            }
            else
            {
                running = true;
            }
        }

        for (var i = _stretchings.Count - 1; i >= 0; i--)
        {
            var entry = _stretchings[i];
            if (entry.View.Tree.IsDestroyed ||
                !entry.Stretch.Step(
                    entry.Stack, FrameOf(entry.View), entry.View.Tree.X, entry.View.Tree.Y, tick))
            {
                if (entry.View.Tree.IsDestroyed)
                {
                    entry.Stretch.Release();
                }
                else
                {
                    entry.Stretch.End(entry.Stack);
                }

                _stretchings.RemoveAt(i);
            }
            else
            {
                running = true;
            }
        }

        return running;
    }

    private const double SheetMillis = 300;

    private const double GlideMillis = 160;

    private const double FadeMillis = 150;

    private const double FallApartMillis = 1000;

    private const double MagicLampMillis = 250;

    private const double SquashMillis = 250;

    private const double StretchMillis = 250;

    private Box FrameOf(PlasmaHostView view) => WindowFrame?.Invoke(view) ?? default;

    private GlideOptions GlideOptions() => new()
    {
        InEdge = (FrustumEdge)_config.Integer("glide", "InRotationEdge", 0),
        InAngle = _config.Number("glide", "InRotationAngle", 3.0),
        InDistance = _config.Number("glide", "InDistance", 30.0),
        InOpacity = _config.Number("glide", "InOpacity", 0.4),
        OutEdge = (FrustumEdge)_config.Integer("glide", "OutRotationEdge", 2),
        OutAngle = _config.Number("glide", "OutRotationAngle", 3.0),
        OutDistance = _config.Number("glide", "OutDistance", 30.0),
        OutOpacity = _config.Number("glide", "OutOpacity", 0.0),
    };

    private bool IsAnimatable(PlasmaHostView view)
    {
        var appId = view.Xdg.AppId;
        if (appId is "plasmashell" or "org.kde.plasmashell")
        {
            return view.Frame is not null;
        }

        return !Blacklist.Contains(appId);
    }

    private sealed class Minimizing
    {
        public required PlasmaHostView View;
        public required TransformStack Stack;
        public MagicLampEffect? Lamp;
        public SquashEffect? Squash;
        public bool Hiding;

        public bool Step(in FrameTick tick) => Lamp?.Step(tick) ?? Squash?.Step(Stack, tick) ?? false;
    }

    private sealed class Stretching
    {
        public required PlasmaHostView View;

        public required TransformStack Stack;

        public required StretchEffect Stretch;
    }
}
