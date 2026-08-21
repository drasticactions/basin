namespace Basin.Seat;

public sealed class TouchPointerEmulator
{
    private readonly SeatTouch _touch;
    private int _slot = -1;

    public TouchPointerEmulator(SeatTouch touch) => _touch = touch;

    public int Slot => _slot;

    public bool Active => _slot >= 0;

    public bool TryClaim(int slot, Surface? surface)
    {
        if (_slot >= 0 || _touch.Accepts(surface))
        {
            return false;
        }

        _slot = slot;
        return true;
    }

    public bool Owns(int slot) => _slot >= 0 && _slot == slot;

    public bool Release(int slot)
    {
        if (!Owns(slot))
        {
            return false;
        }

        _slot = -1;
        return true;
    }

    public bool Cancel()
    {
        if (_slot < 0)
        {
            return false;
        }

        _slot = -1;
        return true;
    }
}
