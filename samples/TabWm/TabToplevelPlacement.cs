using Basin.Capabilities;

namespace TabWm;

internal sealed class TabToplevelPlacement : IToplevelPlacement
{
    private readonly TabWm _shell;

    public TabToplevelPlacement(TabWm shell)
    {
        _shell = shell;
    }

    public bool Move(IToplevelHandle toplevel, int x, int y) => _shell.MoveRequestedTo(toplevel, x, y);

    public bool Resize(IToplevelHandle toplevel, int width, int height) => _shell.ResizeRequestedTo(toplevel, width, height);
}
