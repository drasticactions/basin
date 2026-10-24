namespace Basin.Seat;

/// <summary>
/// The kind of target that took a routed touch down.
/// </summary>
public enum TouchTargetKind
{
    /// <summary>Nothing took the contact.</summary>
    None,

    /// <summary>The <see cref="ITouchChrome"/> took the contact.</summary>
    Chrome,

    /// <summary>A client surface that has bound <c>wl_touch</c> took the contact.</summary>
    Client,

    /// <summary>The <see cref="TouchPointerDriver"/> took the contact and emulates a mouse.</summary>
    Pointer,
}
