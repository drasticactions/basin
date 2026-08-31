using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Basin.Diagnostics;

public static class AllocationScope
{
    private const int MaxDepth = 8;

    private const int MaxRegions = 32;

    private const int ForgivenessBudget = 8;

    private static readonly string[] Names = new string[MaxRegions];
    private static readonly int[] Entered = new int[MaxRegions];
    private static readonly int[] Forgiven = new int[MaxRegions];
    private static readonly string[] Open = new string[MaxDepth];
    private static readonly long[] Started = new long[MaxDepth];
    private static readonly bool[] Forgiving = new bool[MaxDepth];

    private const int MaxPauses = 8;

    private static readonly long[] PausedAt = new long[MaxPauses];
    private static readonly int[] PausedDepth = new int[MaxPauses];

    private static int _regions;
    private static int _depth;
    private static int _paused;

    public static bool Enabled { get; set; } = true;

    [Conditional("BASIN_COUNTERS")]
    public static void Begin(int warmup = 0, [CallerMemberName] string region = "", bool forgiving = false)
    {
        if (_depth == MaxDepth)
        {
            throw new InvalidOperationException(
                $"Allocation scopes nested more than {MaxDepth} deep, entering '{region}'.");
        }

        var index = IndexOf(region);
        var seen = Entered[index]++;

        Open[_depth] = region;
        Started[_depth] = Enabled && seen >= warmup ? GC.GetAllocatedBytesForCurrentThread() : -1;
        Forgiving[_depth] = forgiving;
        _depth++;
    }

    [Conditional("BASIN_COUNTERS")]
    public static void End(long allowance = 0)
    {
        if (_depth == 0)
        {
            throw new InvalidOperationException("An allocation scope ended that never began.");
        }

        var started = Started[--_depth];
        if (started < 0)
        {
            return;
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - started;
        if (allocated > allowance)
        {
            var index = IndexOf(Open[_depth]);
            if (Forgiving[_depth] && Forgiven[index] < ForgivenessBudget)
            {
                Forgiven[index]++;
                return;
            }

            throw new InvalidOperationException(
                $"'{Open[_depth]}' allocated {allocated} bytes on a path allowed {allowance} (entry {Entered[index]}).");
        }
    }

    [Conditional("BASIN_COUNTERS")]
    public static void Pause()
    {
        if (_paused == MaxPauses)
        {
            throw new InvalidOperationException($"Allocation pauses nested more than {MaxPauses} deep.");
        }

        PausedAt[_paused] = GC.GetAllocatedBytesForCurrentThread();
        PausedDepth[_paused] = _depth;
        _paused++;
    }

    [Conditional("BASIN_COUNTERS")]
    public static void Resume()
    {
        if (_paused == 0)
        {
            throw new InvalidOperationException("An allocation pause resumed that never began.");
        }

        _paused--;
        var exempt = GC.GetAllocatedBytesForCurrentThread() - PausedAt[_paused];
        var from = _paused > 0 ? PausedDepth[_paused - 1] : 0;
        for (var i = from; i < PausedDepth[_paused]; i++)
        {
            if (Started[i] >= 0)
            {
                Started[i] += exempt;
            }
        }
    }

    public static void Reset()
    {
        _depth = 0;
        _paused = 0;
        for (var i = 0; i < _regions; i++)
        {
            Entered[i] = 0;
            Forgiven[i] = 0;
        }
    }

    private static int IndexOf(string region)
    {
        for (var i = 0; i < _regions; i++)
        {
            if (ReferenceEquals(Names[i], region))
            {
                return i;
            }
        }

        if (_regions == MaxRegions)
        {
            throw new InvalidOperationException(
                $"More than {MaxRegions} allocation scopes have been named, adding '{region}'.");
        }

        Names[_regions] = region;
        Entered[_regions] = 0;
        return _regions++;
    }
}
