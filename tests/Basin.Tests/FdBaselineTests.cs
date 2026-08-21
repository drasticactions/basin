using Xunit;

namespace Basin.Tests;

public sealed class FdBaselineTests
{
    [Fact]
    public void Console_writes_inside_a_test_do_not_read_as_leaked_fds()
    {
        using var host = new CompositorTestHost();
        Console.Error.WriteLine("a test that reports something to standard error");
        Console.Out.WriteLine("a test that reports something to standard output");
    }
}
