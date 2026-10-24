using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Basin.Diagnostics;

/// <summary>
/// Process-wide lifetime counters that tests check at teardown. The tracking calls exist only in builds that define <c>BASIN_COUNTERS</c>, and nothing locks them.
/// </summary>
public static class BasinCounters
{
    /// <summary>
    /// A value indicating whether this build defines <c>BASIN_COUNTERS</c>. When it is false, the counters always read 0.
    /// </summary>
    public static readonly bool Enabled =
#if BASIN_COUNTERS
        true;
#else
        false;
#endif

    /// <summary>
    /// The number of tracked objects that are alive. Code must change it through <see cref="Track"/> and <see cref="Untrack"/> only.
    /// </summary>
    public static int LiveObjects;

    /// <summary>
    /// The number of deferred frees that did not run yet.
    /// </summary>
    public static int PendingFrees;

    /// <summary>
    /// Gets or sets a value indicating whether each tracking call records a stack trace for the census. The default comes from <c>BASIN_CENSUS_ORIGINS</c>.
    /// </summary>
    public static bool CaptureOrigins { get; set; } =
        Environment.GetEnvironmentVariable("BASIN_CENSUS_ORIGINS") is not null;

    private static readonly Dictionary<string, FileCensus> Files = new(StringComparer.Ordinal);

    /// <summary>
    /// Records that tracked objects were created. The compiler removes the call without <c>BASIN_COUNTERS</c>.
    /// </summary>
    /// <param name="count">The number of objects.</param>
    /// <param name="file">The source file of the caller, for the census.</param>
    /// <param name="line">The source line of the caller, for the census.</param>
    [Conditional("BASIN_COUNTERS")]
    public static void Track(int count = 1, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        LiveObjects += count;
        Record(file, line, count);
    }

    /// <summary>
    /// Records that tracked objects were destroyed. The compiler removes the call without <c>BASIN_COUNTERS</c>.
    /// </summary>
    /// <param name="count">The number of objects.</param>
    /// <param name="file">The source file of the caller, for the census.</param>
    /// <param name="line">The source line of the caller, for the census.</param>
    [Conditional("BASIN_COUNTERS")]
    public static void Untrack(int count = 1, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        LiveObjects -= count;
        Record(file, line, -count);
    }

    /// <summary>
    /// Records a deferred free that is queued. The compiler removes the call without <c>BASIN_COUNTERS</c>.
    /// </summary>
    [Conditional("BASIN_COUNTERS")]
    public static void TrackPendingFree() => PendingFrees++;

    /// <summary>
    /// Records a deferred free that ran. The compiler removes the call without <c>BASIN_COUNTERS</c>.
    /// </summary>
    [Conditional("BASIN_COUNTERS")]
    public static void UntrackPendingFree() => PendingFrees--;

    /// <summary>
    /// Sets both counters to 0 and clears the census.
    /// </summary>
    public static void Reset()
    {
        LiveObjects = 0;
        PendingFrees = 0;
        Files.Clear();
    }

    /// <summary>
    /// Copies the live count of each source file into a dictionary. The dictionary stays empty when tracking is compiled out.
    /// </summary>
    /// <param name="into">The dictionary to clear and fill, keyed by source file path.</param>
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

    /// <summary>
    /// Formats the census as text.
    /// </summary>
    /// <returns>The same text that <see cref="WriteCensus"/> writes.</returns>
    public static string CensusReport()
    {
        var writer = new StringWriter();
        WriteCensus(writer);
        return writer.ToString();
    }

    /// <summary>
    /// Writes the live count of each source file, the net count of each tracking line, and the recorded stack traces.
    /// </summary>
    /// <param name="writer">The destination of the report.</param>
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
