namespace Basin;

public static class SwapchainFormats
{
    public static ulong[] CommonModifiers(IAllocator allocator, DrmFormatSet importer, DrmFormat format)
    {
        ArgumentNullException.ThrowIfNull(allocator);
        return [.. allocator.Formats.Intersect(importer).ModifiersOf(format)];
    }
}
