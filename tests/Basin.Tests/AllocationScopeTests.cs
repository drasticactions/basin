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
    public void A_paused_region_charges_nothing_to_the_open_scopes()
    {
        LeakTracking.Require();

        AllocationScope.Begin();
        AllocationScope.Pause();
        GC.KeepAlive(new byte[8192]);
        AllocationScope.Resume();
        AllocationScope.End();
    }

    [Fact]
    public void A_nested_pause_resumes_only_at_the_outermost_resume()
    {
        LeakTracking.Require();

        AllocationScope.Begin();
        AllocationScope.Pause();
        AllocationScope.Pause();
        GC.KeepAlive(new byte[4096]);
        AllocationScope.Resume();
        GC.KeepAlive(new byte[4096]);
        AllocationScope.Resume();
        AllocationScope.End();
    }

    [Fact]
    public void An_allocation_after_a_resume_still_throws()
    {
        LeakTracking.Require();

        Assert.Throws<InvalidOperationException>(() =>
        {
            AllocationScope.Begin();
            AllocationScope.Pause();
            AllocationScope.Resume();
            GC.KeepAlive(new byte[4096]);
            AllocationScope.End();
        });
    }

    [Fact]
    public void A_scope_begun_inside_a_pause_still_measures_itself()
    {
        LeakTracking.Require();

        Assert.Throws<InvalidOperationException>(() =>
        {
            AllocationScope.Pause();
            try
            {
                AllocationScope.Begin(region: "inside-a-pause");
                GC.KeepAlive(new byte[4096]);
                AllocationScope.End();
            }
            finally
            {
                AllocationScope.Resume();
            }
        });
    }

    [Fact]
    public void A_pause_inside_a_scope_begun_during_an_outer_pause_still_exempts()
    {
        LeakTracking.Require();

        AllocationScope.Pause();
        try
        {
            AllocationScope.Begin(region: "born-inside-a-pause");
            AllocationScope.Pause();
            GC.KeepAlive(new byte[8192]);
            AllocationScope.Resume();
            AllocationScope.End();
        }
        finally
        {
            AllocationScope.Resume();
        }
    }

    [Fact]
    public void A_forgiving_region_forgives_a_one_off_and_throws_on_a_recurrence()
    {
        LeakTracking.Require();

        for (var i = 0; i < 8; i++)
        {
            AllocationScope.Begin(region: "forgiving", forgiving: true);
            GC.KeepAlive(new byte[4096]);
            AllocationScope.End();
        }

        Assert.Throws<InvalidOperationException>(() =>
        {
            AllocationScope.Begin(region: "forgiving", forgiving: true);
            GC.KeepAlive(new byte[4096]);
            AllocationScope.End();
        });
    }

    [Fact]
    public void A_resume_that_never_paused_cannot_run()
    {
        LeakTracking.Require();

        Assert.Throws<InvalidOperationException>(() => AllocationScope.Resume());
    }

    [Fact]
    public void A_scope_that_never_began_cannot_end()
    {
        LeakTracking.Require();

        Assert.Throws<InvalidOperationException>(() => AllocationScope.End());
    }
}
