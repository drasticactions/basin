using System.CommandLine;
using Basin.Cli;

namespace BasinCtl;

internal static class Program
{
    private static int Main(string[] args)
    {
        var cli = new BasinCommand(
            "Control a running basin compositor through its control socket.\n"
            + "\n"
            + "  basinctl windows | outputs | workspaces | methods [--detail] | events | version | describe\n"
            + "  basinctl activate ID | close ID | move ID X Y | resize ID W H | wait APP_ID | density ID native|low\n"
            + "  basinctl stats ID | frame-limit ID FPS\n"
            + "  basinctl shot OUTPUT FILE.png | chord CHORD | key CODE | text TEXT... | spawn [--x11-density low] ARGV...\n"
            + "  basinctl subscribe EVENT... | clipboard [primary] | raw JSON | quit\n"
            + "  basinctl devices | device ID | device ID key=value... | device ID reset [KEY...]\n"
            + "  basinctl METHOD [key=value ...]")
        {
            ReportsOptions = false,
        };
        var socket = cli.Add(new Option<string?>("--socket")
        {
            Description = "the control socket, otherwise $BASIN_SOCKET or the one basin-*.sock in $XDG_RUNTIME_DIR",
            HelpName = "PATH",
        });
        var json = cli.Add(new Option<bool>("--json")
        {
            Description = "print the raw result instead of a table",
        });
        var fd = cli.Add(new Option<bool>("--fd")
        {
            Description = "take a screenshot as raw pixels over a descriptor and encode it here",
        });
        var maxDimension = cli.Add(new Option<int?>("--max-dimension")
        {
            Description = "with shot, scale the image down until its longer side is at most N pixels",
            HelpName = "N",
        });
        var hdr = cli.Add(new Option<bool>("--hdr")
        {
            Description = "with shot, write a 16-bit PNG in the output's own encoding, PQ on an HDR output, tagged with cICP",
        });
        var detail = cli.Add(new Option<bool>("--detail")
        {
            Description = "with methods, print each method's traits and description",
        });
        var x11Density = cli.Add(new Option<string?>("--x11-density")
        {
            Description = "with spawn, the X server for an X11 program: native or low",
            HelpName = "native|low",
        });
        var words = new Argument<string[]>("command")
        {
            Description = "the command and its arguments",
            Arity = ArgumentArity.ZeroOrMore,
        };
        cli.Command.Arguments.Add(words);

        return cli.Run(args, result =>
        {
            cli.ConfigureLogging(result);
            var command = result.GetValue(words) ?? [];
            if (command.Length == 0)
            {
                return cli.Usage(result);
            }

            return new Controller(result.GetValue(socket), result.GetValue(json), result.GetValue(fd), result.GetValue(detail), result.GetValue(maxDimension), result.GetValue(x11Density), result.GetValue(hdr))
                .RunAsync(command).GetAwaiter().GetResult();
        });
    }
}
