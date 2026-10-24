using Basin.Capabilities;
using SDL3;

namespace Basin.Sdl;

/// <summary>
/// Sets the SDL host cursor from a cursor shape, an xcursor name or a client cursor surface.
/// System cursors are created once and cached. Only the 20 system cursors of SDL 3.4 are used.
/// </summary>
public sealed unsafe class SdlCursor : IDisposable
{
    private const int SystemCursors = (int)SDL.SystemCursor.WResize + 1;

    private readonly nint[] _system = new nint[SystemCursors];
    private nint _image;
    private bool _hidden;
    private bool _disposed;

    /// <summary>
    /// Gets the SDL cursor handle that was set last. It is 0 before the first cursor.
    /// </summary>
    public nint Current { get; private set; }

    /// <summary>
    /// Gets the system cursor that was set last. It is <see langword="null"/> while a surface image is the cursor.
    /// </summary>
    public SDL.SystemCursor? CurrentSystem { get; private set; }

    /// <summary>
    /// Gets a value indicating whether <see cref="Hide"/> hid the cursor and no cursor showed it again.
    /// </summary>
    public bool IsHidden => _hidden;

    /// <summary>
    /// Shows the system cursor that is nearest to a cursor shape.
    /// </summary>
    /// <param name="shape">The cursor shape that a client or the shell asked for.</param>
    public void Show(CursorShape shape) => ShowSystem(Map(shape));

    /// <summary>
    /// Shows the system cursor that is nearest to an xcursor name. An unknown name gives the default cursor.
    /// </summary>
    /// <param name="name">The xcursor or CSS cursor name.</param>
    public void ShowNamed(string name) => ShowSystem(Map(name));

    /// <summary>
    /// Shows a system cursor and shows a hidden cursor again. An id outside the SDL 3.4 range gives the default cursor.
    /// </summary>
    /// <param name="id">The SDL system cursor.</param>
    /// <exception cref="ObjectDisposedException">The cursor is disposed.</exception>
    public void ShowSystem(SDL.SystemCursor id)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var index = (int)id;
        if (index < 0 || index >= SystemCursors)
        {
            index = (int)SDL.SystemCursor.Default;
        }

        if (_system[index] == 0)
        {
            _system[index] = SDL.CreateSystemCursor((SDL.SystemCursor)index);
        }

