namespace Basin.Seat;

/// <summary>
/// Receives a notice for each touch event that a <see cref="TouchRouter"/> sees, for idle tracking.
/// </summary>
public interface ITouchActivitySink
{
    /// <summary>
    /// Called for each touch down, motion, up and cancel, before the router routes it. Events for unknown contacts count too.
    /// </summary>
    void OnTouchActivity();
}
