using Basin.Capabilities;
using Wayland;
using Xkb;

namespace Basin.Seat;

/// <summary>
/// The default <see cref="IIdleSource"/>. It measures idle time with a monotonic stopwatch and also counts touch activity from a <see cref="TouchRouter"/>.
/// </summary>
public sealed class SeatIdleSource : IIdleSource, ITouchActivitySink
{
    private readonly System.Diagnostics.Stopwatch _since = System.Diagnostics.Stopwatch.StartNew();
    private int _inhibitors;

    /// <inheritdoc/>
    public long IdleMillis => IsInhibited ? 0 : _since.ElapsedMilliseconds;

    /// <inheritdoc/>
    public bool IsInhibited => _inhibitors > 0;

    /// <inheritdoc/>
    public event Action? Activity;

    /// <inheritdoc/>
    public event Action? InhibitionChanged;

    /// <inheritdoc/>
    public void NotifyActivity()
    {
        _since.Restart();
        Activity?.Invoke();
    }

    void ITouchActivitySink.OnTouchActivity() => NotifyActivity();

    /// <inheritdoc/>
    public IDisposable Inhibit()
    {
        _inhibitors++;
        InhibitionChanged?.Invoke();
        return new Inhibitor(this);
    }

    private sealed class Inhibitor : IDisposable
    {
        private SeatIdleSource? _owner;

        public Inhibitor(SeatIdleSource owner) => _owner = owner;

        public void Dispose()
        {
            if (_owner is { } owner)
            {
                _owner = null;
                owner._inhibitors--;
                owner.InhibitionChanged?.Invoke();
            }
        }
    }
}
