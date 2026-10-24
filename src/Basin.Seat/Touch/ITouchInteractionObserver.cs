namespace Basin.Seat;

/// <summary>
/// Learns where each routed touch down went, for focus or activation policy.
/// </summary>
public interface ITouchInteractionObserver
{
    /// <summary>
    /// Called after the router routes a touch down, also when nothing took it.
    /// </summary>
    /// <param name="id">The contact id.</param>
    /// <param name="kind">The kind of target that took the contact.</param>
    /// <param name="surface">The surface under the contact, or <see langword="null"/> for chrome or empty space.</param>
    void OnTouchInteraction(int id, TouchTargetKind kind, Surface? surface);
}
