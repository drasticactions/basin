namespace Basin.Seat;

/// <summary>
/// One touch event that an edge swipe kept back while it decided, for replay when it declines.
/// </summary>
/// <param name="TimeMs">The event time in milliseconds.</param>
/// <param name="X">The horizontal position. <see cref="EdgeSwipeGesture"/> gives layout coordinates, <see cref="EdgeSwipeRecognizer"/> gives area-local ones.</param>
/// <param name="Y">The vertical position, in the same space as <paramref name="X"/>.</param>
/// <param name="Down"><see langword="true"/> for the touch down, <see langword="false"/> for a motion.</param>
public readonly record struct EdgeSwipeSample(uint TimeMs, double X, double Y, bool Down);
