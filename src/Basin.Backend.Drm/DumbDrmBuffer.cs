using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Drm;

namespace Basin.Backend.Drm;

/// <summary>
/// A dumb buffer that <see cref="DumbAllocator"/> created. The CPU can map it, and the backend can scan it out.
/// </summary>
public sealed class DumbDrmBuffer : BufferBase
{
    private readonly DrmDumbBuffer _dumb;
    private nint _data;

    internal DumbDrmBuffer(DrmDumbBuffer dumb, DrmFormat format)
        : base((int)dumb.Width, (int)dumb.Height)
    {
        _dumb = dumb;
        Format = format;
    }

    /// <inheritdoc/>
    public override DrmFormat Format { get; }

    internal uint GemHandle => _dumb.Handle;

    internal uint Stride => _dumb.Pitch;

    /// <summary>
    /// Maps the dumb buffer's memory for every kind of access. The mapping stays until the storage is freed.
    /// </summary>
    /// <param name="access">The requested access. The buffer allows all access.</param>
    /// <param name="view">The mapped pixels with the kernel's pitch.</param>
    /// <returns>Always <see langword="true"/>.</returns>
    protected override unsafe bool TryMap(BufferDataAccess access, out BufferDataView view)
    {
        if (_data == 0)
        {
            _data = (nint)Unsafe.AsPointer(ref MemoryMarshal.GetReference(_dumb.AsSpan()));
        }

        view = new BufferDataView(_data, (int)_dumb.Pitch, Format);
        return true;
    }

    /// <summary>
    /// Destroys the dumb buffer in the kernel.
    /// </summary>
    protected override void OnFreeStorage()
    {
        _data = 0;
        _dumb.Dispose();
    }
}
