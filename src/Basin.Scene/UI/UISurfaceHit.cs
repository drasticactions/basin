using Basin.Capabilities;
using Pixman;

namespace Basin.Scene;

public readonly record struct UISurfaceHit(IUISurface Surface, SceneBuffer Node, double X, double Y);
