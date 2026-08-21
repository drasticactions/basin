using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basin.Scene;
using Basin.Shell.Xdg;

namespace Basin.Avalonia;

public sealed class AvaloniaShellPolicy : IAvaloniaShellPolicy
{
    public void PlaceWindow(Window window, ToplevelInfo info)
    {
    }

    public string? ChooseScreen(IReadOnlyCollection<HostScreenInfo> screens) => null;

    public void CloseRequested(XdgToplevelWindow toplevel, int requests)
    {
        ArgumentNullException.ThrowIfNull(toplevel);
        toplevel.Close();
    }
}
