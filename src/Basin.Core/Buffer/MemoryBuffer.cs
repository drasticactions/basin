using System.Runtime.InteropServices;

namespace Basin;

/// <summary>
/// A buffer in zeroed native process memory with a tight stride. The CPU can always map it.
/// </summary>
public sealed class MemoryBuffer : BufferBase
{
    private readonly DrmFormat _format;
    private nint _data;

    /// <summary>Initializes a new instance of the <see cref="MemoryBuffer"/> class.</summary>
    /// <param name="width">The width in pixels. It must be positive.</param>
    /// <param name="height">The height in pixels. It must be positive.</param>
    /// <param name="format">The pixel format. It must have a known pixel size.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> or <paramref name="height"/> is not positive.</exception>
    /// <exception cref="NotSupportedException"><paramref name="format"/> has no known pixel size.</exception>
    public MemoryBuffer(int width, int height, DrmFormat format)
        : base(width, height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(width, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(height, 0);
        _format = format;
        Stride = width * format.BytesPerPixel();
        unsafe
        {
            _data = (nint)NativeMemory.AllocZeroed((nuint)(Stride * height));
        }
    }

    /// <summary>Gets the row stride in bytes. It is the width times the pixel size.</summary>
    public int Stride { get; }

    /// <inheritdoc/>
    public override DrmFormat Format => _format;

    /// <inheritdoc/>
    protected override bool TryMap(BufferDataAccess access, out BufferDataView view)
    {
        view = new BufferDataView(_data, Stride, _format);
        return true;
    }

    /// <inheritdoc/>
    protected override void OnFreeStorage()
    {
        unsafe
        {
            NativeMemory.Free((void*)_data);
        }

        _data = 0;
    }
}
