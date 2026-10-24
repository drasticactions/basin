namespace Basin.Hosted;

/// <summary>
/// A token buffer that names the frame of a <see cref="HostedRenderer"/> pass. It carries a size and a scale and has no pixel storage.
/// </summary>
public sealed class HostedFrameTarget : BufferBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HostedFrameTarget"/> class.
    /// </summary>
    /// <param name="width">The frame width in pixels. It must be positive.</param>
    /// <param name="height">The frame height in pixels. It must be positive.</param>
    /// <param name="scale">The output scale of the frame. It must be positive.</param>
    /// <exception cref="ArgumentOutOfRangeException">A size or the scale is zero or negative.</exception>
    public HostedFrameTarget(int width, int height, double scale = 1.0)
        : base(width, height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(width, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(height, 0);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        Scale = scale;
    }

    /// <summary>
    /// Gets the output scale. A pass on a frame with a scale other than 1 scales the leased canvas by its reciprocal.
    /// </summary>
    public double Scale { get; }

    /// <summary>
    /// Refuses every mapping, because the target has no pixel storage.
    /// </summary>
    /// <param name="access">The requested access. The target ignores it.</param>
    /// <param name="view">Always the default value.</param>
    /// <returns>Always <see langword="false"/>.</returns>
    protected override bool TryMap(BufferDataAccess access, out BufferDataView view)
    {
        view = default;
        return false;
    }

    /// <summary>
    /// Does nothing, because the target holds no storage.
    /// </summary>
    protected override void OnFreeStorage()
    {
    }
}
