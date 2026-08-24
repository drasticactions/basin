namespace Basin.Plasma;

public sealed class SurfaceSlide
{
    private const uint LocationField = 1u << 0;
    private const uint OffsetField = 1u << 1;

    private uint _pendingFields;
    private SlideLocation _pendingLocation;
    private int _pendingOffset;
    private Surface? _surface;

    internal SurfaceSlide(Surface surface)
    {
        _surface = surface;
        surface.Destroyed += Release;
    }

    public Surface? Surface => _surface;

    public bool IsReleased { get; private set; }

    public SlideLocation Location { get; private set; }

    public int Offset { get; private set; }

    public event Action? Changed;

    internal void SetPendingLocation(uint location)
    {
        if (IsReleased || location > (uint)SlideLocation.Bottom)
        {
            return;
        }

        _pendingLocation = (SlideLocation)location;
        _pendingFields |= LocationField;
    }

    internal void SetPendingOffset(int offset)
    {
        if (IsReleased)
        {
            return;
        }

        _pendingOffset = offset;
        _pendingFields |= OffsetField;
    }

    internal void Commit()
    {
        if (IsReleased)
        {
            return;
        }

        if ((_pendingFields & LocationField) != 0)
        {
            Location = _pendingLocation;
        }

        if ((_pendingFields & OffsetField) != 0)
        {
            Offset = _pendingOffset;
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
        if (state.GetExtension<Attachment>() is { } attachment && ReferenceEquals(attachment.Slide, this))
        {
            attachment.Slide = null;
        }
    }

    internal sealed class Attachment : IDisposable
    {
        public SurfaceSlide? Slide;

        public void Dispose()
        {
        }
    }
}
