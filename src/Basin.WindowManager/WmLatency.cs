using System.Diagnostics;

namespace Basin.WindowManager;

public sealed class WmLatency
{
    private const int Capacity = 1024;

    private readonly long[] _samples = new long[Capacity];
    private int _next;
    private int _count;
    private long _total;
    private long _worst;

    public long Sequences { get; private set; }

    public TimeSpan Worst => FromTicks(_worst);

    public TimeSpan Mean => _count == 0 ? TimeSpan.Zero : FromTicks(_total / _count);

    public TimeSpan Median => Percentile(0.50);

    public TimeSpan P99 => Percentile(0.99);

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
