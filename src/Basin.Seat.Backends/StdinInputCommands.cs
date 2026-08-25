using System.Globalization;

namespace Basin.Seat.Backends;

public sealed class StdinInputCommands
{
    private readonly SeatInjector _injector;

    public StdinInputCommands(SeatInjector injector)
    {
        ArgumentNullException.ThrowIfNull(injector);
        _injector = injector;
    }

    public bool Handle(ReadOnlySpan<string> parts)
    {
        switch (parts)
        {
            case ["move", var x, var y]:
                _injector.Warp(Number(x), Number(y));
                return true;
            case ["button", var code, var state]:
                _injector.Button(uint.Parse(code, CultureInfo.InvariantCulture), state == "1");
                return true;
            case ["key", var code, var state]:
                _injector.Key(uint.Parse(code, CultureInfo.InvariantCulture), state == "1");
                return true;
            default:
                return false;
        }
    }

    private static double Number(string value) => double.Parse(value, CultureInfo.InvariantCulture);
}
