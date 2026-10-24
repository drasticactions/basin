namespace Basin.Themes.MsStyles;

/// <summary>
/// The exception that a theme reader throws when a file is not a valid visual style or its structure is damaged.
/// </summary>
public sealed class MsStylesFormatException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MsStylesFormatException"/> class. When <paramref name="offset"/> is 0 or more, the message ends with it in hexadecimal.
    /// </summary>
    /// <param name="message">The description of the problem.</param>
    /// <param name="offset">The byte offset of the problem in the data, or -1 when it is not known.</param>
    public MsStylesFormatException(string message, long offset = -1)
        : base(offset >= 0 ? $"{message} (at offset 0x{offset:x})" : message)
    {
        Offset = offset;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MsStylesFormatException"/> class with the exception that caused it. <see cref="Offset"/> is -1.
    /// </summary>
    /// <param name="message">The description of the problem.</param>
    /// <param name="inner">The exception that caused this exception.</param>
    public MsStylesFormatException(string message, Exception inner)
        : base(message, inner)
    {
        Offset = -1;
    }

    /// <summary>
    /// Gets the byte offset of the problem in the data. It is -1 when the offset is not known.
    /// </summary>
    public long Offset { get; }
}
