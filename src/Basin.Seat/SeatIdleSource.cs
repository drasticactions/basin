using Basin.Capabilities;
using Wayland;
using Xkb;

namespace Basin.Seat;

public sealed class SeatIdleSource : IIdleSource, ITouchActivitySink
{
    private readonly System.Diagnostics.Stopwatch _since = System.Diagnostics.Stopwatch.StartNew();
    private int _inhibitors;

    public long IdleMillis => IsInhibited ? 0 : _since.ElapsedMilliseconds;

    public bool IsInhibited => _inhibitors > 0;

    public event Action? Activity;

    public event Action? InhibitionChanged;

    public void NotifyActivity()
    {
        _since.Restart();
        Activity?.Invoke();
    }

    void ITouchActivitySink.OnTouchActivity() => NotifyActivity();

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
