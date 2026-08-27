using Avalonia;
using Avalonia.Controls;
using Basin.Avalonia;
using Basin.Shell.Xdg;
using Wayland.Server;
using static Waylonia.WayloniaLog;

namespace Waylonia;

internal sealed class DesktopShellPolicy(IAvaloniaShellPolicy inner) : IAvaloniaShellPolicy
{
    private readonly IAvaloniaShellPolicy _inner = inner;

    private readonly HashSet<WlClient> _declared = [];

    private bool _claimed;

    public void Declare(WlClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (_declared.Add(client))
        {
            _claimed = true;
            client.Destroyed += () => _declared.Remove(client);
        }
    }

    public bool IsDeclared(WlClient client) => _declared.Contains(client);

    public bool HasDeclared => _declared.Count > 0;

    public bool HasClaimed => _claimed;

    public int DeclaredPid { get; set; }

    public Func<IReadOnlyCollection<WlClient>>? BoundClients { get; set; }

    public (int Width, int Height)? Size { get; set; }

    public ScreenSurfaceKind Classify(ToplevelInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        if (info.Client is not { } client)
        {
            return ScreenSurfaceKind.Application;
        }

        if (_declared.Contains(client))
        {
            return ScreenSurfaceKind.Screen;
        }

        if (!_claimed
            && DeclaredPid != 0
            && client.TryGetCredentials(out var credentials)
            && ProcessTree.IsDescendant((int)credentials.Pid, DeclaredPid))
        {
            Log.Debug($"'{info.AppId}' is the declared session, taking it for a guest compositor");
            Declare(client);
            return ScreenSurfaceKind.Screen;
        }

        if (BoundClients?.Invoke() is { } bound && bound.Contains(client))
        {
            Log.Debug($"'{info.AppId}' binds fullscreen-shell and owns a toplevel, taking it for a guest compositor");
            return ScreenSurfaceKind.Screen;
        }

        return ScreenSurfaceKind.Application;
    }

    public void PlaceWindow(Window window, ToplevelInfo info)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(info);
        if (Classify(info) != ScreenSurfaceKind.Screen)
        {
            _inner.PlaceWindow(window, info);
            return;
        }

        var screen = HostCursor.TryGetPosition() is { } cursor
            ? window.Screens?.ScreenFromPoint(cursor)
            : window.Screens?.Primary;
        var scaling = screen?.Scaling is > 0 ? screen.Scaling : 1.0;
        var (width, height) = Size ?? DefaultSize(screen, scaling);
        window.Width = width;
        window.Height = height;
        if (screen is null)
        {
            return;
        }

        if (CursorScreenPolicy.Centered(
                screen.WorkingArea,
                (int)Math.Round(width * scaling),
                (int)Math.Round(height * scaling)) is not { } position)
        {
            return;
        }

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Position = position;
        Log.Debug($"placed the screen window {width}x{height} at {position.X},{position.Y}");
    }

    internal static (int Width, int Height) DefaultSize(global::Avalonia.Platform.Screen? screen, double scaling)
    {
        if (screen is null || screen.WorkingArea.Width <= 0 || screen.WorkingArea.Height <= 0)
        {
            return (1920, 1080);
        }

        return (
            Math.Max(320, (int)Math.Round(screen.WorkingArea.Width * 0.8 / scaling)),
            Math.Max(240, (int)Math.Round(screen.WorkingArea.Height * 0.8 / scaling)));
    }

    public string? ChooseScreen(IReadOnlyCollection<HostScreenInfo> screens) => _inner.ChooseScreen(screens);

    public void CloseRequested(XdgToplevelWindow toplevel, int requests) =>
        _inner.CloseRequested(toplevel, requests);
}
