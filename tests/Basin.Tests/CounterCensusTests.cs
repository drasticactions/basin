using System.Runtime.CompilerServices;
using Basin.Diagnostics;
using Xunit;

namespace Basin.Tests;

public sealed class CounterCensusTests : IDisposable
{
    public CounterCensusTests() => BasinCounters.Reset();

    public void Dispose()
    {
        BasinCounters.CaptureOrigins = false;
        BasinCounters.Reset();
    }

    private static int Line([CallerLineNumber] int line = 0) => line;

    [Fact]
    public void A_balanced_site_is_not_reported()
    {
        LeakTracking.Require();

        BasinCounters.Track();
        BasinCounters.Untrack();

        var report = BasinCounters.CensusReport();
        Assert.Contains("0 live objects", report, StringComparison.Ordinal);
        Assert.DoesNotContain("CounterCensusTests.cs", report, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unbalanced_site_is_reported_by_file_and_line()
    {
        LeakTracking.Require();

        var tracked = Line(); BasinCounters.Track(2);
        var untracked = Line(); BasinCounters.Untrack();

        var report = BasinCounters.CensusReport();
        Assert.Contains("1 live objects", report, StringComparison.Ordinal);
        Assert.Contains("CounterCensusTests.cs", report, StringComparison.Ordinal);
        Assert.Contains($"+2 at :{tracked}", report, StringComparison.Ordinal);
        Assert.Contains($"-1 at :{untracked}", report, StringComparison.Ordinal);

        BasinCounters.Untrack();
    }

    [Fact]
    public void Origins_name_the_stack_that_created_what_is_left()
    {
        LeakTracking.Require();

        BasinCounters.CaptureOrigins = true;
        BasinCounters.Track();

        Assert.Contains(
            nameof(Origins_name_the_stack_that_created_what_is_left),
            BasinCounters.CensusReport(),
            StringComparison.Ordinal);

        BasinCounters.Untrack();
        Assert.DoesNotContain(
            nameof(Origins_name_the_stack_that_created_what_is_left),
            BasinCounters.CensusReport(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Reset_clears_the_census()
    {
        LeakTracking.Require();

        BasinCounters.Track();
        BasinCounters.Reset();

        Assert.DoesNotContain("CounterCensusTests.cs", BasinCounters.CensusReport(), StringComparison.Ordinal);
    }
}
