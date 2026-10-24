namespace Basin.Seat;

/// <summary>
/// The part of the area where a claimed edge swipe was released.
/// </summary>
public enum EdgeSwipeZone
{
    /// <summary>No swipe has finished, or the area had no size.</summary>
    None,

    /// <summary>The left side band, set by <see cref="EdgeSwipeRecognizer.SideZoneFraction"/>.</summary>
    Left,

    /// <summary>The right side band, set by <see cref="EdgeSwipeRecognizer.SideZoneFraction"/>.</summary>
    Right,

    /// <summary>The bottom band, set by <see cref="EdgeSwipeRecognizer.BottomZoneFraction"/>. It wins over the side bands.</summary>
    Bottom,

    /// <summary>Any point that is not in the bottom band or a side band.</summary>
    Middle,
}
