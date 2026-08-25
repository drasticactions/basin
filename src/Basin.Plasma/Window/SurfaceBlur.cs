using Pixman;

namespace Basin.Plasma;

public sealed class SurfaceBlur : IDisposable
{
    private readonly PixmanRegion32 _region = new();
    private readonly PixmanRegion32 _pendingRegion = new();
    private bool _pendingWholeSurface = true;
    private bool _pendingSet;
    private Surface? _surface;

    internal SurfaceBlur(Surface surface)
    {
        _surface = surface;
        surface.Destroyed += Release;
    }

    public Surface? Surface => _surface;

    public bool IsReleased { get; private set; }

    public bool WholeSurface { get; private set; } = true;

    public PixmanRegion32 Region => _region;

    public event Action? Changed;

    public void Dispose()
    {
        _region.Dispose();
        _pendingRegion.Dispose();
    }

    internal void SetPendingRegion(PixmanRegion32? region)
    {
        if (IsReleased)
        {
            return;
        }

        _pendingWholeSurface = region is null;
        _pendingRegion.Clear();
        if (region is not null)
        {
            _pendingRegion.Copy(region);
        }

        _pendingSet = true;
    }

    internal void Commit()
    {
        if (IsReleased || !_pendingSet)
        {
            return;
        }

        WholeSurface = _pendingWholeSurface;
        _region.Copy(_pendingRegion);
        _pendingSet = false;
        Changed?.Invoke();
    }

    internal void Release()
    {
        if (IsReleased)
        {
            return;
        }

        IsReleased = true;
        _pendingSet = false;
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
        if (state.GetExtension<Attachment>() is { } attachment && ReferenceEquals(attachment.Blur, this))
        {
            attachment.Blur = null;
        }
    }

    internal sealed class Attachment : IDisposable
    {
        public SurfaceBlur? Blur;

        public void Dispose()
        {
        }
    }
}
