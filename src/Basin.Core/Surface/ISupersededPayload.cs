namespace Basin;

/// <summary>
/// An extension payload of a <see cref="SurfaceState"/> that must act when a later commit replaces it before it applies.
/// </summary>
public interface ISupersededPayload
{
    /// <summary>
    /// Runs when a later commit replaces this payload in a cached or parked state. The state disposes the payload after this call.
    /// </summary>
    void Superseded();
}
