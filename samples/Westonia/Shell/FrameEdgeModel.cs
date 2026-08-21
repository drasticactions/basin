using System.ComponentModel;
using Avalonia;
using Avalonia.Media;

namespace Westonia.Shell;

public sealed class FrameEdgeModel : INotifyPropertyChanged
{
    private static readonly IBrush SideShadow = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.0),
            new GradientStop(Color.FromArgb(0x60, 0, 0, 0), 1.0),
        },
    };

    private bool _active = true;

    public FrameEdgeModel(FrameEdge edge)
    {
        Edge = edge;
        ShadowBrush = edge switch
        {
            FrameEdge.Left => SideShadow,
            FrameEdge.Right => Flip(SideShadow),
            _ => Down(),
        };

        ChromeMargin = edge switch
        {
            FrameEdge.Left => new Thickness(FrameModel.Margin, 0, 0, 0),
            FrameEdge.Right => new Thickness(0, 0, FrameModel.Margin, 0),
            _ => new Thickness(FrameModel.Margin, 0, FrameModel.Margin, FrameModel.Margin),
        };

        ShadowMargin = edge switch
        {
            FrameEdge.Left => new Thickness(FrameModel.Margin - 8, 0, 0, 0),
            FrameEdge.Right => new Thickness(0, 0, FrameModel.Margin - 8, 0),
            _ => new Thickness(FrameModel.Margin - 6, 0, FrameModel.Margin - 6, FrameModel.Margin - 4),
        };

        Radius = edge == FrameEdge.Bottom ? new CornerRadius(0, 0, 3, 3) : default;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public FrameEdge Edge { get; }

    public IBrush ShadowBrush { get; }

    public Thickness ChromeMargin { get; }

    public Thickness ShadowMargin { get; }

    public CornerRadius Radius { get; }

    public bool Active
    {
        get => _active;
        set
        {
            if (_active == value)
            {
                return;
            }

            _active = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Active)));
        }
    }

    private static IBrush Flip(IBrush _) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.0),
            new GradientStop(Color.FromArgb(0x60, 0, 0, 0), 1.0),
        },
    };

    private static IBrush Down() => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.0),
            new GradientStop(Color.FromArgb(0x60, 0, 0, 0), 1.0),
        },
    };
}
