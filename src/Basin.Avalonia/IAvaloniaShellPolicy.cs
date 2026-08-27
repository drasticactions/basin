using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basin.Scene;
using Basin.Shell.Xdg;

namespace Basin.Avalonia;

public interface IAvaloniaShellPolicy
{
    void PlaceWindow(Window window, ToplevelInfo info);

    string? ChooseScreen(IReadOnlyCollection<HostScreenInfo> screens);

    void CloseRequested(XdgToplevelWindow toplevel, int requests);

    ScreenSurfaceKind Classify(ToplevelInfo info) => ScreenSurfaceKind.Application;
}
