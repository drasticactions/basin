namespace Basin.Capabilities;

public readonly record struct FrameAction(FrameActionKind Kind, FrameEdges Edges = FrameEdges.None);
