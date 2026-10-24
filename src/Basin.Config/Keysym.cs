using Xkb;

namespace Basin.Config;

/// <summary>
/// Looks up xkb keysyms by name.
/// </summary>
public static class Keysym
{
    /// <summary>
    /// The keysym value that means no symbol.
    /// </summary>
    public const uint NoSymbol = 0;

    /// <summary>
    /// Finds the keysym for a name. An exact match comes first, then a case-insensitive match.
    /// </summary>
    /// <param name="name">The keysym name, for example <c>Return</c>.</param>
    /// <returns>The keysym, or <see cref="NoSymbol"/> when no keysym has the name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public static uint FromName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var symbol = XkbKeysym.FromName(name).Value;
        return symbol != NoSymbol ? symbol : XkbKeysym.FromName(name, caseInsensitive: true).Value;
    }

    /// <summary>
    /// Finds the keysym for a name in the same way as <see cref="FromName"/>, and throws when there is none.
    /// </summary>
    /// <param name="name">The keysym name.</param>
    /// <returns>The keysym.</returns>
    /// <exception cref="ArgumentException">No keysym has the name.</exception>
    public static uint Require(string name)
    {
        var symbol = FromName(name);
        return symbol != NoSymbol
            ? symbol
            : throw new ArgumentException($"The keysym name '{name}' is unknown.", nameof(name));
    }

}
