namespace Basin.Diagnostics;

internal static class LogScratch
{
    private const int Size = 512;

    [ThreadStatic]
    private static char[]? _buffer;

    internal static char[] Take()
    {
        var taken = _buffer ?? new char[Size];
        _buffer = null;
        return taken;
    }

    internal static void Return(char[] buffer) => _buffer = buffer;
}
