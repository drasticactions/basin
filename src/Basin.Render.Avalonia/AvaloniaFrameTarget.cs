namespace Basin.Render.Avalonia;

public sealed class AvaloniaFrameTarget : BufferBase
{
    public AvaloniaFrameTarget(int width, int height, double scale = 1.0)
        : base(width, height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(width, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(height, 0);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        Scale = scale;
    }

    public double Scale { get; }

    protected override bool TryMap(BufferDataAccess access, out BufferDataView view)
    {
        view = default;
        return false;
    }

    protected override void OnFreeStorage()
    {
    }
}
