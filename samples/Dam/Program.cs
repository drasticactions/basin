using System.CommandLine;
using Basin.Cli;

using Basin.Diagnostics;

namespace Dam;

internal static class Program
{
    private static int Main(string[] args)
    {
        var cli = new BasinCommand(
            ".NET Wayland kiosk.\n"
            + "\n"
            + "Usage: dam [OPTIONS] [--] [APPLICATION...]\n"
            + "\n"
            + " Use -- when you want to pass arguments to APPLICATION");

        foreach (var option in cli.Command.Options)
        {
            if (option is VersionOption)
            {
                option.Aliases.Add("-v");
            }
        }

        var decorationsOption = cli.Add(new Option<bool>("--no-decorations", "-d")
        {
            Description = "tell clients the server decorates their windows, so they should not draw their own",
        });
        var debugOption = cli.Add(new Option<bool>("-D")
        {
            Description = "equivalent to --log-level debug",
        });
        var outputModeOption = cli.Add(new Option<string>("--output-mode", "-m")
        {
            Description = "extend the layout across every connected output, or use only the last one",
            HelpName = "extend|last",
            DefaultValueFactory = _ => "extend",
        });
        outputModeOption.Validators.Add(result =>
        {
            var value = result.GetValueOrDefault<string?>();
            if (value is not ("extend" or "last"))
            {
                result.AddError($"unknown output mode '{value}' (expected extend, last)");
            }
        });
        var vtSwitchOption = cli.Add(new Option<bool>("--allow-vt-switch", "-s")
        {
            Description = "enable the VT switching keybindings",
        });
        var xwaylandOption = cli.Add(new Option<bool>("--no-xwayland", "-x")
        {
            Description = "do not start Xwayland",
        });
        var backendOption = cli.Add(CommonOptions.Backend(
            [BackendKind.Nested, BackendKind.Drm, BackendKind.Headless]));
        var rendererOption = cli.Add(CommonOptions.Renderer(
            Basin.Renderers.RendererCatalog.Names, "vulkan"));
        var framesOption = cli.Add(CommonOptions.Frames());
        var applicationArgument = new Argument<string[]>("application")
        {
            Description = "the primary client and its arguments",
            Arity = ArgumentArity.ZeroOrMore,
        };
        cli.Command.Arguments.Add(applicationArgument);

        return cli.Run(args, result =>
        {
            cli.ConfigureLogging(result, trace: result.GetValue(debugOption));
            var options = new DamOptions
            {
                Backend = result.GetValue(backendOption).Kind,
                Renderer = result.GetValue(rendererOption)!,
                ServerDecorations = result.GetValue(decorationsOption),
                LastOutputOnly = result.GetValue(outputModeOption) == "last",
                AllowVtSwitch = result.GetValue(vtSwitchOption),
                EnableXWayland = !result.GetValue(xwaylandOption),
                Frames = result.GetValue(framesOption),
                Application = result.GetValue(applicationArgument) ?? [],
            };
            var status = Dam.Run(options, BasinLog.For("Dam"), out var rendered);
            cli.ReportFrames(rendered);
            return status;
        });
    }
}
