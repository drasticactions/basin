namespace Basin.WindowManager;

/// <summary>
/// Detects a second click on the same target within <see cref="ThresholdMs"/>. A double click clears the state, so a third click starts over.
/// </summary>
/// <typeparam name="T">The click target type, compared by reference.</typeparam>
public sealed class WmDoubleClick<T>
    where T : class
{
    /// <summary>
    /// The longest time in milliseconds between two clicks of a double click.
    /// </summary>
    public const long ThresholdMs = 400;

    private T? _target;
    private long _at;

    /// <summary>
    /// Records a click now.
    /// </summary>
    /// <param name="target">The clicked target.</param>
    /// <returns><see langword="true"/> when this click completes a double click.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> is <see langword="null"/>.</exception>
    public bool Click(T target) => Click(target, Environment.TickCount64);

    /// <summary>
    /// Records a click at a given time.
    /// </summary>
    /// <param name="target">The clicked target.</param>
    /// <param name="nowMs">The click time in milliseconds on the same clock as earlier clicks.</param>
    /// <returns><see langword="true"/> when this click completes a double click.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> is <see langword="null"/>.</exception>
    public bool Click(T target, long nowMs)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (_target is not null && ReferenceEquals(_target, target) && nowMs - _at <= ThresholdMs)
        {
            _target = null;
            return true;
        }

        _target = target;
        _at = nowMs;
        return false;
    }

    /// <summary>
    /// Forgets the last click, so the next click cannot complete a double click.
    /// </summary>
    public void Reset() => _target = null;

    /// <summary>
    /// Forgets the last click when it was on a target, for example a window that closed.
    /// </summary>
    /// <param name="target">The target to forget.</param>
    public void Forget(T target)
    {
        if (ReferenceEquals(_target, target))
        {
            _target = null;
        }
    }
}
