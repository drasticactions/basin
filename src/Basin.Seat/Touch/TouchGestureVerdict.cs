namespace Basin.Seat;

/// <summary>
/// The answer of an <see cref="ITouchGestures"/> to one contact event. It tells the <see cref="TouchRouter"/> where the event goes.
/// </summary>
public enum TouchGestureVerdict
{
    /// <summary>The gesture does not want the event. The router routes it as usual.</summary>
    Pass,

    /// <summary>The gesture keeps the event back while it decides. The router delivers nothing.</summary>
    Withhold,

    /// <summary>The gesture takes the touch sequence. The router cancels every other target of every live contact.</summary>
    Claim,

    /// <summary>The gesture already owns the contact. The router delivers nothing.</summary>
    Owned,

    /// <summary>The gesture gives the contact back. The router replays the withheld events, then routes this one.</summary>
    Decline,

    /// <summary>The gesture ended with this event. The router delivers nothing.</summary>
    Finish,
}
