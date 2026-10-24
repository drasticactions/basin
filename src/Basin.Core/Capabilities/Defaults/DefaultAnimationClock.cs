namespace Basin.Capabilities.Defaults;

/// <summary>
/// The default <see cref="IAnimationClock"/>. It runs with the monotonic clock until <see cref="Freeze"/>, and its time never decreases.
/// </summary>
public sealed class DefaultAnimationClock : IAnimationClock, IAnimationClockControl
{
    private readonly IFrameClock? _frames;
    private long _offset;
    private long _frozenAt;
    private bool _marked;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultAnimationClock"/> class.
    /// </summary>
    /// <param name="frames">The frame clock that gets a frame request after each change of time, or <see langword="null"/>.</param>
    public DefaultAnimationClock(IFrameClock? frames = null)
    {
        _frames = frames;
    }

    /// <inheritdoc/>
    public bool IsFrozen { get; private set; }

    /// <inheritdoc/>
    public long Nanos => IsFrozen ? _frozenAt : MonotonicClock.Nanos - _offset;

    /// <inheritdoc/>
    public bool IsAnimating => _marked;

    /// <inheritdoc/>
    public long FromMonotonic(long monotonicNanos) => IsFrozen ? _frozenAt : monotonicNanos - _offset;

    /// <inheritdoc/>
    public bool MarkAnimating()
    {
        if (IsFrozen)
        {
            return false;
        }

        _marked = true;
        return true;
    }

    /// <inheritdoc/>
    public void BeginFrame() => _marked = false;

    /// <inheritdoc/>
    public void Freeze()
    {
        if (IsFrozen)
        {
            return;
        }

        _frozenAt = Nanos;
        IsFrozen = true;
        _marked = false;
    }

    /// <inheritdoc/>
    public void Advance(long nanos)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(nanos);
        if (IsFrozen)
        {
            _frozenAt += nanos;
        }
        else
        {
            _offset -= nanos;
        }

        _frames?.RequestFrame();
    }

    /// <inheritdoc/>
    public void Resume()
    {
        if (!IsFrozen)
        {
            return;
        }

        _offset = MonotonicClock.Nanos - _frozenAt;
        IsFrozen = false;
        _frames?.RequestFrame();
    }
}
