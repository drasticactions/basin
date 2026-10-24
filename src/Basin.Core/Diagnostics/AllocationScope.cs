using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Basin.Diagnostics;

/// <summary>
/// Asserts at run time that a code region allocates no managed memory on the current thread. The calls exist only in builds that define <c>BASIN_COUNTERS</c>.
/// </summary>
public static class AllocationScope
{
    private const int MaxDepth = 8;

    private const int MaxRegions = 64;

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
    private static bool _thrown;

    /// <summary>
    /// Gets or sets a value indicating whether new scopes measure. A scope that begins while it is false never fails.
    /// </summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>
    /// Opens a measured region. Scopes can nest up to 8 deep, and up to 64 region names can exist.
    /// </summary>
    /// <param name="warmup">The number of first entries to the region that are not measured.</param>
    /// <param name="region">The region name. It must be a string literal, because names compare by reference.</param>
    /// <param name="forgiving">A value that permits the region to fail 8 times before it throws.</param>
    [Conditional("BASIN_COUNTERS")]
    public static void Begin(int warmup = 0, [CallerMemberName] string region = "", bool forgiving = false)
    {
        if (_depth == MaxDepth)
        {
            throw new InvalidOperationException(
                $"Allocation scopes nested more than {MaxDepth} deep, entering '{region}'.");
        }

        if (_depth == 0)
        {
            _thrown = false;
        }

        var index = IndexOf(region);
        var seen = Entered[index]++;

        Open[_depth] = region;
        Started[_depth] = Enabled && seen >= warmup ? GC.GetAllocatedBytesForCurrentThread() : -1;
        Forgiving[_depth] = forgiving;
        _depth++;
    }

    /// <summary>
    /// Closes the innermost region. It throws <see cref="InvalidOperationException"/> when the region allocated more than the allowance, once per outermost scope.
    /// </summary>
    /// <param name="allowance">The number of bytes that the region can allocate.</param>
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
            if (_thrown)
            {
                return;
            }

            var index = IndexOf(Open[_depth]);
            if (Forgiving[_depth] && Forgiven[index] < ForgivenessBudget)
            {
                Forgiven[index]++;
                return;
            }

            _thrown = true;
            throw new InvalidOperationException(
                $"'{Open[_depth]}' allocated {allocated} bytes on a path allowed {allowance} (entry {Entered[index]}).");
        }
    }

    /// <summary>
    /// Stops the measurement of the open regions, for example around a consumer callback. Pauses can nest up to 8 deep.
    /// </summary>
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

    /// <summary>
    /// Restarts the measurement that the matching <see cref="Pause"/> stopped. The bytes allocated during the pause do not count.
    /// </summary>
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

    /// <summary>
    /// Closes all open scopes and pauses, and clears the entry and failure counts of every region.
    /// </summary>
    public static void Reset()
    {
        _depth = 0;
        _paused = 0;
        _thrown = false;
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
