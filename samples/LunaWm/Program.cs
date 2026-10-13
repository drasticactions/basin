using System.CommandLine;
using Basin.Cli;
using Basin.Diagnostics;
using Basin.Session;

namespace LunaWm;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "--pam-helper")
        {
            return PamHelper.Run(args[1], args[2]);
        }

        var cli = new BasinCommand("A webOS 3 TouchPad shell on basin: cards, the status bar, the launcher and Just Type.");
        var renderer = cli.Add(CommonOptions.Renderer(Basin.Renderers.RendererCatalog.Names, "vulkan"));
        var backend = cli.Add(CommonOptions.Backend(
            [BackendKind.Nested, BackendKind.Drm, BackendKind.Headless], acceptsSocketFd: true));
        var outputs = cli.Add(CommonOptions.Outputs());
        var scales = cli.Add(CommonOptions.Scales());
        var config = cli.Add(CommonOptions.Config("luna-wm"));
        var xwayland = cli.Add(new Option<bool?>("--xwayland")
        {
            Description = "host X11 clients through Xwayland: true or false, beating [shell] xwayland (default true)",
            HelpName = "BOOL",
            Arity = ArgumentArity.ZeroOrOne,
        });
        var command = cli.Add(CommonOptions.Command());
        var pamHelper = cli.Add(new Option<string?>("--pam-helper-command")
        {
            Description = "run `sh -c <command>` to check an unlock entry instead of the PAM helper; it reads one line and prints 1 or 0",
            HelpName = "COMMAND",
        });
        var greeter = cli.Add(new Option<bool>("--greeter")
        {
            Description = $"run as a greetd greeter with LunaWm's lock screen look, reading {LunaGreeterConfig.DefaultPath} and no user configuration",
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
            Description = "with --greeter, the state file (default $STATE_DIRECTORY/state.toml, else /var/lib/luna-wm-greeter/state.toml)",
            HelpName = "PATH",
        });
        var screenshot = cli.Add(CommonOptions.Screenshot());
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
                Basin.Greetd.GreeterEnvironment.PrepareDirectories(Path.GetDirectoryName(statePath ?? Basin.Greetd.GreeterMemory.DefaultPath(LunaGreeter.StateProgram)));
                Basin.Greetd.GreeterEnvironment.PrepareSessionBus();
                var greeterOptions = new LunaGreeterOptions
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
                    ConfigPath = string.Equals(greeterConfig, "false", StringComparison.OrdinalIgnoreCase) ? null : greeterConfig ?? LunaGreeterConfig.DefaultPath,
                    StatePath = statePath,
                    GreetdSocket = result.GetValue(greetdSock),
                    Demo = result.GetValue(demo),
                    Ipc = result.GetResult(ipcOption) is { Implicit: false } ? IpcCli.Read(cli, result) : Basin.Cli.IpcChoice.Off,
                };
                var greeterStatus = LunaGreeter.Run(greeterOptions, BasinLog.For("greeter"), out var greeterRendered);
                cli.ReportFrames(greeterRendered);
                return greeterStatus;
            }

            var log = BasinLog.For("LunaWm");
            var configValue = result.GetValue(config);
            var noConfig = string.Equals(configValue, "false", StringComparison.OrdinalIgnoreCase);
            var settings = LunaConfig.Load(noConfig ? null : configValue, noConfig, log, out var fatal);
            if (fatal is not null)
            {
                log.Error($"{fatal}");
                return 1;
            }

            var rendererName = Environment.GetEnvironmentVariable("BASIN_RENDERER") is { Length: > 0 }
                ? result.GetValue(renderer)!
                : BasinCommand.Effective(result, renderer, settings.Renderer ?? result.GetValue(renderer)!);
            var options = new LunaOptions
            {
                Ipc = IpcCli.Read(cli, result),
                Backend = chosen.Kind,
                Handoff = CommonOptions.ReadHandoff(result, handoff),
                Renderer = rendererName,
                Outputs = Math.Max(1, result.GetValue(outputs)),
                Scales = result.GetValue(scales)!,
                Frames = result.GetValue(frames),
                Screenshot = result.GetValue(screenshot),
                XWayland = result.GetValue(xwayland),
                Command = result.GetValue(command),
                PamHelperCommand = result.GetValue(pamHelper),
                SocketFd = chosen.SocketFd,
                ConfigPath = noConfig ? null : configValue,
                NoConfig = noConfig,
            };

            var status = LunaShell.Run(options, settings, log, out var rendered);
            cli.ReportFrames(rendered);
            return status;
        });
    }
}
