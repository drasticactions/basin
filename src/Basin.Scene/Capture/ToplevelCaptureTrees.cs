using Basin.Capabilities;

namespace Basin.Scene;

/// <summary>
/// The scene nodes that a capture of one toplevel draws.
/// </summary>
/// <param name="Content">The node of the window with its decorations, or <see langword="null"/> when it is not in the scene.</param>
/// <param name="Popups">The tree of the popups of the window, or <see langword="null"/> when there is none.</param>
/// <param name="Client">The node of the client surface without decorations, for a client-only capture. <see langword="null"/> uses <paramref name="Content"/>.</param>
public readonly record struct ToplevelCaptureTrees(SceneNode? Content, SceneNode? Popups, SceneNode? Client = null);
