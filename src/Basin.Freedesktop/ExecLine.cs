using System.Text;

namespace Basin.Freedesktop;

public static class ExecLine
{
    private const string FieldCodes = "fFuUdDnNickvm";

    private const string Reserved = " \t\n\"'\\><~|&;$*?#()`";

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
