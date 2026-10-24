namespace Basin.Seat;

/// <summary>
/// The result of <see cref="SwipeRecognizer.End"/>.
/// </summary>
public enum SwipeOutcome
{
    /// <summary>No swipe was active.</summary>
    None,

    /// <summary>The swipe went past the commit fraction, or flung in its own direction fast enough.</summary>
    Commit,

    /// <summary>The swipe was cancelled, pressed against a clamped limit, or did not go far or fast enough.</summary>
    Cancel,
}
