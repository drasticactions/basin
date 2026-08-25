using Avalonia.Input;
using Basin.Diagnostics;
using static Basin.Avalonia.AvaloniaLog;

namespace Basin.Avalonia;

internal sealed class BasinInputChannel
{
    private readonly Lock _lock = new();
    private BasinInputEvent[] _events = new BasinInputEvent[1024];
    private int _read;
    private int _count;
    private bool _reportedGrowth;

    public void Write(in BasinInputEvent input)
    {
        lock (_lock)
        {
            if (_count == _events.Length)
            {
                var grown = new BasinInputEvent[_events.Length * 2];
                for (var i = 0; i < _count; i++)
                {
                    grown[i] = _events[(_read + i) % _events.Length];
                }

                _events = grown;
                _read = 0;
                if (!_reportedGrowth)
                {
                    _reportedGrowth = true;
                    Log.Info($"the input ring grew to {_events.Length} entries");
                }
            }

            _events[(_read + _count) % _events.Length] = input;
            _count++;
        }
    }

    public bool TryRead(out BasinInputEvent input)
    {
        lock (_lock)
        {
            if (_count == 0)
            {
                input = default;
                return false;
            }

            input = _events[_read];
            _read = (_read + 1) % _events.Length;
            _count--;
            return true;
        }
    }
}
