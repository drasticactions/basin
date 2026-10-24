namespace Basin.Ipc;

/// <summary>
/// One scene node in the reply of <c>diag/tree</c>.
/// </summary>
/// <param name="Depth">The depth below the root. The root is at depth 0.</param>
/// <param name="Kind">The node kind, for example a tree, a buffer or a rect.</param>
/// <param name="Name">The debug name of the node, or <see langword="null"/>.</param>
/// <param name="Path">The path from the root: the debug name or kind of each node, separated by slashes.</param>
/// <param name="X">The x position relative to the parent node.</param>
/// <param name="Y">The y position relative to the parent node.</param>
/// <param name="Width">The width of a buffer or rect node. It is 0 for other kinds.</param>
/// <param name="Height">The height of a buffer or rect node. It is 0 for other kinds.</param>
/// <param name="Enabled">Whether the node is enabled.</param>
/// <param name="Details">More attributes as text, for example the clip, size and color. A text that is not empty starts with a space.</param>
public readonly record struct IpcDiagNode(
    int Depth, string Kind, string? Name, string Path, int X, int Y, int Width, int Height, bool Enabled, string Details);
