namespace Basin;

public static class ScanoutProbe
{
    public static bool CanScanOut(
        this IAllocator allocator, IOutput output, ReadOnlySpan<ulong> modifiers, DrmFormat format)
    {
        ArgumentNullException.ThrowIfNull(allocator);
        ArgumentNullException.ThrowIfNull(output);
        var mode = output.CurrentMode.Width > 0 ? output.CurrentMode : new OutputMode(1920, 1080, 60_000);
        if (allocator.Allocate(mode.Width, mode.Height, format, modifiers, BufferUse.Render | BufferUse.Scanout)
            is not BufferBase probe)
        {
            return false;
        }

        probe.Destroy();
        return true;
    }
}
