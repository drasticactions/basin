namespace Basin.Seat;

/// <summary>
/// Tells the caller of an <see cref="EdgeSwipeRecognizer"/> step what to do with the touch contact.
/// </summary>
public enum EdgeSwipeAction
{
    /// <summary>The recognizer does not track this contact. Route it as usual.</summary>
    Ignore,

    /// <summary>The contact is a candidate. Keep the event back until the recognizer claims or declines it.</summary>
    Withhold,

    /// <summary>The recognizer took the contact. Cancel what the contact started elsewhere and drop the withheld samples.</summary>
    Claim,

    /// <summary>The contact is not an edge swipe. Replay the withheld samples, then route the contact as usual.</summary>
    Decline,

    /// <summary>The claimed swipe moved. <see cref="EdgeSwipeRecognizer.Progress"/> has the new value.</summary>
    Track,

    /// <summary>The claimed swipe ended. <see cref="EdgeSwipeRecognizer.Outcome"/> and <see cref="EdgeSwipeRecognizer.Zone"/> hold the result.</summary>
    Finish,
}
