using System.Runtime.InteropServices;

namespace Basin;

public sealed class MemoryBuffer : BufferBase
{
    private readonly DrmFormat _format;
    private nint _data;

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

    public int Stride { get; }

    public DrmFormat Format => _format;

    protected override bool TryMap(BufferDataAccess access, out BufferDataView view)
    {
        view = new BufferDataView(_data, Stride, _format);
        return true;
    }

    protected override void OnFreeStorage()
    {
        unsafe
        {
            NativeMemory.Free((void*)_data);
        }

        _data = 0;
    }
}
