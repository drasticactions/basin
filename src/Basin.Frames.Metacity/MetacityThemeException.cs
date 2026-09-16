namespace Basin.Frames.Metacity;

public sealed class MetacityThemeException : Exception
{
    public MetacityThemeException(string message)
        : base(message)
    {
    }

    public MetacityThemeException(string message, Exception inner)
        : base(message, inner)
    {
    }

    internal bool TooOld { get; init; }
}
