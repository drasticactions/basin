using SDL3;

namespace Basin.Sdl;

public sealed class SdlWindow : IDisposable
{
    private bool _disposed;

    public SdlWindow(string title, int width, int height, SDL.WindowFlags flags)
    {
        ArgumentNullException.ThrowIfNull(title);
        Handle = SDL.CreateWindow(title, width, height, flags | SDL.WindowFlags.HighPixelDensity);
        if (Handle == 0)
        {
            throw new InvalidOperationException($"SDL_CreateWindow failed: {SDL.GetError()}");
        }

        Id = SDL.GetWindowID(Handle);
    }

    public SdlWindow(SdlWindow parent, int offsetX, int offsetY, int width, int height, SDL.WindowFlags flags)
    {
        ArgumentNullException.ThrowIfNull(parent);
        Handle = SDL.CreatePopupWindow(
            parent.Handle, offsetX, offsetY, Math.Max(1, width), Math.Max(1, height), flags | SDL.WindowFlags.HighPixelDensity);
        if (Handle == 0)
        {
            throw new InvalidOperationException($"SDL_CreatePopupWindow failed: {SDL.GetError()}");
        }

        Id = SDL.GetWindowID(Handle);
        Parent = parent;
    }

    public SdlWindow? Parent { get; }

    public bool IsPopup => Parent is not null;

    public nint Handle { get; }

    public uint Id { get; }

    public (int Width, int Height) PixelSize
    {
        get
        {
            _ = SDL.GetWindowSizeInPixels(Handle, out var width, out var height);
            return (Math.Max(1, width), Math.Max(1, height));
        }
    }

    public double DisplayScale
    {
        get
        {
            var scale = SDL.GetWindowDisplayScale(Handle);
            return scale > 0 ? scale : 1.0;
        }
    }

    public double PixelDensity
    {
        get
        {
            var density = SDL.GetWindowPixelDensity(Handle);
            return density > 0 ? density : 1.0;
        }
    }

    public uint DisplayId => SDL.GetDisplayForWindow(Handle);

    public float RefreshRate =>
        SDL.GetCurrentDisplayMode(DisplayId) is { RefreshRate: > 0 } mode ? mode.RefreshRate : 0f;

    public const float ScrgbReferenceNits = 80f;

    public const float SdrReferenceNits = 203f;

    public SdlHdrState ReadHdr()
    {
        var props = SDL.GetWindowProperties(Handle);
        if (props == 0)
        {
            return SdlHdrState.Sdr;
        }

        var enabled = SDL.GetBooleanProperty(props, SDL.Props.WindowHDREnabledBoolean, false);
        var white = SDL.GetFloatProperty(props, SDL.Props.WindowSDRWhiteLevelFloat, 1f);
        var headroom = SDL.GetFloatProperty(props, SDL.Props.WindowHDRHeadroomFloat, 1f);
        return enabled
            ? new SdlHdrState(true, Math.Max(1f, white) * (OperatingSystem.IsWindows() ? ScrgbReferenceNits : SdrReferenceNits), Math.Max(1f, headroom))
            : SdlHdrState.Sdr;
    }

    public string Title
    {
        set => SDL.SetWindowTitle(Handle, value);
    }

    public void Show() => SDL.ShowWindow(Handle);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SDL.DestroyWindow(Handle);
    }
}
