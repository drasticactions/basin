namespace Basin.Capabilities;

public sealed record WindowParts(string AppId, string Title, Box Frame, IReadOnlyList<(FramePart Part, Box Box)> Parts);
