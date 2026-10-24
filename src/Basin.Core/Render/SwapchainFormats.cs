namespace Basin;

/// <summary>
/// Helps choose the format modifiers of a swapchain.
/// </summary>
public static class SwapchainFormats
{
    /// <summary>
    /// Lists the modifiers of a format that the allocator can make and the importer can read.
    /// </summary>
    /// <param name="allocator">The allocator that makes the buffers.</param>
    /// <param name="importer">The formats that the consumer of the buffers takes.</param>
    /// <param name="format">The format to look up.</param>
    /// <returns>A new array of the shared modifiers, which can be empty.</returns>
    public static ulong[] CommonModifiers(IAllocator allocator, DrmFormatSet importer, DrmFormat format)
    {
        ArgumentNullException.ThrowIfNull(allocator);
        return [.. allocator.Formats.Intersect(importer).ModifiersOf(format)];
    }
}
