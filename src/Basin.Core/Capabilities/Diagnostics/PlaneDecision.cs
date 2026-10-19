namespace Basin.Capabilities;

public readonly record struct PlaneDecision(string Path, Box Box, bool OnPlane, string? Reason);
