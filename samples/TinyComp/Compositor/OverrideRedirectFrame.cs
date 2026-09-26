using Basin.Scene;
using Basin.XWayland;

namespace TinyComp;

internal sealed class OverrideRedirectFrame(XWaylandWindow window, SceneTransform node)
{
    public XWaylandWindow Window { get; } = window;

    public SceneTransform Node { get; } = node;

    public TinyComp.IGrabTarget? Owner { get; set; }
}
