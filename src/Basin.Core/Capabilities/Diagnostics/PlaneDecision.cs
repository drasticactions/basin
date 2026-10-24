namespace Basin.Capabilities;

/// <summary>
/// The plane offload decision for one scene node on one output.
/// </summary>
/// <param name="Path">The scene path of the node.</param>
/// <param name="Box">The node box in scene coordinates, at the size that the node draws. Every entry has it.</param>
/// <param name="OnPlane">Whether the node is on a plane.</param>
/// <param name="Reason">The reason that the node is not on a plane, or <see langword="null"/> when it is.</param>
/// <param name="PlaneBox">The plane box in output physical pixels for a node on a plane, or <see langword="null"/> for a declined node.</param>
public readonly record struct PlaneDecision(string Path, Box Box, bool OnPlane, string? Reason, Box? PlaneBox = null);