        CurrentSystem = (SDL.SystemCursor)index;
        Apply(_system[index]);
        ReleaseImage();
    }

    /// <summary>
    /// Shows the current buffer of a cursor surface as a color cursor at the logical size of the surface.
    /// A buffer with a larger scale goes on as an alternate image, so SDL can pick the image that fits the display.
    /// </summary>
    /// <param name="surface">The cursor surface. Its buffer must allow read access.</param>
    /// <param name="hotspotX">The horizontal hotspot in surface-local logical units.</param>
    /// <param name="hotspotY">The vertical hotspot in surface-local logical units.</param>
    /// <returns><see langword="false"/> when the surface has no readable buffer or SDL cannot create the cursor. The previous cursor then stays.</returns>
    /// <exception cref="ObjectDisposedException">The cursor is disposed.</exception>
    public bool ShowImage(Surface surface, int hotspotX, int hotspotY)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (surface.Current.Buffer is not { } buffer || !buffer.BeginDataAccess(BufferDataAccess.Read, out var view))
        {
            return false;
        }

        nint baseSurface = 0;
        nint alternate = 0;
        try
        {
            var width = surface.Current.Width > 0 ? surface.Current.Width : buffer.Width;
            var height = surface.Current.Height > 0 ? surface.Current.Height : buffer.Height;
            baseSurface = Copy(view.Data, view.Stride, buffer.Width, buffer.Height, width, height);
            if (baseSurface == 0)
            {
                return false;
            }

            if (buffer.Width != width || buffer.Height != height)
            {
                alternate = Copy(view.Data, view.Stride, buffer.Width, buffer.Height, buffer.Width, buffer.Height);
                if (alternate != 0)
                {
                    _ = SDL.AddSurfaceAlternateImage(baseSurface, alternate);
                }
            }

            var cursor = SDL.CreateColorCursor(
                baseSurface, Math.Clamp(hotspotX, 0, width - 1), Math.Clamp(hotspotY, 0, height - 1));
            if (cursor == 0)
            {
                return false;
            }

            CurrentSystem = null;
            Apply(cursor);
            ReleaseImage();
            _image = cursor;
            return true;
        }
        finally
        {
            buffer.EndDataAccess();
            if (alternate != 0)
            {
                SDL.DestroySurface(alternate);
            }

            if (baseSurface != 0)
            {
                SDL.DestroySurface(baseSurface);
            }
        }
    }

    /// <summary>
    /// Hides the host cursor until the next cursor is shown.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The cursor is disposed.</exception>
    public void Hide()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _hidden = true;
        _ = SDL.HideCursor();
    }

    private void Apply(nint cursor)
    {
        if (cursor != 0)
        {
            _ = SDL.SetCursor(cursor);
            Current = cursor;
        }

        if (_hidden)
        {
            _hidden = false;
            _ = SDL.ShowCursor();
        }
    }

    private void ReleaseImage()
    {
        if (_image != 0 && _image != Current)
        {
            SDL.DestroyCursor(_image);
            _image = 0;
        }
    }

    private static nint Copy(nint source, int sourceStride, int sourceWidth, int sourceHeight, int width, int height)
    {
        var target = SDL.CreateSurface(width, height, SDL.PixelFormat.ARGB8888);
        if (target == 0)
        {
            return 0;
        }

        var info = (SDL.Surface*)target;
        var pixels = info->Pixels;
        var pitch = info->Pitch;
        for (var y = 0; y < height; y++)
        {
            var row = (byte*)(pixels + (y * pitch));
            var sourceY = Math.Min(sourceHeight - 1, y * sourceHeight / height);
            var from = (byte*)(source + (sourceY * sourceStride));
            for (var x = 0; x < width; x++)
            {
                var sourceX = Math.Min(sourceWidth - 1, x * sourceWidth / width);
                var pixel = from + (sourceX * 4);
                var alpha = pixel[3];
                var cell = row + (x * 4);
                cell[3] = alpha;
                for (var channel = 0; channel < 3; channel++)
                {
                    cell[channel] = alpha is 0 or 255
                        ? pixel[channel]
                        : (byte)Math.Min(255, pixel[channel] * 255 / alpha);
                }
            }
        }

        return target;
    }

    /// <summary>
    /// Maps a cursor shape to the nearest of the 20 SDL 3.4 system cursors.
    /// </summary>
    /// <param name="shape">The cursor shape.</param>
    /// <returns>The system cursor, or <see cref="SDL.SystemCursor.Default"/> when no cursor is near.</returns>
    public static SDL.SystemCursor Map(CursorShape shape) => shape switch
    {
        CursorShape.Pointer => SDL.SystemCursor.Pointer,
        CursorShape.Progress => SDL.SystemCursor.Progress,
        CursorShape.Wait => SDL.SystemCursor.Wait,
        CursorShape.Crosshair or CursorShape.Cell => SDL.SystemCursor.Crosshair,
        CursorShape.Text or CursorShape.VerticalText => SDL.SystemCursor.Text,
        CursorShape.Move or CursorShape.Grab or CursorShape.Grabbing or CursorShape.AllScroll or CursorShape.AllResize => SDL.SystemCursor.Move,
        CursorShape.NoDrop or CursorShape.NotAllowed => SDL.SystemCursor.NotAllowed,
        CursorShape.EResize => SDL.SystemCursor.EResize,
        CursorShape.NResize => SDL.SystemCursor.NResize,
        CursorShape.NeResize => SDL.SystemCursor.NEResize,
        CursorShape.NwResize => SDL.SystemCursor.NWResize,
        CursorShape.SResize => SDL.SystemCursor.SResize,
        CursorShape.SeResize => SDL.SystemCursor.SEResize,
        CursorShape.SwResize => SDL.SystemCursor.SWResize,
        CursorShape.WResize => SDL.SystemCursor.WResize,
        CursorShape.EwResize or CursorShape.ColResize => SDL.SystemCursor.EWResize,
        CursorShape.NsResize or CursorShape.RowResize => SDL.SystemCursor.NSResize,
        CursorShape.NeswResize => SDL.SystemCursor.NESWResize,
        CursorShape.NwseResize => SDL.SystemCursor.NWSEResize,
        _ => SDL.SystemCursor.Default,
    };

    /// <summary>
    /// Maps an xcursor or CSS cursor name to the nearest of the 20 SDL 3.4 system cursors.
    /// </summary>
    /// <param name="name">The cursor name.</param>
    /// <returns>The system cursor, or <see cref="SDL.SystemCursor.Default"/> for an unknown name.</returns>
    public static SDL.SystemCursor Map(string name) => name switch
    {
        "fleur" or "move" or "grabbing" or "grab" => SDL.SystemCursor.Move,
        "text" or "xterm" => SDL.SystemCursor.Text,
        "pointer" or "hand1" or "hand2" => SDL.SystemCursor.Pointer,
        "wait" or "watch" => SDL.SystemCursor.Wait,
        "progress" or "left_ptr_watch" => SDL.SystemCursor.Progress,
        "crosshair" or "cross" => SDL.SystemCursor.Crosshair,
        "not-allowed" or "crossed_circle" => SDL.SystemCursor.NotAllowed,
        "top_side" => SDL.SystemCursor.NResize,
        "bottom_side" => SDL.SystemCursor.SResize,
        "left_side" => SDL.SystemCursor.WResize,
        "right_side" => SDL.SystemCursor.EResize,
        "top_left_corner" => SDL.SystemCursor.NWResize,
        "top_right_corner" => SDL.SystemCursor.NEResize,
        "bottom_left_corner" => SDL.SystemCursor.SWResize,
        "bottom_right_corner" => SDL.SystemCursor.SEResize,
        "sb_h_double_arrow" or "ew-resize" or "col-resize" => SDL.SystemCursor.EWResize,
        "sb_v_double_arrow" or "ns-resize" or "row-resize" => SDL.SystemCursor.NSResize,
        _ => SDL.SystemCursor.Default,
    };

    /// <summary>
    /// Restores the SDL default cursor and destroys every cursor that this object created. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (Current != 0)
        {
            _ = SDL.SetCursor(SDL.GetDefaultCursor());
        }

        if (_image != 0)
        {
            SDL.DestroyCursor(_image);
            _image = 0;
        }

        for (var i = 0; i < _system.Length; i++)
        {
            if (_system[i] != 0)
            {
                SDL.DestroyCursor(_system[i]);
                _system[i] = 0;
            }
        }
    }
}
