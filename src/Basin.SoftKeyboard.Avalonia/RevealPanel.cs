using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Basin.SoftKeyboard.Avalonia;

public class RevealPanel : Decorator
{
    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<RevealPanel, bool>(nameof(IsOpen));

    public static readonly StyledProperty<TimeSpan> OpenDurationProperty =
        AvaloniaProperty.Register<RevealPanel, TimeSpan>(nameof(OpenDuration), TimeSpan.FromMilliseconds(300));

    public static readonly StyledProperty<TimeSpan> CloseDurationProperty =
        AvaloniaProperty.Register<RevealPanel, TimeSpan>(nameof(CloseDuration), TimeSpan.FromMilliseconds(300));

    public static readonly StyledProperty<TimeSpan> OpenDelayProperty =
        AvaloniaProperty.Register<RevealPanel, TimeSpan>(nameof(OpenDelay));

    public static readonly StyledProperty<TimeSpan> CloseDelayProperty =
        AvaloniaProperty.Register<RevealPanel, TimeSpan>(nameof(CloseDelay));

    public static readonly StyledProperty<TimeSpan> FadeDurationProperty =
        AvaloniaProperty.Register<RevealPanel, TimeSpan>(nameof(FadeDuration));

    public static readonly StyledProperty<TimeSpan> FadeOpenDelayProperty =
        AvaloniaProperty.Register<RevealPanel, TimeSpan>(nameof(FadeOpenDelay));

    public static readonly StyledProperty<Easing> EasingProperty =
        AvaloniaProperty.Register<RevealPanel, Easing>(nameof(Easing), new CubicEaseOut());

    public static readonly DirectProperty<RevealPanel, double> RevealedProperty =
        AvaloniaProperty.RegisterDirect<RevealPanel, double>(nameof(Revealed), panel => panel.Revealed);

    private DispatcherTimer? _slide;
    private double _progress;
    private double _opacity = 1;
    private double _from;
    private double _to;
    private double _fadeFrom;
    private double _fadeTo;
    private long _started;
    private double _revealed;

    static RevealPanel()
    {
        ClipToBoundsProperty.OverrideDefaultValue<RevealPanel>(true);
        IsOpenProperty.Changed.AddClassHandler<RevealPanel>((panel, _) => panel.Slide());
    }

    public bool IsOpen
    {
        get => GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public TimeSpan OpenDuration
    {
        get => GetValue(OpenDurationProperty);
        set => SetValue(OpenDurationProperty, value);
    }

    public TimeSpan CloseDuration
    {
        get => GetValue(CloseDurationProperty);
        set => SetValue(CloseDurationProperty, value);
    }

    public TimeSpan OpenDelay
    {
        get => GetValue(OpenDelayProperty);
        set => SetValue(OpenDelayProperty, value);
    }

    public TimeSpan CloseDelay
    {
        get => GetValue(CloseDelayProperty);
        set => SetValue(CloseDelayProperty, value);
    }

    public TimeSpan FadeDuration
    {
        get => GetValue(FadeDurationProperty);
        set => SetValue(FadeDurationProperty, value);
    }

    public TimeSpan FadeOpenDelay
    {
        get => GetValue(FadeOpenDelayProperty);
        set => SetValue(FadeOpenDelayProperty, value);
    }

    public Easing Easing
    {
        get => GetValue(EasingProperty);
        set => SetValue(EasingProperty, value);
    }

    public double Revealed
    {
        get => _revealed;
        private set => SetAndRaise(RevealedProperty, ref _revealed, value);
    }

    public bool IsSliding => _slide is not null;

    public void Settle()
    {
        StopSlide();
        _progress = IsOpen ? 1 : 0;
        _opacity = IsOpen || FadeDuration <= TimeSpan.Zero ? 1 : 0;
        Apply();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ChildProperty)
        {
            Apply();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var full = base.MeasureOverride(availableSize.WithHeight(double.PositiveInfinity));
        var revealed = Math.Round(full.Height * _progress);
        Revealed = revealed;
        return new Size(full.Width, revealed);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Child is not { } child)
        {
            return finalSize;
        }

        var height = child.DesiredSize.Height;
        var revealed = Math.Round(height * _progress);
        child.Arrange(new Rect(0, finalSize.Height - revealed, finalSize.Width, height));
        return finalSize;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        StopSlide();
    }

    private TimeSpan SizeDelay => IsOpen ? OpenDelay : CloseDelay;

    private TimeSpan SizeDuration => IsOpen ? OpenDuration : CloseDuration;

    private TimeSpan FadeDelay => IsOpen ? FadeOpenDelay : TimeSpan.Zero;

    private bool Fades => FadeDuration > TimeSpan.Zero;

    private void Slide()
    {
        StopSlide();
        _from = _progress;
        _to = IsOpen ? 1 : 0;
        _fadeFrom = _opacity;
        _fadeTo = IsOpen || !Fades ? 1 : 0;
        if (IsOpen && Fades && _progress <= 0)
        {
            _fadeFrom = 0;
            _opacity = 0;
        }

        var total = Math.Max((SizeDelay + SizeDuration).TotalMilliseconds, Fades ? (FadeDelay + FadeDuration).TotalMilliseconds : 0);
        if (total <= 0 || VisualRoot is null || (Math.Abs(_to - _from) < 0.001 && Math.Abs(_fadeTo - _fadeFrom) < 0.001))
        {
            _progress = _to;
            _opacity = _fadeTo;
            Apply();
            return;
        }

        _started = Environment.TickCount64;
        _slide = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => Step());
        _slide.Start();
        Apply();
    }

    private void Step()
    {
        var elapsed = (double)(Environment.TickCount64 - _started);
        var size = Fraction(elapsed - SizeDelay.TotalMilliseconds, SizeDuration.TotalMilliseconds);
        var fade = Fades ? Fraction(elapsed - FadeDelay.TotalMilliseconds, FadeDuration.TotalMilliseconds) : 1;
        _progress = size >= 1 ? _to : _from + ((_to - _from) * Easing.Ease(size));
        _opacity = fade >= 1 ? _fadeTo : _fadeFrom + ((_fadeTo - _fadeFrom) * Easing.Ease(fade));
        if (size >= 1 && fade >= 1)
        {
            StopSlide();
        }

        Apply();
    }

    private static double Fraction(double elapsed, double duration) =>
        duration <= 0 ? (elapsed >= 0 ? 1 : 0) : Math.Clamp(elapsed / duration, 0, 1);

    private void StopSlide()
    {
        _slide?.Stop();
        _slide = null;
    }

    private void Apply()
    {
        var shown = _progress > 0 || _slide is not null;
        IsVisible = shown;
        if (Child is { } child)
        {
            child.IsVisible = shown;
            child.Opacity = _opacity;
        }

        if (!shown)
        {
            Revealed = 0;
        }

        InvalidateMeasure();
    }
}
