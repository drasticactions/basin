namespace Basin.Freedesktop;

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
                    return MinSize * Scale - wanted;
                }

                return wanted > (Size + Threshold) * Scale ? wanted - MaxSize * Scale : 0;
        }
    }
}
