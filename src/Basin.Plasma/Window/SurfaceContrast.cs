using Basin.Capabilities;
using Pixman;

namespace Basin.Plasma;

public sealed class SurfaceContrast : IDisposable
{
    private const uint RegionField = 1u << 0;
    private const uint ContrastField = 1u << 1;
    private const uint IntensityField = 1u << 2;
    private const uint SaturationField = 1u << 3;
    private const uint FrostField = 1u << 4;

    private readonly PixmanRegion32 _region = new();
    private readonly PixmanRegion32 _pendingRegion = new();
    private uint _pendingFields;
    private bool _pendingWholeSurface = true;
    private double _pendingContrast = 1.0;
    private double _pendingIntensity = 1.0;
    private double _pendingSaturation = 1.0;
    private bool _pendingFrost;
    private uint _pendingFrostColor;
    private Surface? _surface;

    internal SurfaceContrast(Surface surface)
    {
        _surface = surface;
        surface.Destroyed += Release;
    }

    public Surface? Surface => _surface;

    public bool IsReleased { get; private set; }

    public bool WholeSurface { get; private set; } = true;

    public PixmanRegion32 Region => _region;

    public ContrastParameters Parameters { get; private set; } = new(1.0, 1.0, 1.0);

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

        _pendingFields |= RegionField;
    }

    internal void SetPendingContrast(double contrast)
    {
        if (!IsReleased)
        {
            _pendingContrast = contrast;
            _pendingFields |= ContrastField;
        }
    }

    internal void SetPendingIntensity(double intensity)
    {
        if (!IsReleased)
        {
            _pendingIntensity = intensity;
            _pendingFields |= IntensityField;
        }
    }

    internal void SetPendingSaturation(double saturation)
    {
        if (!IsReleased)
        {
            _pendingSaturation = saturation;
            _pendingFields |= SaturationField;
        }
    }

    internal void SetPendingFrost(int red, int green, int blue, int alpha)
    {
        if (IsReleased)
        {
            return;
        }

        _pendingFrost = true;
        _pendingFrostColor =
            ((uint)Math.Clamp(alpha, 0, 255) << 24) |
            ((uint)Math.Clamp(red, 0, 255) << 16) |
            ((uint)Math.Clamp(green, 0, 255) << 8) |
            (uint)Math.Clamp(blue, 0, 255);
        _pendingFields |= FrostField;
    }

    internal void UnsetPendingFrost()
    {
        if (!IsReleased)
        {
            _pendingFrost = false;
            _pendingFrostColor = 0;
            _pendingFields |= FrostField;
        }
    }

    internal void Commit()
    {
        if (IsReleased || _pendingFields == 0)
        {
            return;
        }

        var parameters = Parameters;
        if ((_pendingFields & RegionField) != 0)
        {
            WholeSurface = _pendingWholeSurface;
            _region.Copy(_pendingRegion);
        }

        if ((_pendingFields & ContrastField) != 0)
        {
            parameters = parameters with { Contrast = _pendingContrast };
        }

        if ((_pendingFields & IntensityField) != 0)
        {
            parameters = parameters with { Intensity = _pendingIntensity };
        }

        if ((_pendingFields & SaturationField) != 0)
        {
            parameters = parameters with { Saturation = _pendingSaturation };
        }

        if ((_pendingFields & FrostField) != 0)
        {
            parameters = parameters with { Frost = _pendingFrost, FrostColor = _pendingFrostColor };
        }

        Parameters = parameters;
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
        if (state.GetExtension<Attachment>() is { } attachment && ReferenceEquals(attachment.Contrast, this))
        {
            attachment.Contrast = null;
        }
    }

    internal sealed class Attachment : IDisposable
    {
        public SurfaceContrast? Contrast;

        public void Dispose()
        {
        }
    }
}
