using Basin;
using Basin.Capabilities;

namespace TinyComp;

internal sealed class MenuDonorFrameRenderer(IFrameRenderer frame, IFrameRenderer menu) : IFrameRenderer
{
    public IFrameRenderer Frame => frame;

    public Type SurfaceContract => frame.SurfaceContract;

    public bool OpaqueChrome => frame.OpaqueChrome;

    public bool FollowsPlacement => frame.FollowsPlacement;

    public FrameInsets Measure(in FrameState state, double scale) => frame.Measure(state, scale);

    public FrameInsets Outsets(in FrameState state, double scale) => frame.Outsets(state, scale);

    public void Draw(IUISurface surface, in Box clientBox, in FrameState state, in FrameInteraction interaction) =>
        frame.Draw(surface, clientBox, state, interaction);

    public FramePart PartAt(double x, double y, in FrameState state, double scale) => frame.PartAt(x, y, state, scale);

    public bool Animating(in FrameState state, in FrameInteraction interaction) => frame.Animating(state, interaction);

    public bool BackdropRegion(in FrameState state, double scale, Pixman.PixmanRegion32 into) => frame.BackdropRegion(state, scale, into);

    public string? CursorFor(FramePart part) => frame.CursorFor(part);

    public Box PartBounds(FramePart part) => frame.PartBounds(part);

    public UISurfaceSize MeasureMenu(in FrameState state, double scale) => menu.MeasureMenu(state, scale);

    public void DrawMenu(IUISurface surface, in FrameState state, int hotItem) => menu.DrawMenu(surface, state, hotItem);

    public int MenuItemAt(double x, double y, in FrameState state, double scale) => menu.MenuItemAt(x, y, state, scale);

    public FrameAction? MenuItemAction(int item, in FrameState state) => menu.MenuItemAction(item, state);
}
