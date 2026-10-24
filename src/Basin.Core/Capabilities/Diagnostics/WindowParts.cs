namespace Basin.Capabilities;

/// <summary>
/// One decorated window and the bounds of its frame parts.
/// </summary>
/// <param name="AppId">The application id of the window.</param>
/// <param name="Title">The window title.</param>
/// <param name="Frame">The box of the whole frame.</param>
/// <param name="Parts">Each frame part with its box, in the same coordinates as <paramref name="Frame"/>.</param>
public sealed record WindowParts(string AppId, string Title, Box Frame, IReadOnlyList<(FramePart Part, Box Box)> Parts);
