using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Drm;

namespace Basin.Backend.Drm;

/// <summary>
/// Allocates linear CPU-mapped dumb buffers on the backend's DRM device. It serves XRGB8888 and ARGB8888 only.
/// </summary>
/// <param name="backend">The started backend whose device allocates the buffers.</param>
public sealed class DumbAllocator(DrmBackend backend) : IAllocator
{
    /// <inheritdoc/>
    public DrmFormatSet Formats { get; } = BuildFormats();

    /// <inheritdoc/>
    public BufferBase? Allocate(int width, int height, DrmFormat format, ReadOnlySpan<ulong> modifiers, BufferUse use)
    {
        if (format is not (DrmFormat.Xrgb8888 or DrmFormat.Argb8888))
        {
            return null;
        }

        if (modifiers.Length > 0 && !Contains(modifiers, DrmFormatSet.ModifierLinear) && !Contains(modifiers, DrmFormatSet.ModifierInvalid))
        {
            return null;
        }

        try
        {
            return new DumbDrmBuffer(backend.Device.CreateDumbBuffer((uint)width, (uint)height, 32), format, backend.Device.Fd);
        }
        catch (DrmException)
        {
            return null;
        }
    }

    /// <summary>
    /// Does nothing. The allocator holds no resources of its own.
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
        formats.Add(DrmFormat.Xrgb8888, DrmFormatSet.ModifierLinear);
        formats.Add(DrmFormat.Argb8888, DrmFormatSet.ModifierLinear);
        return formats;
    }
}
