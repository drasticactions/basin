namespace Basin.Seat;

/// <summary>
/// The rectangle in layout coordinates that an edge swipe measures its edges against, usually one output.
/// </summary>
/// <param name="X">The left edge in layout coordinates.</param>
/// <param name="Y">The top edge in layout coordinates.</param>
/// <param name="Width">The width in layout units. A value of zero or less disables edge swipes in the area.</param>
/// <param name="Height">The height in layout units. A value of zero or less disables edge swipes in the area.</param>
public readonly record struct EdgeSwipeArea(double X, double Y, double Width, double Height);
