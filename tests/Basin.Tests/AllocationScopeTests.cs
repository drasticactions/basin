using Basin.Diagnostics;
using Xunit;

namespace Basin.Tests;

public sealed class AllocationScopeTests : IDisposable
{
    public AllocationScopeTests() => AllocationScope.Reset();

    public void Dispose() => AllocationScope.Reset();

    [Fact]
    public void A_region_that_allocates_nothing_passes()
    {
        LeakTracking.Require();

        AllocationScope.Begin();
        var sum = 0;
        for (var i = 0; i < 16; i++)
        {
            sum += i;
        }

        AllocationScope.End();
        Assert.Equal(120, sum);
    }

    [Fact]
    public void A_region_that_allocates_throws_and_names_itself()
    {
        LeakTracking.Require();

        var error = Assert.Throws<InvalidOperationException>(() =>
        {
            AllocationScope.Begin();
            GC.KeepAlive(new byte[4096]);
            AllocationScope.End();
        });

        Assert.Contains(nameof(A_region_that_allocates_throws_and_names_itself), error.Message, StringComparison.Ordinal);
        Assert.Contains("allocated", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_allowance_is_a_ceiling_rather_than_a_licence()
    {
        LeakTracking.Require();

        AllocationScope.Begin();
        GC.KeepAlive(new byte[64]);
        AllocationScope.End(allowance: 4096);

        Assert.Throws<InvalidOperationException>(() =>
        {
            AllocationScope.Begin();
            GC.KeepAlive(new byte[8192]);
            AllocationScope.End(allowance: 4096);
        });
    }

    [Fact]
    public void A_warm_up_leaves_the_first_entries_unmeasured()
    {
        LeakTracking.Require();

        for (var i = 0; i < 3; i++)
        {
            AllocationScope.Begin(warmup: 3);
            GC.KeepAlive(new byte[4096]);
            AllocationScope.End();
        }

        Assert.Throws<InvalidOperationException>(() =>
        {
            AllocationScope.Begin(warmup: 3);
            GC.KeepAlive(new byte[4096]);
            AllocationScope.End();
        });
    }

    [Fact]
    public void A_region_inside_another_is_measured_on_its_own()
    {
        LeakTracking.Require();

        var error = Assert.Throws<InvalidOperationException>(() =>
        {
            AllocationScope.Begin(region: "outer");
            AllocationScope.Begin(region: "inner");
            GC.KeepAlive(new byte[4096]);
            AllocationScope.End();
            AllocationScope.End(allowance: long.MaxValue);
        });

        Assert.Contains("inner", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_scope_that_never_began_cannot_end()
    {
        LeakTracking.Require();

        Assert.Throws<InvalidOperationException>(() => AllocationScope.End());
    }
}
