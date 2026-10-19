namespace Basin.Seat.Backends;

public sealed class TouchChromeChain : ITouchChrome
{
    private readonly Dictionary<int, ITouchChrome> _owners = [];

    public ITouchChrome? First { get; set; }

    public ITouchChrome? Then { get; set; }

    public bool TryPress(int id, uint timeMs, double x, double y)
    {
        if (First is { } first && first.TryPress(id, timeMs, x, y))
        {
            _owners[id] = first;
            return true;
        }

        if (Then is { } then && then.TryPress(id, timeMs, x, y))
        {
            _owners[id] = then;
            return true;
        }

        return false;
    }

    public void Motion(int id, uint timeMs, double x, double y)
    {
        if (_owners.TryGetValue(id, out var owner))
        {
            owner.Motion(id, timeMs, x, y);
        }
    }

    public void Release(int id, uint timeMs, double x, double y)
    {
        if (_owners.Remove(id, out var owner))
        {
            owner.Release(id, timeMs, x, y);
        }
    }

    public void Cancel()
    {
        _owners.Clear();
        First?.Cancel();
        Then?.Cancel();
    }
}
