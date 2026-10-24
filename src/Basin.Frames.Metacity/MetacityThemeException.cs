namespace Basin.Frames.Metacity;

/// <summary>
/// The error that a theme load raises. The message uses marco's wording, with a line and character position where one applies.
/// </summary>
public sealed class MetacityThemeException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MetacityThemeException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public MetacityThemeException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MetacityThemeException"/> class with the exception that caused it.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="inner">The cause, for example an XML error.</param>
    public MetacityThemeException(string message, Exception inner)
        : base(message, inner)
    {
    }

    internal bool TooOld { get; init; }
}
