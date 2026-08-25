using Basin.Diagnostics;
using Xunit;

namespace Basin.Tests;

public sealed class FdBaselineTests
{
    [Fact]
    public void Writes_to_the_standard_streams_inside_a_test_do_not_read_as_leaked_fds()
    {
        using var host = new CompositorTestHost();
        BasinLog.Error($"a test that reports something to standard error");
        BasinReport.Line($"a test that reports something to standard output");
    }
}
