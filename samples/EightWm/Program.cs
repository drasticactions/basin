using System.CommandLine;
using Basin.Cli;

using Basin.Diagnostics;

namespace EightWm;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "--pam-helper")
        {
            return Basin.Session.PamHelper.Run(args[1], args[2]);
        }

        var cli = new BasinCommand("A Windows 8 tablet shell on basin.");
        var renderer = cli.Add(CommonOptions.Renderer(Basin.Renderers.RendererCatalog.Names, "vulkan"));
        var backend = cli.Add(CommonOptions.Backend(
            [BackendKind.Nested, BackendKind.Drm, BackendKind.Headless], acceptsSocketFd: true));
        var outputs = cli.Add(CommonOptions.Outputs());
        var scales = cli.Add(CommonOptions.Scales());
        var config = cli.Add(new Option<string?>("--config")
        {
            Description = "read this config file, or 'false' to read none",
            HelpName = "PATH",
        });
        var hotCorners = cli.Add(new Option<string>("--hot-corners")
        {
            Description = "corner triggers for a mouse: on | off",
            HelpName = "STATE",
            DefaultValueFactory = _ => "on",
        }.AcceptOnlyFromAmong("on", "off"));
        var animations = cli.Add(new Option<string>("--animations")
        {
            Description = "the whole animation catalog: on | off",
            HelpName = "STATE",
            DefaultValueFactory = _ => "on",
        }.AcceptOnlyFromAmong("on", "off"));
        var startOutput = cli.Add(new Option<int>("--start-output")
        {
            Description = "which output shows Start at startup",
            HelpName = "N",
            DefaultValueFactory = _ => 0,
        });
        var minWidth = cli.Add(new Option<int>("--min-width")
        {
            Description = "narrowest a cell may become, in logical pixels",
            HelpName = "N",
            DefaultValueFactory = _ => 500,
        });
        var edgeBand = cli.Add(new Option<int>("--edge-band")
        {
            Description = "how wide the edge bands are, in logical pixels",
            HelpName = "N",
            DefaultValueFactory = _ => 20,
        });
        var xwayland = cli.Add(new Option<string>("--xwayland")
        {
            Description = "host X11 clients through Xwayland: on | off",
            HelpName = "STATE",
            DefaultValueFactory = _ => "on",
        }.AcceptOnlyFromAmong("on", "off"));
        var theme = cli.Add(new Option<string>("--theme")
        {
            Description = "the chrome theme: dark | light",
            HelpName = "THEME",
            DefaultValueFactory = _ => "dark",
        }.AcceptOnlyFromAmong("dark", "light"));
        var accent = cli.Add(new Option<string?>("--accent")
        {
            Description = "the accent color, as #rrggbb",
            HelpName = "COLOR",
        });
        var pamHelper = cli.Add(new Option<string?>("--pam-helper-command")
        {
            Description = "run `sh -c <command>` to check an unlock entry instead of the PAM helper. It reads one line and prints 1 or 0",
            HelpName = "COMMAND",
        });
        var greeter = cli.Add(new Option<bool>("--greeter")
        {
            Description = $"run as a greetd greeter with the Windows 8 lock screen and sign-in screen, reading {EightGreeterConfig.DefaultPath} and no user configuration",
        });
        var greetdSock = cli.Add(new Option<string?>("--greetd-sock")
        {
            Description = "with --greeter, the greetd socket instead of GREETD_SOCK",
            HelpName = "PATH",
        });
        var demo = cli.Add(new Option<bool>("--demo")
        {
            Description = "with --greeter, serve a fake greetd in the process: any user, password demo. Refused when GREETD_SOCK is set",
        });
        var state = cli.Add(new Option<string?>("--state")
        {
            Description = "with --greeter, the state file (default $STATE_DIRECTORY/state.toml, else /var/lib/eight-wm-greeter/state.toml)",
            HelpName = "PATH",
        });
        var screenshot = cli.Add(CommonOptions.Screenshot());
        var client = cli.Add(CommonOptions.Client());
        var frames = cli.Add(CommonOptions.Frames());
        var handoff = cli.Add(CommonOptions.Handoff());
        var ipcOption = IpcCli.AddOption(cli);

        return cli.Run(args, result =>
        {
            var chosen = result.GetValue(backend);
            cli.ConfigureLogging(result);
            if (result.GetValue(greeter))
            {
                var greeterConfig = result.GetValue(config);
                var statePath = result.GetValue(state);
                Basin.Greetd.GreeterEnvironment.PrepareDirectories(Path.GetDirectoryName(statePath ?? Basin.Greetd.GreeterMemory.DefaultPath(EightGreeter.StateProgram)));
                Basin.Greetd.GreeterEnvironment.PrepareSessionBus();
                var greeterOptions = new EightGreeterOptions
                {
                    Backend = chosen.Kind,
                    Handoff = CommonOptions.ReadHandoff(result, handoff),
                    SocketFd = chosen.SocketFd,
                    Renderer = result.GetValue(renderer)!,
                    RendererGiven = result.GetResult(renderer) is { Implicit: false } || Environment.GetEnvironmentVariable("BASIN_RENDERER") is { Length: > 0 },
                    Outputs = Math.Max(1, result.GetValue(outputs)),
                    Scales = result.GetValue(scales)!,
                    Frames = result.GetValue(frames),
                    Screenshot = result.GetValue(screenshot),
                    ConfigPath = string.Equals(greeterConfig, "false", StringComparison.OrdinalIgnoreCase) ? null : greeterConfig ?? EightGreeterConfig.DefaultPath,
                    StatePath = statePath,
                    GreetdSocket = result.GetValue(greetdSock),
                    Demo = result.GetValue(demo),
                    Ipc = result.GetResult(ipcOption) is { Implicit: false } ? IpcCli.Read(cli, result) : IpcChoice.Off,
                };
                var greeterStatus = EightGreeter.Run(greeterOptions, BasinLog.For("greeter"), out var greeterRendered);
                cli.ReportFrames(greeterRendered);
                return greeterStatus;
            }

            var explicitly = new HashSet<string>(StringComparer.Ordinal);
            void Note<T>(string name, Option<T> option)
            {
                if (result.GetResult(option) is { Implicit: false })
                {
                    explicitly.Add(name);
                }
            }

            Note("hot_corners", hotCorners);
            Note("animations", animations);
            Note("edge_band", edgeBand);
            Note("min_width", minWidth);
            Note("start_output", startOutput);
            Note("theme", theme);
            Note("accent", accent);

            var options = new ShellOptions
            {
                Ipc = IpcCli.Read(cli, result),
                Backend = chosen.Kind,
                Handoff = CommonOptions.ReadHandoff(result, handoff),
                Renderer = result.GetValue(renderer)!,
                Outputs = result.GetValue(outputs),
                Scales = result.GetValue(scales)!,
                Frames = result.GetValue(frames),
                Screenshot = result.GetValue(screenshot),
                Client = result.GetValue(client),
                ConfigPath = result.GetValue(config),
                HotCorners = result.GetValue(hotCorners) == "on",
                Animations = result.GetValue(animations) == "on",
                StartOutput = result.GetValue(startOutput),
                MinWidth = result.GetValue(minWidth),
                EdgeBand = result.GetValue(edgeBand),
                XWayland = result.GetValue(xwayland) == "on",
                Dark = result.GetValue(theme) != "light",
                Accent = Basin.Config.TomlColor.Argb(result.GetValue(accent), Shell.DefaultAccent) | 0xff000000,
                SocketFd = chosen.SocketFd,
                Explicit = explicitly,
                PamHelperCommand = result.GetValue(pamHelper),
            };

            var status = Shell.Run(options, BasinLog.For("EightWm"), out var rendered);
            cli.ReportFrames(rendered);
            return status;
        });
    }
}
