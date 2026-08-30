using Xkb;

namespace Basin.Config;

public static class Keysym
{
    public const uint NoSymbol = 0;

    public static uint FromName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var symbol = XkbKeysym.FromName(name).Value;
        return symbol != NoSymbol ? symbol : XkbKeysym.FromName(name, caseInsensitive: true).Value;
    }

    public static uint Require(string name)
    {
        var symbol = FromName(name);
        return symbol != NoSymbol
            ? symbol
            : throw new ArgumentException($"unknown keysym name '{name}'", nameof(name));
    }

}
