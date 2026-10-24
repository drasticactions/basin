namespace Basin.Capabilities;

/// <summary>
/// What a press on the frame or a window menu item asks the consumer to do. What then happens is the consumer's policy.
/// </summary>
/// <param name="Kind">The kind of action.</param>
/// <param name="Edges">The edges to resize for <see cref="FrameActionKind.Resize"/>, otherwise <see cref="FrameEdges.None"/>.</param>
public readonly record struct FrameAction(FrameActionKind Kind, FrameEdges Edges = FrameEdges.None);
