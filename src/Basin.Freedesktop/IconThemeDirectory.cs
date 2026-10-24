namespace Basin.Freedesktop;

/// <summary>
/// One directory that an <c>index.theme</c> file lists, with the defaults of the Icon Theme specification applied.
/// </summary>
/// <param name="Name">The directory path relative to the theme base directory, for example <c>apps/48</c>.</param>
/// <param name="Size">The nominal icon size, in logical pixels.</param>
/// <param name="Scale">The integer scale that the directory is for. It is 1 when the file does not say.</param>
/// <param name="Type">The rule that sets the sizes that the directory matches.</param>
/// <param name="MinSize">The smallest size a <see cref="IconThemeDirectoryType.Scaled"/> directory matches. It defaults to <paramref name="Size"/>.</param>
/// <param name="MaxSize">The largest size a <see cref="IconThemeDirectoryType.Scaled"/> directory matches. It defaults to <paramref name="Size"/>.</param>
/// <param name="Threshold">The size tolerance of a <see cref="IconThemeDirectoryType.Threshold"/> directory. It defaults to 2.</param>
/// <param name="Context">The <c>Context=</c> value, or <see langword="null"/>. Lookup does not filter on it.</param>
public sealed record IconThemeDirectory(
    string Name,
    int Size,
    int Scale,
    IconThemeDirectoryType Type,
    int MinSize,
    int MaxSize,
    int Threshold,
    string? Context)
{
    /// <summary>
    /// Tells whether the directory holds icons for a size at a scale, by the rule of <see cref="Type"/>.
    /// A directory never matches a scale other than its own <see cref="Scale"/>.
    /// </summary>
    /// <param name="size">The requested size, in logical pixels.</param>
    /// <param name="scale">The requested integer scale.</param>
    /// <returns><see langword="true"/> when the directory is an exact match for the request.</returns>
    public bool Matches(int size, int scale)
    {
        if (Scale != scale)
        {
            return false;
        }

        return Type switch
        {
            IconThemeDirectoryType.Fixed => Size == size,
            IconThemeDirectoryType.Scaled => MinSize <= size && size <= MaxSize,
            _ => Size - Threshold <= size && size <= Size + Threshold,
        };
    }

    /// <summary>
    /// Computes the distance function of the Icon Theme specification. Lookup uses it to pick the closest directory when none matches.
    /// A <c>Threshold</c> directory measures from its <c>Size</c>. It ignores <c>MinSize</c> and <c>MaxSize</c>.
    /// </summary>
    /// <param name="size">The requested size, in logical pixels.</param>
    /// <param name="scale">The requested integer scale.</param>
    /// <returns>The distance in physical pixels, never negative. Zero means that the request falls in the directory's range.</returns>
    public int Distance(int size, int scale)
    {
        var wanted = size * scale;
        switch (Type)
        {
            case IconThemeDirectoryType.Fixed:
                return Math.Abs(Size * Scale - wanted);
            case IconThemeDirectoryType.Scaled:
                if (wanted < MinSize * Scale)
                {
                    return MinSize * Scale - wanted;
                }

                return wanted > MaxSize * Scale ? wanted - MaxSize * Scale : 0;
            default:
                if (wanted < (Size - Threshold) * Scale)
                {
                    return Size * Scale - wanted;
                }

                return wanted > (Size + Threshold) * Scale ? wanted - Size * Scale : 0;
        }
    }
}
