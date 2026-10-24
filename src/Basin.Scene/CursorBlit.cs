namespace Basin.Scene;

/// <summary>
/// Describes a cursor image to composite over a presented frame, for example when a screenshot must include a cursor plane.
/// </summary>
/// <param name="Buffer">The cursor image, or <see langword="null"/> when no cursor shows.</param>
/// <param name="Destination">The cursor rectangle in the frame, in physical pixels. An empty box draws nothing.</param>
public readonly record struct CursorBlit(IBuffer? Buffer, Box Destination);
