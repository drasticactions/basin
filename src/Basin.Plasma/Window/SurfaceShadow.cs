namespace Basin.Plasma;

public sealed class SurfaceShadow
{
    private const int PartCount = 8;

    private readonly BufferLock[] _current = new BufferLock[PartCount];
    private readonly BufferLock[] _pending = new BufferLock[PartCount];
    private readonly double[] _currentOffsets = new double[4];
    private readonly double[] _pendingOffsets = new double[4];
    private uint _pendingFields;
    private Surface? _surface;

    internal SurfaceShadow(Surface surface)
    {
        _surface = surface;
        surface.Destroyed += Release;
    }

    public Surface? Surface => _surface;

    public bool IsReleased { get; private set; }

    public event Action? Changed;

    public IBuffer? Buffer(ShadowPart part) => _current[(int)part].Buffer;

    public double LeftOffset => _currentOffsets[0];

    public double TopOffset => _currentOffsets[1];

    public double RightOffset => _currentOffsets[2];

    public double BottomOffset => _currentOffsets[3];

    internal void AttachPending(ShadowPart part, IBuffer? buffer)
    {
        if (IsReleased)
        {
            return;
        }

        var taken = buffer?.Lock() ?? default;
        _pending[(int)part].Dispose();
        _pending[(int)part] = taken;
        _pendingFields |= 1u << (int)part;
    }

    internal void SetPendingOffset(int side, double value)
    {
        if (IsReleased)
        {
            return;
        }

        _pendingOffsets[side] = value;
        _pendingFields |= 1u << (PartCount + side);
    }

    internal void Commit()
    {
        if (IsReleased)
        {
            return;
        }

        for (var i = 0; i < PartCount; i++)
        {
            if ((_pendingFields & (1u << i)) == 0)
            {
                continue;
            }

            _current[i].Dispose();
            _current[i] = _pending[i];
            _pending[i] = default;
        }

        for (var side = 0; side < 4; side++)
        {
            if ((_pendingFields & (1u << (PartCount + side))) != 0)
            {
                _currentOffsets[side] = _pendingOffsets[side];
            }
        }

        _pendingFields = 0;
        Changed?.Invoke();
    }

    internal void Release()
    {
        if (IsReleased)
        {
            return;
        }

        IsReleased = true;
        for (var i = 0; i < PartCount; i++)
        {
            _current[i].Dispose();
            _current[i] = default;
            _pending[i].Dispose();
            _pending[i] = default;
        }

        _pendingFields = 0;
        if (_surface is { IsDestroyed: false } surface)
        {
            surface.Destroyed -= Release;
            Detach(surface.Current);
            Detach(surface.Pending);
        }

        _surface = null;
        Changed?.Invoke();
    }

    private void Detach(SurfaceState state)
    {
        if (state.GetExtension<Attachment>() is { } attachment && ReferenceEquals(attachment.Shadow, this))
        {
            attachment.Shadow = null;
        }
    }

    internal sealed class Attachment : IDisposable
    {
        public SurfaceShadow? Shadow;

        public void Dispose()
        {
        }
    }
}
