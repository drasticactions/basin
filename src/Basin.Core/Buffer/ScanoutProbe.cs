namespace Basin;

/// <summary>
/// Tests whether an allocator can make a buffer that an output can scan out.
/// </summary>
public static class ScanoutProbe
{
    /// <summary>
    /// Allocates one render and scanout buffer at the output's current mode size, and destroys it at once. It uses 1920x1080 when the output has no mode.
    /// </summary>
    /// <param name="allocator">The allocator to test.</param>
    /// <param name="output">The output whose mode gives the size.</param>
    /// <param name="modifiers">The modifiers that the caller accepts.</param>
    /// <param name="format">The pixel format to test.</param>
    /// <returns><see langword="true"/> when the allocation succeeds.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="allocator"/> or <paramref name="output"/> is <see langword="null"/>.</exception>
    public static bool CanScanOut(
        this IAllocator allocator, IOutput output, ReadOnlySpan<ulong> modifiers, DrmFormat format)
    {
        ArgumentNullException.ThrowIfNull(allocator);
        ArgumentNullException.ThrowIfNull(output);
        var mode = output.CurrentMode.Width > 0 ? output.CurrentMode : new OutputMode(1920, 1080, 60_000);
        if (allocator.Allocate(mode.Width, mode.Height, format, modifiers, BufferUse.Render | BufferUse.Scanout)
            is not { } probe)
        {
            return false;
        }

        probe.Destroy();
        return true;
    }
}
