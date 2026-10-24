using Basin.Cli;
using Xunit;

namespace Basin.Tests;

public sealed class CliBoundaryTests
{
    [Theory]
    [InlineData("No seat manager is available. Start seatd.", "no seat manager is available. Start seatd")]
    [InlineData("EpollEventLoop runs only on Linux.", "EpollEventLoop runs only on Linux")]
    [InlineData("ABM level is out of range.", "ABM level is out of range")]
    [InlineData("A\nB.", "a B")]
    public void A_message_takes_the_cli_form(string message, string expected) =>
        Assert.Equal(expected, CliMessage.Text(new InvalidOperationException(message).Message));

    [Fact]
    public void An_argument_exception_drops_its_parameter_suffix() =>
        Assert.Equal(
            "prog: the backend 'x' is unknown. Use drm, nested or headless",
            CliMessage.Line("prog", new ArgumentException("The backend 'x' is unknown. Use drm, nested or headless.", "name")));

    [Fact]
    public void A_body_that_throws_prints_one_line_and_exits_1()
    {
        var command = new BasinCommand("a command that fails") { ReportsOptions = false };
        var writer = new StringWriter();
        var previous = Console.Error;
        Console.SetError(writer);
        int status;
        try
        {
            status = command.Run([], _ => throw new InvalidOperationException("The service registry is frozen. Register services before Freeze."));
        }
        finally
        {
            Console.SetError(previous);
        }

        Assert.Equal(1, status);
        var line = Assert.Single(writer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal($"{command.Command.Name}: the service registry is frozen. Register services before Freeze", line);
    }
}
