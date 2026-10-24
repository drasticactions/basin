using Basin.Capabilities;
using Pixman;

namespace Basin.Scene;

/// <summary>
/// A UI surface found under a scene point by <see cref="UISurfaceRouter.SurfaceAt(double, double)"/>.
/// </summary>
/// <param name="Surface">The UI surface that owns the hit node.</param>
/// <param name="Node">The scene buffer that shows the surface.</param>
/// <param name="X">The horizontal position in surface coordinates, after the input scale of the owning <see cref="UISurfaceNode"/>.</param>
/// <param name="Y">The vertical position in surface coordinates, after the input scale of the owning <see cref="UISurfaceNode"/>.</param>
public readonly record struct UISurfaceHit(IUISurface Surface, SceneBuffer Node, double X, double Y);
