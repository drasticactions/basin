namespace Basin.Seat;

public sealed class TouchPointerDriver
{
    private const uint BtnLeft = 0x110;

    private readonly TouchPointerEmulator _emulator;
    private readonly ITouchPointerTarget _target;

    public TouchPointerDriver(SeatTouch touch, ITouchPointerTarget target)
    {
        ArgumentNullException.ThrowIfNull(touch);
        ArgumentNullException.ThrowIfNull(target);

        _emulator = new TouchPointerEmulator(touch);
        _target = target;
    }

    public bool ClaimWithoutSurface { get; set; }

    public bool Active => _emulator.Active;

    public bool Owns(int id) => _emulator.Owns(id);

    public bool TryClaim(int id, Surface? surface, uint timeMs, double x, double y)
    {
        if (surface is null && !ClaimWithoutSurface)
        {
            return false;
        }

        if (!_emulator.TryClaim(id, surface))
        {
            return false;
        }

        _target.Warp(timeMs, x, y);
        _target.Button(timeMs, BtnLeft, pressed: true);
        return true;
    }

    public bool Motion(int id, uint timeMs, double x, double y)
    {
        if (!_emulator.Owns(id))
        {
            return false;
        }

        _target.Warp(timeMs, x, y);
        return true;
    }

    public bool Release(int id, uint timeMs)
    {
        if (!_emulator.Release(id))
        {
            return false;
        }

        _target.Button(timeMs, BtnLeft, pressed: false);
        return true;
    }

    public bool Cancel()
    {
        if (!_emulator.Cancel())
        {
            return false;
        }

        _target.Button((uint)Environment.TickCount, BtnLeft, pressed: false);
        return true;
    }
}
