using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace EightWm;

public sealed partial class LockCurtainView : UserControl
{
    public const double DragRaiseFraction = 0.25;
    public const double TapSlop = 12;

    private readonly TranslateTransform _shift;
    private readonly PathIcon _networkIcon;
    private CurtainModel? _model;
    private Point _start;
    private bool _dragging;
    private double _travel;
    private Avalonia.Threading.DispatcherTimer? _slide;

    public LockCurtainView()
    {
        InitializeComponent();
        _shift = (TranslateTransform)Sheet.RenderTransform!;
        _networkIcon = NetworkIcon;
        NetworkButton.Click += (_, e) =>
        {
            _model?.RequestNetwork();
            e.Handled = true;
        };
    }

    public static TimeSpan RaiseDuration => TimeSpan.FromMilliseconds(AnimationCatalog.Of(Animation.ShowPanel).DurationMs);

    public double Offset => _shift.Y;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_model is not null)
        {
            _model.PropertyChanged -= OnModelChanged;
        }

        _model = DataContext as CurtainModel;
        if (_model is not null)
        {
            _model.PropertyChanged += OnModelChanged;
            ShowNetworkGlyph();
            Place(animate: false);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_model is null || _model.Raised || e.Handled)
        {
            return;
        }

        _start = e.GetPosition(this);
        _dragging = true;
        _travel = 0;
        _slide?.Stop();
        _slide = null;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging)
        {
            return;
        }

        var point = e.GetPosition(this);
        _travel = Math.Max(_travel, Math.Abs(point.Y - _start.Y) + Math.Abs(point.X - _start.X));
        _shift.Y = Math.Round(Math.Min(0, point.Y - _start.Y));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        e.Pointer.Capture(null);
        if (_travel < TapSlop)
        {
            _model?.RequestRaise("click");
        }
        else if (-_shift.Y > Bounds.Height * DragRaiseFraction)
        {
            _model?.RequestRaise("swipe");
        }

        Place(animate: true);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_dragging)
        {
            _dragging = false;
            Place(animate: true);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_model is { Raised: false })
        {
            _model.RequestRaise("key");
            e.Handled = true;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ShowNetworkGlyph();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        Place(animate: false);
    }

    private void OnModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(CurtainModel.Raised):
                Place(animate: _model is { Raised: true });
                break;
            case nameof(CurtainModel.NetworkGlyph):
                ShowNetworkGlyph();
                break;
        }
    }

    private void Place(bool animate)
    {
        _slide?.Stop();
        _slide = null;
        var target = _model is { Raised: true } ? -Math.Max(Bounds.Height, 1) : 0;
        IsHitTestVisible = _model is not { Raised: true };
        if (!animate || Math.Abs(_shift.Y - target) < 0.5)
        {
            _shift.Y = target;
            return;
        }

        var from = _shift.Y;
        var started = Environment.TickCount64;
        var duration = RaiseDuration.TotalMilliseconds;
        _slide = new Avalonia.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(16), Avalonia.Threading.DispatcherPriority.Render, (_, _) =>
        {
            var t = Math.Clamp((Environment.TickCount64 - started) / duration, 0, 1);
            _shift.Y = t >= 1 ? target : Math.Round(from + ((target - from) * Curves.Evaluate(AnimationCurve.Deceleration, t)));
            if (t >= 1)
            {
                _slide?.Stop();
                _slide = null;
            }
        });
        _slide.Start();
    }

    private void ShowNetworkGlyph()
    {
        if (_model is not null && _model.NetworkGlyph.Length > 0 && this.TryFindResource(_model.NetworkGlyph, out var glyph) && glyph is Geometry geometry)
        {
            _networkIcon.Data = geometry;
        }
    }
}
