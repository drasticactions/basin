namespace Basin;

public sealed class ShmAllocator : IAllocator
{
    public DrmFormatSet Formats { get; } = BuildFormats();

    public IBuffer? Allocate(int width, int height, DrmFormat format, ReadOnlySpan<ulong> modifiers, BufferUse use)
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
