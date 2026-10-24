namespace Basin.Seat;

/// <summary>
/// The result of a claimed edge swipe, set when <see cref="EdgeSwipeRecognizer.End"/> returns <see cref="EdgeSwipeAction.Finish"/>.
/// </summary>
public enum EdgeSwipeOutcome
{
    /// <summary>No swipe has finished since the last <see cref="EdgeSwipeRecognizer.Begin"/>.</summary>
    None,

    /// <summary>The swipe reached the commit fraction, or the finger flung inward fast enough.</summary>
    In,

    /// <summary>The swipe reached the commit fraction and then went back near the edge before the release.</summary>
    InAndBack,

    /// <summary>The finger stayed near the hold fraction for the hold time before the release.</summary>
    Hold,

    /// <summary>The swipe did not go far enough or fast enough, or the contact was cancelled.</summary>
    Cancelled,
}
