using System.Diagnostics;

namespace Basin.WindowManager;

/// <summary>
/// The time that a manager takes to answer each sequence, which is time that the compositor waits. Recording never allocates.
/// </summary>
public sealed class WmLatency
{
    private const int Capacity = 1024;

    private readonly long[] _samples = new long[Capacity];
    private int _next;
    private int _count;
    private long _total;
    private long _worst;

    /// <summary>
    /// Gets the number of sequences recorded since the start.
    /// </summary>
    public long Sequences { get; private set; }

    /// <summary>
    /// Gets the longest time recorded since the start.
    /// </summary>
    public TimeSpan Worst => FromTicks(_worst);

    /// <summary>
    /// Gets the mean of the last 1024 samples, or zero before the first sample.
    /// </summary>
    public TimeSpan Mean => _count == 0 ? TimeSpan.Zero : FromTicks(_total / _count);

    /// <summary>
    /// Gets the median of the last 1024 samples. Reading it sorts a copy, so read it off the frame path.
    /// </summary>
    public TimeSpan Median => Percentile(0.50);

    /// <summary>
    /// Gets the 99th percentile of the last 1024 samples. Reading it sorts a copy, so read it off the frame path.
    /// </summary>
    public TimeSpan P99 => Percentile(0.99);

    /// <summary>
    /// Gets a percentile of the last 1024 samples by nearest rank. It allocates and sorts a copy.
    /// </summary>
    /// <param name="fraction">The percentile as a fraction from 0 to 1.</param>
    /// <returns>The sample at that rank, or zero before the first sample.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="fraction"/> is outside 0 to 1.</exception>
    public TimeSpan Percentile(double fraction)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fraction);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(fraction, 1.0);
        if (_count == 0)
        {
            return TimeSpan.Zero;
        }

        var sorted = _samples.AsSpan(0, _count).ToArray();
        Array.Sort(sorted);
        var index = (int)Math.Clamp(Math.Round(fraction * (sorted.Length - 1)), 0, sorted.Length - 1);
        return FromTicks(sorted[index]);
    }

    /// <inheritdoc/>
    public override string ToString() =>
        $"{Sequences} sequences, p50 {Median.TotalMilliseconds:F3}ms, p99 {P99.TotalMilliseconds:F3}ms, worst {Worst.TotalMilliseconds:F3}ms";

    internal void Record(long elapsedTicks)
    {
        Sequences++;
        if (elapsedTicks > _worst)
        {
            _worst = elapsedTicks;
        }

        if (_count == Capacity)
        {
            _total -= _samples[_next];
        }
        else
        {
            _count++;
        }

        _samples[_next] = elapsedTicks;
        _total += elapsedTicks;
        _next = (_next + 1) % Capacity;
    }

    private static TimeSpan FromTicks(long stopwatchTicks) =>
        TimeSpan.FromTicks((long)(stopwatchTicks * TicksPerStopwatchTick));

    private static readonly double TicksPerStopwatchTick =
        (double)TimeSpan.TicksPerSecond / Stopwatch.Frequency;
}
