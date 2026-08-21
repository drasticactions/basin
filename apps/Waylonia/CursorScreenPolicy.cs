using Avalonia;
using Avalonia.Controls;
using Basin.Avalonia;
using Basin.Diagnostics;
using Basin.Shell.Xdg;

namespace Waylonia;

internal sealed class CursorScreenPolicy : IAvaloniaShellPolicy
{
    private readonly IAvaloniaShellPolicy _inner = new AvaloniaShellPolicy();

    internal static PixelPoint? Centered(PixelRect area, int width, int height)
    {
        if (width <= 0 || height <= 0 || area.Width <= 0 || area.Height <= 0)
        {
            return null;
        }

        var x = area.X + ((area.Width - width) / 2);
        var y = area.Y + ((area.Height - height) / 2);
        return new PixelPoint(
            Math.Clamp(x, area.X, Math.Max(area.X, area.X + area.Width - width)),
            Math.Clamp(y, area.Y, Math.Max(area.Y, area.Y + area.Height - height)));
    }

    public void PlaceWindow(Window window, ToplevelInfo info)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(info);
        _inner.PlaceWindow(window, info);
        if (HostCursor.TryGetPosition() is not { } cursor)
        {
            return;
        }

        if (window.Screens?.ScreenFromPoint(cursor) is not { } screen)
        {
            return;
        }

        var scaling = screen.Scaling > 0 ? screen.Scaling : 1.0;
        var width = double.IsFinite(window.Width) ? window.Width : info.Width;
        var height = double.IsFinite(window.Height) ? window.Height : info.Height;
        if (Centered(
                screen.WorkingArea,
                (int)Math.Round(width * scaling),
                (int)Math.Round(height * scaling)) is not { } position)
        {
            return;
        }

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Position = position;
        BasinLog.Debug($"placed '{info.Title}' on '{screen.DisplayName ?? "?"}' at {position.X},{position.Y}");
    }

    internal static string? Containing(IReadOnlyCollection<HostScreenInfo> screens, PixelPoint point)
    {
        foreach (var screen in screens)
        {
            if (point.X >= screen.X && point.X < screen.X + screen.Width
                && point.Y >= screen.Y && point.Y < screen.Y + screen.Height)
            {
                return screen.Key;
            }
        }

        return null;
    }

    public string? ChooseScreen(IReadOnlyCollection<HostScreenInfo> screens)
    {
        ArgumentNullException.ThrowIfNull(screens);
        if (HostCursor.TryGetPosition() is not { } cursor)
        {
            return null;
        }

        var key = Containing(screens, cursor);
        if (key is not null)
        {
            BasinLog.Debug($"a layer surface named no output, taking '{key}' under the pointer");
        }

        return key;
    }

    public void CloseRequested(XdgToplevelWindow toplevel, int requests) =>
        _inner.CloseRequested(toplevel, requests);
}
