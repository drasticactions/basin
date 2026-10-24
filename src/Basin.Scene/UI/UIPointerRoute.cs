using Basin.Capabilities;
using Pixman;

namespace Basin.Scene;

/// <summary>
/// The result of <see cref="UISurfaceRouter.PointerMotion(uint, double, double)"/>. The default value means that no UI surface took the motion.
/// </summary>
/// <param name="Surface">The surface that received the motion, or <see langword="null"/> when the pointer is over no UI surface.</param>
/// <param name="Entered">A value indicating whether this motion moved the pointer into <paramref name="Surface"/> from somewhere else.</param>
/// <param name="Cursor">The cursor name that the surface asks for at the pointer, or <see langword="null"/> for no preference.</param>
public readonly record struct UIPointerRoute(IUISurface? Surface, bool Entered, string? Cursor);
