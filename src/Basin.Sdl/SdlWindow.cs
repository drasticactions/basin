using SDL3;

namespace Basin.Sdl;

/// <summary>
/// Owns one SDL window handle. Every window has high pixel density on, so its pixel size can differ from its size in points.
/// </summary>
public sealed class SdlWindow : IDisposable
{
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SdlWindow"/> class as a top-level window.
    /// </summary>
    /// <param name="title">The window title.</param>
    /// <param name="width">The width in points.</param>
    /// <param name="height">The height in points.</param>
    /// <param name="flags">The SDL window flags, for example the flags of an <see cref="ISdlRenderDevice"/>.</param>
    /// <exception cref="InvalidOperationException">SDL cannot create the window.</exception>
    public SdlWindow(string title, int width, int height, SDL.WindowFlags flags)
    {
        ArgumentNullException.ThrowIfNull(title);
        Handle = SDL.CreateWindow(title, width, height, flags | SDL.WindowFlags.HighPixelDensity);
        if (Handle == 0)
        {
            throw new InvalidOperationException($"SDL_CreateWindow failed with \"{SDL.GetError()}\".");
        }

        Id = SDL.GetWindowID(Handle);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SdlWindow"/> class as a popup window of a parent.
    /// </summary>
    /// <param name="parent">The window that the popup belongs to.</param>
    /// <param name="offsetX">The horizontal offset from the parent in points.</param>
    /// <param name="offsetY">The vertical offset from the parent in points.</param>
    /// <param name="width">The width in points. A value below 1 counts as 1.</param>
    /// <param name="height">The height in points. A value below 1 counts as 1.</param>
    /// <param name="flags">The SDL window flags, which must include a popup flag.</param>
    /// <exception cref="InvalidOperationException">SDL cannot create the popup window.</exception>
    public SdlWindow(SdlWindow parent, int offsetX, int offsetY, int width, int height, SDL.WindowFlags flags)
    {
        ArgumentNullException.ThrowIfNull(parent);
        Handle = SDL.CreatePopupWindow(
            parent.Handle, offsetX, offsetY, Math.Max(1, width), Math.Max(1, height), flags | SDL.WindowFlags.HighPixelDensity);
        if (Handle == 0)
        {
            throw new InvalidOperationException($"SDL_CreatePopupWindow failed with \"{SDL.GetError()}\".");
        }

        Id = SDL.GetWindowID(Handle);
        Parent = parent;
    }

    /// <summary>
    /// Gets the parent of a popup window. It is <see langword="null"/> for a top-level window.
    /// </summary>
    public SdlWindow? Parent { get; }

    /// <summary>
    /// Gets a value indicating whether the window is a popup of another window.
    /// </summary>
    public bool IsPopup => Parent is not null;

    /// <summary>
    /// Gets the SDL window handle. It is not 0 and stays valid until <see cref="Dispose"/>.
    /// </summary>
    public nint Handle { get; }

    /// <summary>
    /// Gets the SDL window id, which window events carry.
    /// </summary>
    public uint Id { get; }

    /// <summary>
    /// Gets the drawable size in device pixels. Each value is at least 1.
    /// </summary>
    public (int Width, int Height) PixelSize
    {
        get
        {
            _ = SDL.GetWindowSizeInPixels(Handle, out var width, out var height);
            return (Math.Max(1, width), Math.Max(1, height));
        }
    }

    /// <summary>
    /// Gets the content scale that SDL reports for the display of the window, or 1 when SDL reports none.
    /// </summary>
    public double DisplayScale
    {
        get
        {
            var scale = SDL.GetWindowDisplayScale(Handle);
            return scale > 0 ? scale : 1.0;
        }
    }

    /// <summary>
    /// Gets the number of device pixels per point, or 1 when SDL reports none.
    /// </summary>
    public double PixelDensity
    {
        get
        {
            var density = SDL.GetWindowPixelDensity(Handle);
            return density > 0 ? density : 1.0;
        }
    }

    /// <summary>
    /// Gets the SDL id of the display that holds the window, or 0 when SDL cannot tell.
    /// </summary>
    public uint DisplayId => SDL.GetDisplayForWindow(Handle);

    /// <summary>
    /// Gets the refresh rate of the current mode of the window display in hertz, or 0 when SDL reports none.
    /// </summary>
    public float RefreshRate =>
        SDL.GetCurrentDisplayMode(DisplayId) is { RefreshRate: > 0 } mode ? mode.RefreshRate : 0f;

    /// <summary>
    /// The luminance in nits of scRGB 1.0, which is the unit of the SDR white level on Windows.
    /// </summary>
    public const float ScrgbReferenceNits = 80f;

    /// <summary>
    /// The luminance in nits of SDR white in BT.2408, which the window uses for an SDR white level of 1.0 outside Windows.
    /// </summary>
    public const float SdrReferenceNits = 203f;

    /// <summary>
    /// Reads the HDR state of the window from its SDL properties.
    /// </summary>
    /// <returns>The state with the SDR white level in nits, or <see cref="SdlHdrState.Sdr"/> when HDR is off.</returns>
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

    /// <summary>
    /// Sets the window title.
    /// </summary>
    public string Title
    {
        set => SDL.SetWindowTitle(Handle, value);
    }

    /// <summary>
    /// Shows the window.
    /// </summary>
    public void Show() => SDL.ShowWindow(Handle);

    /// <summary>
    /// Destroys the SDL window. A second call does nothing.
    /// </summary>
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
