namespace System7Wm;

internal static class MemorySample
{
    public static (long TotalKb, long AvailableKb) System()
    {
        long total = 0;
        long available = 0;
        try
        {
            foreach (var line in File.ReadLines("/proc/meminfo"))
            {
                if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
                {
                    total = Kilobytes(line);
                }
                else if (line.StartsWith("MemAvailable:", StringComparison.Ordinal))
                {
                    available = Kilobytes(line);
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            System7Log.Log.Debug(error, $"memory: cannot read /proc/meminfo");
        }

        return (total, available);
    }

    public static (long PssKb, long AnonKb) Tree(int pid)
    {
        long pss = 0;
        long anon = 0;
        var seen = new HashSet<int>();
        var pending = new Stack<int>();
        pending.Push(pid);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (current <= 0 || !seen.Add(current))
            {
                continue;
            }

            var (p, a) = Process(current);
            pss += p;
            anon += a;
            foreach (var child in Children(current))
            {
                pending.Push(child);
            }
        }

        return (pss, anon);
    }

    private static (long Pss, long Anon) Process(int pid)
    {
        long pss = 0;
        long anon = 0;
        try
        {
            foreach (var line in File.ReadLines($"/proc/{pid}/smaps_rollup"))
            {
                if (line.StartsWith("Pss:", StringComparison.Ordinal))
                {
                    pss = Kilobytes(line);
                }
                else if (line.StartsWith("Pss_Anon:", StringComparison.Ordinal))
                {
                    anon = Kilobytes(line);
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            System7Log.Log.Debug(error, $"memory: cannot read /proc/{pid}/smaps_rollup");
        }

        return (pss, anon);
    }

    private static IEnumerable<int> Children(int pid)
    {
        var result = new List<int>();
        try
        {
            foreach (var task in Directory.EnumerateDirectories($"/proc/{pid}/task"))
            {
                var path = Path.Combine(task, "children");
                if (!File.Exists(path))
                {
                    continue;
                }

                foreach (var word in File.ReadAllText(path).Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (int.TryParse(word, out var child))
                    {
                        result.Add(child);
                    }
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            System7Log.Log.Debug(error, $"memory: cannot list the children of pid {pid}");
        }

        return result;
    }

    private static long Kilobytes(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && long.TryParse(parts[1], out var value) ? value : 0;
    }
}
