using Basin.Scene;

namespace Basin.Seat.Backends;

/// <summary>
/// Gives touch contacts to compositor UI surfaces through a <see cref="UISurfaceRouter"/>.
/// </summary>
public sealed class UISurfaceTouchChrome : ITouchChrome
{
    private readonly UISurfaceRouter _router;

    /// <summary>
    /// Initializes a new instance of the <see cref="UISurfaceTouchChrome"/> class.
    /// </summary>
    /// <param name="router">The router that hit-tests the UI surfaces and delivers touches to them.</param>
    /// <exception cref="ArgumentNullException"><paramref name="router"/> is <see langword="null"/>.</exception>
    public UISurfaceTouchChrome(UISurfaceRouter router)
    {
        ArgumentNullException.ThrowIfNull(router);
        _router = router;
    }

    /// <inheritdoc/>
    public bool TryPress(int id, uint timeMs, double x, double y) => _router.TouchDown(timeMs, id, x, y);

    /// <inheritdoc/>
    public void Motion(int id, uint timeMs, double x, double y) => _router.TouchMotion(timeMs, id, x, y);

    /// <inheritdoc/>
    public void Release(int id, uint timeMs, double x, double y) => _router.TouchUp(timeMs, id);

    /// <inheritdoc/>
    public void Cancel() => _router.TouchCancel();
}
