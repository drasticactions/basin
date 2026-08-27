namespace Waylonia;

internal static class ProcessTree
{
    public static bool IsDescendant(int pid, int root)
    {
        if (pid <= 0 || root <= 0)
        {
            return false;
        }

        for (var depth = 0; depth < 32 && pid > 1; depth++)
        {
            if (pid == root)
            {
                return true;
            }

            if (ParentOf(pid) is not { } parent || parent == pid)
            {
                return false;
            }

            pid = parent;
        }

        return false;
    }

    private static int? ParentOf(int pid)
    {
        string text;
        try
        {
            text = File.ReadAllText($"/proc/{pid}/stat");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        var close = text.LastIndexOf(')');
        if (close < 0 || close + 2 >= text.Length)
        {
            return null;
        }

        var fields = text[(close + 2)..].Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        return fields.Length >= 2 && int.TryParse(fields[1], out var parent) ? parent : null;
    }
}
