using System.Threading;

namespace Waylonia.Audio;

internal sealed class AudioRing
{
    public const int CapacityMillis = 500;

    public const int TargetMillis = 120;

    private readonly float[] _buffer;
    private readonly int _mask;
    private readonly int _channels;
    private readonly int _target;

    private int _read;
    private int _write;
    private bool _primed;
    private long _underruns;
    private long _dropped;

    public AudioRing(int channels, int capacityFrames, int targetFrames)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacityFrames, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(targetFrames);

        var capacity = (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)(capacityFrames * channels));
        _buffer = new float[capacity];
        _mask = capacity - 1;
        _channels = channels;
        _target = Math.Min(targetFrames * channels, capacity);
        _primed = _target == 0;
    }

    public static AudioRing ForSession(int rate, int channels) => new(
        channels,
        Math.Max(1, rate * CapacityMillis / 1000),
        Math.Max(1, rate * TargetMillis / 1000));

    public int Capacity => _buffer.Length;

    public int Target => _target;

    public int Depth => Volatile.Read(ref _write) - Volatile.Read(ref _read);

    public long Underruns => Volatile.Read(ref _underruns);

    public long Dropped => Volatile.Read(ref _dropped);

    public bool Primed => _primed;

    public int Write(ReadOnlySpan<float> samples)
    {
        if (samples.Length == 0)
        {
            return 0;
        }

        var capacity = _buffer.Length;
        var source = samples.Length > capacity ? samples[^capacity..] : samples;
        var count = source.Length;
        var write = _write;
        var offset = write & _mask;
        var first = Math.Min(count, capacity - offset);
        source[..first].CopyTo(_buffer.AsSpan(offset, first));
        if (first < count)
        {
            source[first..].CopyTo(_buffer.AsSpan(0, count - first));
        }

        Volatile.Write(ref _write, write + count);
        return count;
    }

    public int Read(Span<float> destination)
    {
        if (destination.Length == 0)
        {
            return 0;
        }

        var read = _read;
        var write = Volatile.Read(ref _write);
        var available = write - read;
        if (!_primed)
        {
            if (available < _target)
            {
                destination.Clear();
                return 0;
            }

            _primed = true;
        }

        if (available < 0 || available > _buffer.Length)
        {
            _dropped += available > _target ? available - _target : 0;
            read = write - _target;
            available = _target;
        }

        var count = Math.Min(destination.Length, available);
        if (count > 0)
        {
            var offset = read & _mask;
            var first = Math.Min(count, _buffer.Length - offset);
            _buffer.AsSpan(offset, first).CopyTo(destination[..first]);
            if (first < count)
            {
                _buffer.AsSpan(0, count - first).CopyTo(destination.Slice(first, count - first));
            }
        }

        Volatile.Write(ref _read, read + count);
        if (count < destination.Length)
        {
            destination[count..].Clear();
            _underruns++;
        }

        return count;
    }
}
