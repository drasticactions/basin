namespace Basin.Effects;

public readonly record struct ZoomOptions
{
    public ZoomOptions()
    {
    }

    public double ZoomFactor { get; init; } = 1.2;

    public double InitialZoom { get; init; } = 1.0;

    public ZoomTracking MouseTracking { get; init; } = ZoomTracking.Proportional;

    public bool FocusTracking { get; init; } = false;

    public bool TextCaretTracking { get; init; } = false;

    public double FocusDelayMillis { get; init; } = 350;

    public double MoveFactor { get; init; } = 20.0;

    public double PixelGridZoom { get; init; } = 15.0;

    public bool UsePatternUpscaler { get; init; } = true;
}
