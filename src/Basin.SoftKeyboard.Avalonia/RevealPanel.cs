using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Basin.SoftKeyboard.Avalonia;

/// <summary>
/// Reveals its child from the bottom edge up and hides it again with an animated slide and an optional fade. A closed panel that is not moving is hidden.
/// </summary>
public class RevealPanel : Decorator
{
    /// <summary>
    /// Identifies the <see cref="IsOpen"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<RevealPanel, bool>(nameof(IsOpen));

    /// <summary>
    /// Identifies the <see cref="OpenDuration"/> property.
    /// </summary>
    public static readonly StyledProperty<TimeSpan> OpenDurationProperty =
        AvaloniaProperty.Register<RevealPanel, TimeSpan>(nameof(OpenDuration), TimeSpan.FromMilliseconds(300));

    /// <summary>
    /// Identifies the <see cref="CloseDuration"/> property.
    /// </summary>
    public static readonly StyledProperty<TimeSpan> CloseDurationProperty =
        AvaloniaProperty.Register<RevealPanel, TimeSpan>(nameof(CloseDuration), TimeSpan.FromMilliseconds(300));

    /// <summary>
    /// Identifies the <see cref="OpenDelay"/> property.
    /// </summary>
    public static readonly StyledProperty<TimeSpan> OpenDelayProperty =
        AvaloniaProperty.Register<RevealPanel, TimeSpan>(nameof(OpenDelay));

    /// <summary>
    /// Identifies the <see cref="CloseDelay"/> property.
    /// </summary>
    public static readonly StyledProperty<TimeSpan> CloseDelayProperty =
        AvaloniaProperty.Register<RevealPanel, TimeSpan>(nameof(CloseDelay));

    /// <summary>
    /// Identifies the <see cref="FadeDuration"/> property.
    /// </summary>
    public static readonly StyledProperty<TimeSpan> FadeDurationProperty =
        AvaloniaProperty.Register<RevealPanel, TimeSpan>(nameof(FadeDuration));

    /// <summary>
    /// Identifies the <see cref="FadeOpenDelay"/> property.
    /// </summary>
    public static readonly StyledProperty<TimeSpan> FadeOpenDelayProperty =
        AvaloniaProperty.Register<RevealPanel, TimeSpan>(nameof(FadeOpenDelay));

    /// <summary>
    /// Identifies the <see cref="Easing"/> property.
    /// </summary>
    public static readonly StyledProperty<Easing> EasingProperty =
        AvaloniaProperty.Register<RevealPanel, Easing>(nameof(Easing), new CubicEaseOut());

    /// <summary>
    /// Identifies the <see cref="Revealed"/> property.
    /// </summary>
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

    /// <summary>
    /// Gets or sets a value indicating whether the panel is open. A change starts the slide from the current position.
    /// </summary>
    public bool IsOpen
    {
        get => GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    /// <summary>
    /// Gets or sets the time of the opening slide. The default is 300 ms.
    /// </summary>
    public TimeSpan OpenDuration
    {
        get => GetValue(OpenDurationProperty);
        set => SetValue(OpenDurationProperty, value);
    }

    /// <summary>
    /// Gets or sets the time of the closing slide. The default is 300 ms.
    /// </summary>
    public TimeSpan CloseDuration
    {
        get => GetValue(CloseDurationProperty);
        set => SetValue(CloseDurationProperty, value);
    }

    /// <summary>
    /// Gets or sets the time before the opening slide starts. The default is zero.
    /// </summary>
    public TimeSpan OpenDelay
    {
        get => GetValue(OpenDelayProperty);
        set => SetValue(OpenDelayProperty, value);
    }

    /// <summary>
    /// Gets or sets the time before the closing slide starts. The default is zero.
    /// </summary>
    public TimeSpan CloseDelay
    {
        get => GetValue(CloseDelayProperty);
        set => SetValue(CloseDelayProperty, value);
    }

    /// <summary>
    /// Gets or sets the time of the child's fade. Zero, the default, turns the fade off.
    /// </summary>
    public TimeSpan FadeDuration
    {
        get => GetValue(FadeDurationProperty);
        set => SetValue(FadeDurationProperty, value);
    }

    /// <summary>
    /// Gets or sets the time before the fade-in starts on open. The fade-out on close starts at once.
    /// </summary>
    public TimeSpan FadeOpenDelay
    {
        get => GetValue(FadeOpenDelayProperty);
        set => SetValue(FadeOpenDelayProperty, value);
    }

    /// <summary>
    /// Gets or sets the curve of the slide and the fade. The default is <see cref="CubicEaseOut"/>.
    /// </summary>
    public Easing Easing
    {
        get => GetValue(EasingProperty);
        set => SetValue(EasingProperty, value);
    }

    /// <summary>
    /// Gets the visible height of the child in whole pixels. It is 0 when the panel is closed and not moving.
    /// </summary>
    public double Revealed
    {
        get => _revealed;
        private set => SetAndRaise(RevealedProperty, ref _revealed, value);
    }

    /// <summary>
    /// Gets a value indicating whether a slide or fade is in progress.
    /// </summary>
    public bool IsSliding => _slide is not null;

    /// <summary>
    /// Stops a slide and jumps to the end state of <see cref="IsOpen"/>, for example for a panel that is attached already open.
    /// </summary>
    public void Settle()
    {
        StopSlide();
        _progress = IsOpen ? 1 : 0;
        _opacity = IsOpen || FadeDuration <= TimeSpan.Zero ? 1 : 0;
        Apply();
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ChildProperty)
        {
            Apply();
        }
    }

    /// <summary>
    /// Measures the child with unlimited height and returns the revealed part of its height.
    /// </summary>
    /// <param name="availableSize">The size that the parent gives.</param>
    /// <returns>The child's width and the revealed height.</returns>
    protected override Size MeasureOverride(Size availableSize)
    {
        var full = base.MeasureOverride(availableSize.WithHeight(double.PositiveInfinity));
        var revealed = Math.Round(full.Height * _progress);
        Revealed = revealed;
        return new Size(full.Width, revealed);
    }

    /// <summary>
    /// Arranges the child at its full height with its top part showing at the bottom of the panel.
    /// </summary>
    /// <param name="finalSize">The size that the parent gives.</param>
    /// <returns>The given size.</returns>
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

    /// <inheritdoc/>
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
