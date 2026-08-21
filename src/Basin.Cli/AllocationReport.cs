using System.Globalization;
using System.Runtime;

namespace Basin.Cli;

public readonly struct AllocationReport : IEquatable<AllocationReport>
{
    private readonly long _bytes;
    private readonly long _threadBytes;
    private readonly int _gen0;
    private readonly int _gen1;
    private readonly int _gen2;

    private AllocationReport(long bytes, long threadBytes, int gen0, int gen1, int gen2)
    {
        _bytes = bytes;
        _threadBytes = threadBytes;
        _gen0 = gen0;
        _gen1 = gen1;
        _gen2 = gen2;
    }

    public static AllocationReport Capture() => new(
        GC.GetTotalAllocatedBytes(precise: true),
        GC.GetAllocatedBytesForCurrentThread(),
        GC.CollectionCount(0),
        GC.CollectionCount(1),
        GC.CollectionCount(2));

    public string Since(long frames)
    {
        var now = Capture();
        var bytes = now._bytes - _bytes;
        var thread = now._threadBytes - _threadBytes;
        var perFrame = frames > 0
            ? (thread / frames).ToString(CultureInfo.InvariantCulture)
            : "n/a";

        return string.Create(
            CultureInfo.InvariantCulture,
            $"ALLOC bytes={bytes} thread={thread} per-frame={perFrame} gen0={now._gen0 - _gen0} gen1={now._gen1 - _gen1} gen2={now._gen2 - _gen2} gc={CollectorName()}");
    }

    public bool Equals(AllocationReport other) =>
        _bytes == other._bytes
        && _threadBytes == other._threadBytes
        && _gen0 == other._gen0
        && _gen1 == other._gen1
        && _gen2 == other._gen2;

    public override bool Equals(object? obj) => obj is AllocationReport other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(_bytes, _threadBytes, _gen0, _gen1, _gen2);

    public static bool operator ==(AllocationReport left, AllocationReport right) => left.Equals(right);

    public static bool operator !=(AllocationReport left, AllocationReport right) => !left.Equals(right);

    private static string CollectorName()
    {
        var builtIn = false;
        foreach (var entry in GC.GetConfigurationVariables())
        {
            if (string.Equals(entry.Key, "GCName", StringComparison.OrdinalIgnoreCase))
            {
                builtIn = true;
                break;
            }
        }

        if (builtIn)
        {
            return GCSettings.IsServerGC ? "server" : "workstation";
        }

        return Environment.GetEnvironmentVariable("DOTNET_GCName") is { Length: > 0 } asked
            ? "standalone:" + asked
            : "standalone";
    }
}
