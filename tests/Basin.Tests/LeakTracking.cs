using Basin.Diagnostics;
using Xunit;

namespace Basin.Tests;

internal static class LeakTracking
{
    private const string Reason =
        "lifetime tracking is compiled out of this build; rebuild with -p:BasinCounters=true to assert on leaks";

    public static void Require() => Assert.SkipWhen(!BasinCounters.Enabled, Reason);

    public static void Expect(int expected, int actual)
    {
        if (BasinCounters.Enabled)
        {
            Assert.Equal(expected, actual);
        }
    }
}
