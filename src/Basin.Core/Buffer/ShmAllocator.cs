namespace Basin;

/// <summary>
/// An <see cref="IAllocator"/> that allocates <see cref="MemoryBuffer"/> instances in process memory. It supports only linear ARGB8888 and XRGB8888.
/// </summary>
public sealed class ShmAllocator : IAllocator
{
    /// <inheritdoc/>
    public DrmFormatSet Formats { get; } = BuildFormats();

    /// <summary>
    /// Allocates a zeroed <see cref="MemoryBuffer"/>.
    /// </summary>
    /// <param name="width">The width in physical pixels.</param>
    /// <param name="height">The height in physical pixels.</param>
    /// <param name="format">The pixel format.</param>
    /// <param name="modifiers">The modifiers that the caller accepts. An empty span, the linear modifier or the invalid modifier are accepted.</param>
    /// <param name="use">The requested uses. This allocator ignores them.</param>
    /// <returns>The new buffer, or <see langword="null"/> when <paramref name="format"/> is not ARGB8888 or XRGB8888 or no accepted modifier is linear.</returns>
    public BufferBase? Allocate(int width, int height, DrmFormat format, ReadOnlySpan<ulong> modifiers, BufferUse use)
    {
        if (!PixelFormatInfo.TryGet(format, out _))
        {
            return null;
        }

        if (modifiers.Length > 0 && !Contains(modifiers, DrmFormatSet.ModifierLinear) && !Contains(modifiers, DrmFormatSet.ModifierInvalid))
        {
            return null;
        }

        return new MemoryBuffer(width, height, format);
    }

    /// <summary>
    /// Does nothing. The allocator holds no resources.
    /// </summary>
    public void Dispose()
    {
    }

    private static bool Contains(ReadOnlySpan<ulong> modifiers, ulong value)
    {
        foreach (var modifier in modifiers)
        {
            if (modifier == value)
            {
                return true;
            }
        }

        return false;
    }

    private static DrmFormatSet BuildFormats()
    {
        var formats = new DrmFormatSet();
        formats.Add(DrmFormat.Argb8888, DrmFormatSet.ModifierLinear);
        formats.Add(DrmFormat.Xrgb8888, DrmFormatSet.ModifierLinear);
        return formats;
    }
}
