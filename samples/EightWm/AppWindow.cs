using Basin;
using Basin.Capabilities;
using Basin.Scene;
using Basin.Shell.Xdg;

namespace EightWm;

internal sealed class AppWindow : IShellApp, IClosable
{
    public AppWindow(XdgToplevelWindow xdg, SceneTree slot, SceneTransform frame, SceneSurface scene)
    {
        Handle = xdg;
        Xdg = xdg;
        Slot = slot;
        Frame = frame;
        Scene = scene;
    }

    public AppWindow(Basin.XWayland.XWaylandWindow x11, SceneTree slot, SceneTransform frame, SceneSurface scene)
    {
        Handle = x11;
        X11 = x11;
        Slot = slot;
        Frame = frame;
        Scene = scene;
    }

    public IToplevelHandle Handle { get; }

    public SceneTree Slot { get; }

    public SceneTransform Frame { get; }

    public Tween Motion;

    public XdgToplevelWindow? Xdg { get; }

    public Basin.XWayland.XWaylandWindow? X11 { get; }

    public SceneSurface Scene { get; }

    public Surface? Surface => Handle.Surface;

    public string Title => Handle.Title;

    public string AppId => Handle.AppId;

    public bool IsTransient => Handle.Parent is not null;

    public bool WantsFocus => Handle.WantsFocus;

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

    public (int Width, int Height) NaturalSize() => Handle.NaturalSize;

    public void SetActivated(bool activated) => Handle.SetActivated(activated);

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
        Handle.Configure(box.X, box.Y, box.Width, box.Height);
        if (Xdg is not null)
        {
            Handle.SetFullscreen(true);
        }
        else
        {
            Handle.SetMaximized(true);
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

    public void RequestClose() => Handle.Close();
}
