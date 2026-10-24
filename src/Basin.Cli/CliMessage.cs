namespace Basin.Cli;

/// <summary>
/// Formats the standard-error lines of a program in the GNU form: the program name, a colon, and a lowercase message with no final period.
/// </summary>
public static class CliMessage
{
    /// <summary>
    /// Formats a message as one line for the program.
    /// </summary>
    /// <param name="program">The program name, for example <c>basinctl</c>.</param>
    /// <param name="message">The message, which can be a capitalized sentence.</param>
    /// <returns>The line.</returns>
    public static string Line(string program, string message) => $"{program}: {Text(message)}";

    /// <summary>
    /// Formats the message of an exception as one line for the program. The parameter suffix of an <see cref="ArgumentException"/> is removed.
    /// </summary>
    /// <param name="program">The program name.</param>
    /// <param name="exception">The exception.</param>
    /// <returns>The line.</returns>
    public static string Line(string program, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var message = exception.Message;
        if (exception is ArgumentException { ParamName: { } name })
        {
            message = message.Replace($" (Parameter '{name}')", string.Empty, StringComparison.Ordinal);
        }

        return Line(program, message);
    }

    /// <summary>
    /// Converts a sentence to the GNU form. The lines join into one, the first letter becomes lowercase unless the first word holds another capital, and one final period is removed.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>The converted message.</returns>
    public static string Text(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var text = string.Join(' ', message.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (text.EndsWith('.') && !text.EndsWith("..", StringComparison.Ordinal))
        {
            text = text[..^1];
        }

        var word = text.IndexOf(' ', StringComparison.Ordinal) is var space and >= 0 ? text[..space] : text;
        return word.Length > 0 && char.IsUpper(word[0]) && word.AsSpan(1).IndexOfAnyInRange('A', 'Z') < 0
            ? char.ToLowerInvariant(word[0]) + text[1..]
            : text;
    }
}
