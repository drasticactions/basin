using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Drm;

namespace Basin.Backend.Drm;

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

    public override DrmFormat Format { get; }

    internal uint GemHandle => _dumb.Handle;

    internal uint Stride => _dumb.Pitch;

    protected override unsafe bool TryMap(BufferDataAccess access, out BufferDataView view)
    {
        if (_data == 0)
        {
            _data = (nint)Unsafe.AsPointer(ref MemoryMarshal.GetReference(_dumb.AsSpan()));
        }

        view = new BufferDataView(_data, (int)_dumb.Pitch, Format);
        return true;
    }

    protected override void OnFreeStorage()
    {
        _data = 0;
        _dumb.Dispose();
    }
}
