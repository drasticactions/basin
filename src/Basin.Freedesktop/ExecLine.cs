using System.Text;

namespace Basin.Freedesktop;

/// <summary>
/// Converts between the <c>Exec</c> value of a desktop entry and an argument vector, by the quoting rules of the Desktop Entry specification.
/// </summary>
public static class ExecLine
{
    private const string FieldCodes = "fFuUdDnNickvm";

    private const string Reserved = " \t\n\"'\\><~|&;$*?#()`";

    /// <summary>
    /// Splits an <c>Exec</c> value into arguments on unquoted spaces and decodes the escapes inside double quotes.
    /// Field codes are removed and <c>%%</c> becomes <c>%</c>. An unquoted argument that only field codes made is dropped.
    /// </summary>
    /// <param name="exec">The raw <c>Exec</c> value.</param>
    /// <returns>A new array of arguments. It is empty for an empty line.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="exec"/> is <see langword="null"/>.</exception>
    public static string[] Split(string exec)
    {
        ArgumentNullException.ThrowIfNull(exec);
        var argv = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        var wasQuoted = false;
        var hadFieldCode = false;
        for (var i = 0; i < exec.Length; i++)
        {
            var c = exec[i];
            if (quoted)
            {
                if (c == '"')
                {
                    quoted = false;
                }
                else if (c == '\\' && i + 1 < exec.Length && exec[i + 1] is '"' or '`' or '$' or '\\')
                {
                    current.Append(exec[++i]);
                }
                else
                {
                    current.Append(c);
                }

                continue;
            }

            if (c == '"')
            {
                quoted = true;
                wasQuoted = true;
            }
            else if (c == ' ')
            {
                Flush(argv, current, wasQuoted, hadFieldCode);
                wasQuoted = false;
                hadFieldCode = false;
            }
            else if (c == '%' && i + 1 < exec.Length)
            {
                var code = exec[++i];
                if (code == '%')
                {
                    current.Append('%');
                }
                else
                {
                    hadFieldCode = true;
                }
            }
            else
            {
                current.Append(c);
            }
        }

        Flush(argv, current, wasQuoted, hadFieldCode);
        return [.. argv];
    }

    /// <summary>
    /// Joins arguments into one line that is a valid <c>Exec</c> value and a valid <c>sh -c</c> line.
    /// An argument that is empty or holds a reserved character is put in double quotes. A percent sign stays literal.
    /// </summary>
    /// <param name="argv">The arguments to join.</param>
    /// <returns>The arguments separated by single spaces.</returns>
    public static string Join(ReadOnlySpan<string> argv)
    {
        var builder = new StringBuilder();
        foreach (var argument in argv)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            if (argument.Length > 0 && argument.AsSpan().IndexOfAny(Reserved) < 0)
            {
                builder.Append(argument);
                continue;
            }

            builder.Append('"');
            foreach (var c in argument)
            {
                if (c is '"' or '`' or '$' or '\\')
                {
                    builder.Append('\\');
                }

                builder.Append(c);
            }

            builder.Append('"');
        }

        return builder.ToString();
    }

    /// <summary>
    /// Finds a terminal command for <see cref="DesktopEntry.LaunchArgv(IReadOnlyList{string})"/> from the environment.
    /// </summary>
    /// <returns><c>$TERMINAL</c> split like an <c>Exec</c> line with <c>-e</c> added, then <c>xdg-terminal-exec</c> when it is on <c>PATH</c>, otherwise <see langword="null"/>.</returns>
    public static string[]? TerminalFromEnvironment()
    {
        var terminal = Environment.GetEnvironmentVariable("TERMINAL");
        if (!string.IsNullOrWhiteSpace(terminal) && Split(terminal) is { Length: > 0 } argv)
        {
            return [.. argv, "-e"];
        }

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':'))
        {
            if (directory.Length > 0 && File.Exists(Path.Combine(directory, "xdg-terminal-exec")))
            {
                return ["xdg-terminal-exec"];
            }
        }

        return null;
    }

    internal static bool IsFieldCode(char c) => FieldCodes.Contains(c);

    private static void Flush(List<string> argv, StringBuilder current, bool wasQuoted, bool hadFieldCode)
    {
        if (current.Length > 0 || (wasQuoted && !hadFieldCode))
        {
            argv.Add(current.ToString());
        }

        current.Clear();
    }
}
