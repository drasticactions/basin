using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Basin.Diagnostics;

public static class BasinCounters
{
    public static readonly bool Enabled =
#if BASIN_COUNTERS
        true;
#else
        false;
#endif

    public static int LiveObjects;

    public static int PendingFrees;

    public static bool CaptureOrigins { get; set; } =
        Environment.GetEnvironmentVariable("BASIN_CENSUS_ORIGINS") is not null;

    private static readonly Dictionary<string, FileCensus> Files = new(StringComparer.Ordinal);

    [Conditional("BASIN_COUNTERS")]
    public static void Track(int count = 1, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        LiveObjects += count;
        Record(file, line, count);
    }

    [Conditional("BASIN_COUNTERS")]
    public static void Untrack(int count = 1, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        LiveObjects -= count;
        Record(file, line, -count);
    }

    [Conditional("BASIN_COUNTERS")]
    public static void TrackPendingFree() => PendingFrees++;

    [Conditional("BASIN_COUNTERS")]
    public static void UntrackPendingFree() => PendingFrees--;

    public static void Reset()
    {
        LiveObjects = 0;
        PendingFrees = 0;
        Files.Clear();
    }

    public static void SnapshotCensus(IDictionary<string, int> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        into.Clear();
        if (!Enabled)
        {
            return;
        }

        foreach (var (file, census) in Files)
        {
            into[file] = census.Live;
        }
    }

    public static string CensusReport()
    {
        var writer = new StringWriter();
        WriteCensus(writer);
        return writer.ToString();
    }

    public static void WriteCensus(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (!Enabled)
        {
            writer.WriteLine("census: lifetime tracking is compiled out of this build");
            return;
        }

        writer.WriteLine($"census: {LiveObjects} live objects, {PendingFrees} pending frees");
        foreach (var (file, census) in Files.OrderByDescending(entry => entry.Value.Live))
        {
            if (census.Live <= 0)
            {
                continue;
            }

            writer.WriteLine($"  {census.Live,6}  {file}");
            foreach (var (line, count) in census.Lines.OrderBy(entry => entry.Key))
            {
                if (count != 0)
                {
                    writer.WriteLine($"          {count:+#;-#} at :{line}");
                }
            }

            if (census.Origins is not { Count: > 0 } origins)
            {
                continue;
            }

            for (var i = 0; i < origins.Count; i++)
            {
                writer.WriteLine(origins[i]);
            }
        }
    }

    private sealed class FileCensus
    {
        public int Live;

        public readonly Dictionary<int, int> Lines = [];

        public List<string>? Origins;
    }

    private static void Record(string file, int line, int delta)
    {
        if (!Files.TryGetValue(file, out var census))
        {
            census = new FileCensus();
            Files[file] = census;
        }

        census.Live += delta;
        census.Lines.TryGetValue(line, out var count);
        census.Lines[line] = count + delta;
        if (!CaptureOrigins)
        {
            return;
        }

        var origins = census.Origins ??= [];
        for (var i = 0; i < delta; i++)
        {
            origins.Add(Environment.StackTrace);
        }

        for (var i = 0; i < -delta && origins.Count > 0; i++)
        {
            origins.RemoveAt(origins.Count - 1);
        }
    }
}
