using System.Numerics;
using System.Runtime.CompilerServices;

namespace Basin.Kinematics;

/// <summary>
/// Measures the smoothed velocity of a point in 3D from timed samples. A point that stops for <see cref="StaleNanos"/> reads as still.
/// </summary>
public struct VelocityTracker3
{
    /// <summary>
    /// The number of samples that the tracker keeps.
    /// </summary>
    public const int Capacity = 8;

    /// <summary>
    /// The weight of each new instant velocity in the smoothed velocity.
    /// </summary>
    public const float Smoothing = 0.4f;

    /// <summary>
    /// The gap in nanoseconds after which old samples no longer count.
    /// </summary>
    public const long StaleNanos = 60_000_000;

    private Ring _ring;
    private int _head;
    private int _count;
    private Vector3 _velocity;
    private bool _hasVelocity;

    /// <summary>
    /// Gets or sets the shortest time, in nanoseconds, between a new sample and the older sample that it measures against.
    /// </summary>
    public long MinSpanNanos { get; set; }

    /// <summary>
    /// Gets the number of samples held.
    /// </summary>
    public readonly int Count => _count;

    /// <summary>
    /// Gets a value indicating whether two samples have given a velocity since the last reset or stale gap.
    /// </summary>
    public readonly bool HasVelocity => _hasVelocity;

    /// <summary>
    /// Gets the smoothed velocity in units per second, with no staleness test. It is zero when <see cref="HasVelocity"/> is <see langword="false"/>.
    /// </summary>
    public readonly Vector3 Smoothed => _hasVelocity ? _velocity : Vector3.Zero;

    /// <summary>
    /// Gets the time of the newest sample, in nanoseconds. It is zero when the tracker is empty.
    /// </summary>
    public readonly long NewestNanos => _count == 0 ? 0 : _ring[Newest].Nanos;

    private readonly int Newest => (_head + Capacity - 1) % Capacity;

    /// <summary>
    /// Adds a sample. A sample at or before the newest time replaces the newest position and changes no velocity. A gap of <see cref="StaleNanos"/> or more starts again.
    /// </summary>
    /// <param name="point">The position of the sample.</param>
    /// <param name="nanos">The time of the sample, in nanoseconds.</param>
    public void Add(Vector3 point, long nanos)
    {
        if (_count > 0)
        {
            ref var newest = ref _ring[Newest];
            var elapsed = nanos - newest.Nanos;
            if (elapsed <= 0)
            {
                newest.Point = point;
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
                var instant = (point - partner.Point) / (float)((nanos - partner.Nanos) / 1e9);
                if (_hasVelocity)
                {
                    _velocity += Smoothing * (instant - _velocity);
                }
                else
                {
                    _velocity = instant;
                    _hasVelocity = true;
                }
            }
        }

        _ring[_head] = new Sample { Point = point, Nanos = nanos };
        _head = (_head + 1) % Capacity;
        if (_count < Capacity)
        {
            _count++;
        }
    }

    /// <summary>
    /// Gets the smoothed velocity at a time.
    /// </summary>
    /// <param name="nowNanos">The current time, in nanoseconds.</param>
    /// <returns>The velocity in units per second, or zero when the newest sample is <see cref="StaleNanos"/> or more older than <paramref name="nowNanos"/>.</returns>
    public readonly Vector3 Velocity(long nowNanos) =>
        _hasVelocity && _count > 0 && nowNanos - _ring[Newest].Nanos < StaleNanos ? _velocity : Vector3.Zero;

    /// <summary>
    /// Removes every sample and the velocity.
    /// </summary>
    public void Reset()
    {
        _head = 0;
        _count = 0;
        _velocity = Vector3.Zero;
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
        public Vector3 Point;
        public long Nanos;
    }

    [InlineArray(Capacity)]
    private struct Ring
    {
        private Sample _element;
    }
}
