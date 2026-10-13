using System.Text;
using Basin.Session;
using Xunit;

namespace Basin.Tests;

public sealed class PamCheckTests
{
    private const string Stub = "read p; if [ \"$p\" = right ]; then echo 1; else echo 0; fi";

    private static bool? Check(CompositorTestHost host, string password)
    {
        bool? result = null;
        using var check = PamCheck.Start(host.Loop, ["sh", "-c", Stub], Encoding.UTF8.GetBytes(password), success => result = success, out var error);
        Assert.Null(error);
        Assert.NotNull(check);
        var deadline = Environment.TickCount64 + 10_000;
        while (result is null && Environment.TickCount64 < deadline)
        {
            host.Loop.Dispatch(10);
        }

        return result;
    }

    [Fact]
    public void The_helper_reads_one_line_and_answers_one_byte()
    {
        using var host = new CompositorTestHost();
        Assert.True(Check(host, "right"));
        Assert.False(Check(host, "wrong"));
    }

    [Fact]
    public void A_helper_that_cannot_start_is_an_error_and_no_check()
    {
        using var host = new CompositorTestHost();
        var check = PamCheck.Start(host.Loop, ["/nonexistent/pam-helper"], "x"u8.ToArray(), _ => { }, out var error);
        Assert.Null(check);
        Assert.NotNull(error);
        Assert.StartsWith("/nonexistent/pam-helper:", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_helper_that_exits_without_an_answer_is_a_failure()
    {
        using var host = new CompositorTestHost();
        bool? result = null;
        using var check = PamCheck.Start(host.Loop, ["sh", "-c", "exit 0"], "x"u8.ToArray(), success => result = success, out _);
        var deadline = Environment.TickCount64 + 10_000;
        while (result is null && Environment.TickCount64 < deadline)
        {
            host.Loop.Dispatch(10);
        }

        Assert.False(result);
    }
}
