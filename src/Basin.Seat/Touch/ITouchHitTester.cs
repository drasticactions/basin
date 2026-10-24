namespace Basin.Seat;

/// <summary>
/// Finds the client surface under a touch and maps later positions into it. The compositor implements it over its scene.
/// </summary>
public interface ITouchHitTester
{
    /// <summary>
    /// Finds the surface under a new contact.
    /// </summary>
    /// <param name="layoutX">The horizontal position in layout coordinates.</param>
    /// <param name="layoutY">The vertical position in layout coordinates.</param>
    /// <param name="hit">The surface, the surface-local point and a token for <see cref="TryMap"/>.</param>
    /// <returns><see langword="false"/> when nothing is under the point.</returns>
    bool TryHit(double layoutX, double layoutY, out TouchHit hit);

    /// <summary>
    /// Maps a later position of a contact into the surface that <see cref="TryHit"/> found.
    /// </summary>
    /// <param name="token">The token from the <see cref="TouchHit"/>.</param>
    /// <param name="layoutX">The horizontal position in layout coordinates.</param>
    /// <param name="layoutY">The vertical position in layout coordinates.</param>
    /// <param name="localX">The horizontal surface-local position.</param>
    /// <param name="localY">The vertical surface-local position.</param>
    /// <returns><see langword="false"/> when the surface is gone. The router then sends an up for the contact and drops it.</returns>
    bool TryMap(object? token, double layoutX, double layoutY, out double localX, out double localY);
}
