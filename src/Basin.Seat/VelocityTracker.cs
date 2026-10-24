using System.Runtime.CompilerServices;

namespace Basin.Seat;

/// <summary>
/// Estimates a smoothed 2D velocity from timed position samples in a fixed ring, with no allocation.
/// </summary>
public struct VelocityTracker
{
    /// <summary>
    /// The number of samples that the ring holds.
    /// </summary>
    public const int Capacity = 8;

    /// <summary>
    /// The weight of each new velocity estimate in the exponential smoothing.
    /// </summary>
    public const double Smoothing = 0.4;

    /// <summary>
    /// The gap in nanoseconds after which old samples are dropped and the velocity reads as zero.
    /// </summary>
    public const long StaleNanos = 60_000_000;

    private Ring _ring;
    private int _head;
    private int _count;
    private double _vx;
    private double _vy;
    private bool _hasVelocity;

    /// <summary>
    /// Gets or sets the smallest time span in nanoseconds over which each estimate is measured. Zero uses the previous sample.
    /// </summary>
    public long MinSpanNanos { get; set; }

    /// <summary>
    /// Gets the number of samples in the ring.
    /// </summary>
    public readonly int Count => _count;

    /// <summary>
    /// Gets a value indicating whether two samples close enough in time have given an estimate.
    /// </summary>
    public readonly bool HasVelocity => _hasVelocity;

    /// <summary>
    /// Gets the smoothed velocity in units per second, with no staleness check. It is zero before the first estimate.
    /// </summary>
    public readonly (double X, double Y) Smoothed => _hasVelocity ? (_vx, _vy) : (0, 0);

    /// <summary>
    /// Gets the time of the newest sample in nanoseconds, or zero when the ring is empty.
    /// </summary>
    public readonly long NewestNanos => _count == 0 ? 0 : _ring[Newest].Nanos;

    private readonly int Newest => (_head + Capacity - 1) % Capacity;

    /// <summary>
    /// Adds a sample. A sample at the same time as the newest replaces its position. A gap of <see cref="StaleNanos"/> or more restarts the estimate.
    /// </summary>
    /// <param name="x">The horizontal position.</param>
    /// <param name="y">The vertical position.</param>
    /// <param name="nanos">The sample time in nanoseconds.</param>
    public void Add(double x, double y, long nanos)
    {
        if (_count > 0)
        {
            ref var newest = ref _ring[Newest];
            var elapsed = nanos - newest.Nanos;
            if (elapsed <= 0)
            {
                newest.X = x;
                newest.Y = y;
                return;
            }

            if (elapsed >= StaleNanos)
            {
                _count = 0;
                _hasVelocity = false;
            }
            else
            {
                var partner = _ring[Partner(nanos)];
                var span = (nanos - partner.Nanos) / 1e9;
                var ix = (x - partner.X) / span;
                var iy = (y - partner.Y) / span;
                if (_hasVelocity)
                {
                    _vx += Smoothing * (ix - _vx);
                    _vy += Smoothing * (iy - _vy);
                }
                else
                {
                    _vx = ix;
                    _vy = iy;
                    _hasVelocity = true;
                }
            }
        }

        _ring[_head] = new Sample { X = x, Y = y, Nanos = nanos };
        _head = (_head + 1) % Capacity;
        if (_count < Capacity)
        {
            _count++;
        }
    }

    /// <summary>
    /// Gets the smoothed velocity in units per second at a given time.
    /// </summary>
    /// <param name="nowNanos">The current time in nanoseconds, on the clock of the samples.</param>
    /// <returns>The velocity, or zero when the newest sample is <see cref="StaleNanos"/> or more old.</returns>
    public readonly (double X, double Y) Velocity(long nowNanos) =>
        _hasVelocity && _count > 0 && nowNanos - _ring[Newest].Nanos < StaleNanos ? (_vx, _vy) : (0, 0);

    /// <summary>
    /// Empties the ring and clears the velocity. <see cref="MinSpanNanos"/> is kept.
    /// </summary>
    public void Reset()
    {
        _head = 0;
        _count = 0;
        _vx = 0;
        _vy = 0;
        _hasVelocity = false;
    }

    private readonly int Partner(long nanos)
    {
        var index = Newest;
        for (var i = 1; i < _count; i++)
        {
            if (nanos - _ring[index].Nanos >= MinSpanNanos)
            {
                break;
            }

            var older = (index + Capacity - 1) % Capacity;
            if (nanos - _ring[older].Nanos >= StaleNanos)
            {
                break;
            }

            index = older;
        }

        return index;
    }

    private struct Sample
    {
        public double X;
        public double Y;
        public long Nanos;
    }

    [InlineArray(Capacity)]
    private struct Ring
    {
        private Sample _element;
    }
}
