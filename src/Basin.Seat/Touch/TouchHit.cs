namespace Basin.Seat;

/// <summary>
/// The result of <see cref="ITouchHitTester.TryHit"/>.
/// </summary>
/// <param name="Surface">The surface under the contact, or <see langword="null"/> when the hit is not a client surface.</param>
/// <param name="LocalX">The horizontal surface-local position in logical pixels.</param>
/// <param name="LocalY">The vertical surface-local position in logical pixels.</param>
/// <param name="Token">An opaque value that the router gives back to <see cref="ITouchHitTester.TryMap"/> for later motion.</param>
public readonly record struct TouchHit(Surface? Surface, double LocalX, double LocalY, object? Token);
