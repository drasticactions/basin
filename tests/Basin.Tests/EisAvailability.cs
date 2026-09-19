using Basin.Eis;

namespace Basin.Tests;

public static class EisAvailability
{
    public const string Missing = "libeis.so.1 is not installed";

    public static bool Loaded => EisLibrary.IsAvailable(out _);
}
