using Basin;
using Basin.Scene;
using Basin.Shell.Xdg;
using Basin.XWayland;

namespace System7Wm;

internal sealed class ShellWindow
{
    private static int _nextId;

    public ShellWindow(SceneTree container, AppGroup app)
    {
        Id = ++_nextId;
        Container = container;
        App = app;
    }

    public int Id { get; }

    public SceneTree Container { get; }

    public AppGroup App { get; set; }

    public XdgToplevelWindow? Xdg { get; init; }

    public XWaylandWindow? X11 { get; init; }

    private ProcessIdentity? _process;

    public Basin.Config.WindowFacts Facts => X11 is { } x11
        ? Basin.Config.WindowFacts.ForX11(x11.Class, x11.Instance, x11.Title, x11.SteamGame, x11.WindowType, x11.Process)
        : Xdg is { } xdg
            ? Basin.Config.WindowFacts.ForWayland(xdg.AppId, xdg.Title, xdg.Parent is not null, _process ??= ProcessIdentity.Of(xdg.Surface))
            : new Basin.Config.WindowFacts { Title = ShellTitle };

    public SceneSurface? Scene { get; set; }

    public SceneNode? ContentNode { get; set; }

    public ClientFrame? Frame { get; set; }

    public ChromeSurface? ShellSurface { get; set; }

    public System7.Avalonia.Controls.System7WindowFrame? ShellFrame { get; set; }

    public string ShellKey { get; init; } = "";

    public string ShellTitle { get; set; } = "";

    public (int Width, int Height) ContentDip { get; set; }

    public Box ShellDevice { get; set; }

    public bool ShellResizable { get; init; }

    public bool ShellClosable { get; init; } = true;

    public bool IsAlert { get; init; }

    public Action? Closing { get; set; }

    public Action? ShellResized { get; set; }

    public Action? ShellClosed { get; set; }

    public Action? ShellPlaced { get; set; }

    public bool ShellGrown { get; set; }

    public Avalonia.Controls.Panel? ShellRoot { get; set; }

    public (int Width, int Height) ShellLogicalSize { get; set; }

    public bool IsShell => ShellSurface is not null;

    public Basin.AppMenu.AppMenuImport? MenuImport { get; set; }

    public Basin.AppMenu.IAppMenuSource? Imported => MenuImport?.Source;

    public WindowKind Kind { get; set; } = WindowKind.Document;

    public int X { get; set; }

    public int Y { get; set; }

    public Box? UserBox { get; set; }

    public bool Zoomed { get; set; }

    public bool Shaded { get; set; }

    public bool Modal { get; set; }

    public bool Active { get; set; }

    public bool Mapped { get; set; }

    public long LastTitleClick { get; set; }

    public int TitleClicks { get; set; }

    public ShellWindow? Parent => Xdg?.Parent is { } parent ? Owner?.Invoke(parent.Surface) : null;

    public Func<Surface, ShellWindow?>? Owner { get; init; }

    public Surface? Surface => Xdg?.Surface ?? X11?.Surface;

    public string Title => Xdg?.Title ?? X11?.Title ?? ShellTitle;

    public string AppId => Xdg?.AppId ?? X11?.Class ?? "";

    public (int Width, int Height) Size
    {
        get
        {
            if (IsShell)
            {
                return ShellLogicalSize;
            }

            if (Xdg is { } xdg)
            {
                var geometry = xdg.Xdg.PresentedGeometry;
                return (geometry.Width, geometry.Height);
            }

            return X11 is { } x ? (x.Width, x.Height) : (0, 0);
        }
    }

    public bool CanResize
    {
        get
        {
            if (IsShell)
            {
                return ShellResizable;
            }

            if (Xdg is { } xdg)
            {
                return xdg.MaxWidth == 0 || xdg.MinWidth != xdg.MaxWidth || xdg.MinHeight != xdg.MaxHeight;
            }

            return X11 is not { } x || x.MaxWidth == 0 || x.MinWidth != x.MaxWidth || x.MinHeight != x.MaxHeight;
        }
    }

    public (int Width, int Height) MinSize => Xdg is { } xdg ? (xdg.MinWidth, xdg.MinHeight) : X11 is { } x ? (x.MinWidth, x.MinHeight) : (0, 0);

    public (int Width, int Height) MaxSize => Xdg is { } xdg ? (xdg.MaxWidth, xdg.MaxHeight) : X11 is { } x ? (x.MaxWidth, x.MaxHeight) : (0, 0);

    public Box Logical
    {
        get
        {
            var (width, height) = Size;
            return new Box(X, Y, width, height);
        }
    }

    public void Close()
    {
        if (IsShell)
        {
            Closing?.Invoke();
            return;
        }

        if (Xdg is { } xdg)
        {
            xdg.Close();
        }
        else
        {
            X11?.Close();
        }
    }
}
