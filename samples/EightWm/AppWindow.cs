using Basin;
using Basin.Scene;
using Basin.Shell.Xdg;

namespace EightWm;

internal sealed class AppWindow : IShellApp, IClosable
{
    public AppWindow(XdgToplevelWindow xdg, SceneTree slot, SceneTransform frame, SceneSurface scene)
    {
        Xdg = xdg;
        Slot = slot;
        Frame = frame;
        Scene = scene;
    }

    public AppWindow(Basin.XWayland.XWaylandWindow x11, SceneTree slot, SceneTransform frame, SceneSurface scene)
    {
        X11 = x11;
        Slot = slot;
        Frame = frame;
        Scene = scene;
    }

    public SceneTree Slot { get; }

    public SceneTransform Frame { get; }

    public Tween Motion;

    public XdgToplevelWindow? Xdg { get; }

    public Basin.XWayland.XWaylandWindow? X11 { get; }

    public SceneSurface Scene { get; }

    public Surface? Surface => Xdg is { } xdg ? xdg.Surface : X11?.Surface;

    public string Title => Xdg is { } xdg ? xdg.Title : X11?.Title ?? string.Empty;

    public string AppId => Xdg is { } xdg ? xdg.AppId : X11?.Class ?? string.Empty;

    public bool IsTransient => Xdg is { } xdg ? xdg.Parent is not null : X11?.TransientFor is not null;

    public bool WantsFocus => X11 is null || X11.WantsFocus;

    public int MinWidth { get; set; }

    public Box Cell { get; set; }

    public bool Closing { get; set; }

    public bool IsAttributable => X11 is null;

    public int Pid
    {
        get
        {
            if (X11 is not null || Surface is not { IsDestroyed: false } surface)
            {
                return 0;
            }

            try
            {
                return surface.Resource.Client.Credentials.Pid;
            }
            catch (NotSupportedException)
            {
                return 0;
            }
        }
    }

    public (int Width, int Height) NaturalSize()
    {
        if (Xdg is { } xdg)
        {
            var geometry = xdg.Xdg.EffectiveGeometry;
            return (geometry.Width, geometry.Height);
        }

        var current = X11?.Surface?.Current;
        return (current?.Width ?? 0, current?.Height ?? 0);
    }

    public void SetActivated(bool activated)
    {
        if (Xdg is { } xdg)
        {
            xdg.SetActivated(activated);
        }
        else if (activated)
        {
            X11!.Activate();
        }
    }

    public const int ParkGap = 64;

    public bool IsParked { get; private set; }

    public void Hidden()
    {
        if (Slot.IsDestroyed)
        {
            return;
        }

        var height = Math.Max(1, Cell.Height);
        Slot.ClipBox = new Box(0, 0, Math.Max(1, Cell.Width), height);
        Slot.SetPosition(0, -(height + ParkGap));
        Slot.Enabled = true;
        IsParked = true;
    }

    public void Placed(in Box cell) => PlaceInCell(cell);

    public void PlaceInCell(in Box box)
    {
        Cell = box;
        Slot.Enabled = true;
        IsParked = false;
        Slot.SetPosition(box.X, box.Y);
        Slot.ClipBox = new Box(0, 0, box.Width, box.Height);
        if (Xdg is { } xdg)
        {
            xdg.SetSize(box.Width, box.Height);
            xdg.SetFullscreen(true);
        }
        else
        {
            X11!.Configure(box.X, box.Y, box.Width, box.Height);
            X11.SetMaximized(true);
        }
    }

    public void PlaceCentered(in Box cell)
    {
        var (width, height) = NaturalSize();
        if (width <= 0 || height <= 0)
        {
            (width, height) = (cell.Width / 2, cell.Height / 2);
        }

        Cell = cell;
        Slot.Enabled = true;
        IsParked = false;
        Slot.ClipBox = default;
        Slot.SetPosition(
            cell.X + Math.Max(0, (cell.Width - width) / 2),
            cell.Y + Math.Max(0, (cell.Height - height) / 2));
    }

    public void RequestClose()
    {
        if (Xdg is { } xdg)
        {
            xdg.Close();
        }
        else
        {
            X11!.Close();
        }
    }
}
