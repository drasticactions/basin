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
        var scalerOption = cli.Add(new Option<string?>("--scaler")
        {
            Description = "beyond cage: scale a primary window that keeps another size onto the output",
            HelpName = "fit|integer|fill|stretch",
        });
        scalerOption.AcceptOnlyFromAmong("fit", "integer", "fill", "stretch");
        var filterOption = cli.Add(new Option<string?>("--filter")
        {
            Description = "beyond cage: how a scaled window is sampled; pixel is vulkan and gl only",
            HelpName = "linear|nearest|pixel",
        });
        filterOption.AcceptOnlyFromAmong("linear", "nearest", "pixel");
        var frameLimitOption = cli.Add(new Option<int>("--frame-limit")
        {
            Description = "beyond cage: tell the application to draw at most N frames a second, 0 is off",
            HelpName = "N",
        });
        var contentTypeOption = cli.Add(new Option<string?>("--content-type")
        {
            Description = "beyond cage: the content type every output reports to the display",
            HelpName = "game",
        });
        contentTypeOption.AcceptOnlyFromAmong("game");
        var panelsOption = cli.Add(new Option<string?>("--panels")
        {
            Description = "beyond cage: a panel table in place of $XDG_CONFIG_HOME/basin/panels.toml",
            HelpName = "PATH",
        });
        var itmOption = cli.Add(new Option<bool>("--inverse-tone-mapping")
        {
            Description = "beyond cage: drive HDR on a PQ panel and expand SDR content onto it",
        });
        var vrrOption = cli.Add(new Option<string>("--vrr-pacing")
        {
            Description = "beyond cage: when a VRR output repaints on arrival",
            HelpName = "fullscreen|always|never",
            DefaultValueFactory = _ => "fullscreen",
        });
        vrrOption.AcceptOnlyFromAmong("fullscreen", "always", "never");
        var inspectorOption = cli.Add(new Option<bool>("--inspector")
        {
            Description = "beyond cage: open the inspector overlay at start; Super+F12 and the inspector/* methods drive it",
        });
        var subreaperOption = cli.Add(CommonOptions.Subreaper(defaultOn: true));
        subreaperOption.Description = "run the application under a reaper that adopts every orphan and ends the whole tree when the application exits";
        var backendOption = cli.Add(CommonOptions.Backend(
            [BackendKind.Nested, BackendKind.Drm, BackendKind.Headless]));
        var rendererOption = cli.Add(CommonOptions.Renderer(
            Basin.Renderers.RendererCatalog.Names, "vulkan"));
        var framesOption = cli.Add(CommonOptions.Frames());
        var handoffOption = cli.Add(CommonOptions.Handoff());
        _ = IpcCli.AddOption(cli);
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
                Handoff = CommonOptions.ReadHandoff(result, handoffOption).Resolve(Basin.HandoffMode.Auto),
                ServerDecorations = result.GetValue(decorationsOption),
                LastOutputOnly = result.GetValue(outputModeOption) == "last",
                AllowVtSwitch = result.GetValue(vtSwitchOption),
                EnableXWayland = !result.GetValue(xwaylandOption),
                Frames = result.GetValue(framesOption),
                Application = result.GetValue(applicationArgument) ?? [],
                Ipc = IpcCli.Read(cli, result),
                Scaler = result.GetValue(scalerOption) switch
                {
                    "fit" => Basin.Scene.SceneFitMode.Fit,
                    "integer" => Basin.Scene.SceneFitMode.Integer,
                    "fill" => Basin.Scene.SceneFitMode.Fill,
                    "stretch" => Basin.Scene.SceneFitMode.Stretch,
                    _ => null,
                },
                Filter = result.GetValue(filterOption) switch
                {
                    "linear" => Basin.TextureFilter.Linear,
                    "nearest" => Basin.TextureFilter.Nearest,
                    "pixel" => Basin.TextureFilter.Pixel,
                    _ => null,
                },
                FrameLimit = Math.Max(0, result.GetValue(frameLimitOption)),
                GameContent = result.GetValue(contentTypeOption) == "game",
                Panels = result.GetValue(panelsOption),
                InverseToneMapping = result.GetValue(itmOption),
                VrrPacing = result.GetValue(vrrOption) switch
                {
                    "always" => Basin.Host.VrrPacingPolicy.Always,
                    "never" => Basin.Host.VrrPacingPolicy.Never,
                    _ => Basin.Host.VrrPacingPolicy.Fullscreen,
                },
                Subreaper = result.GetValue(subreaperOption),
                Inspector = result.GetValue(inspectorOption),
            };
            var status = Dam.Run(options, BasinLog.For("Dam"), out var rendered);
            cli.ReportFrames(rendered);
            return status;
        });
    }
}
