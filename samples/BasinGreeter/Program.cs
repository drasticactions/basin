using System.CommandLine;
using Basin.Cli;
using Basin.Diagnostics;
using Basin.Greetd;

namespace BasinGreeter;

internal static class Program
{
    private static int Main(string[] args)
    {
        var cli = new BasinCommand("a greetd greeter: a compositor that draws its own login screen and exits when greetd starts the session.");
        var renderer = cli.Add(CommonOptions.Renderer(Basin.Renderers.RendererCatalog.Names, "vulkan"));
        var backend = cli.Add(CommonOptions.Backend([BackendKind.Nested, BackendKind.Drm, BackendKind.Headless], acceptsSocketFd: true));
        var outputs = cli.Add(CommonOptions.Outputs());
        var scales = cli.Add(CommonOptions.Scales());
        var config = cli.Add(new Option<string?>("--config")
        {
            Description = $"read this configuration, or false to read none (default {GreeterConfig.DefaultPath})",
            HelpName = "PATH",
        });
        var state = cli.Add(new Option<string?>("--state")
        {
            Description = "the state file (default $STATE_DIRECTORY/state.toml, else /var/lib/basin-greeter/state.toml)",
            HelpName = "PATH",
        });
        var greetd = cli.Add(new Option<string?>("--greetd-sock")
        {
            Description = "the greetd socket, instead of GREETD_SOCK",
            HelpName = "PATH",
        });
        var demo = cli.Add(new Option<bool>("--demo")
        {
            Description = "serve a fake greetd in the process: any user, password demo. Refused when GREETD_SOCK is set",
        });
        var ipc = cli.Add(new Option<string?>("--ipc")
        {
            Description = "bind the control socket at this path; a login screen binds none by default",
            HelpName = "PATH",
        });
        var screenshot = cli.Add(CommonOptions.Screenshot());
        var frames = cli.Add(CommonOptions.Frames());
        var handoff = cli.Add(CommonOptions.Handoff());

        return cli.Run(args, result =>
        {
            cli.ConfigureLogging(result);
            GreeterEnvironment.PrepareDirectories(Path.GetDirectoryName(result.GetValue(state) ?? GreeterMemory.DefaultPath("basin-greeter")));
            GreeterEnvironment.PrepareSessionBus();
            var chosen = result.GetValue(backend);
            var configValue = result.GetValue(config);
            var ipcPath = result.GetValue(ipc);
            var options = new GreeterOptions
            {
                Backend = chosen.Kind,
                Handoff = CommonOptions.ReadHandoff(result, handoff),
                SocketFd = chosen.SocketFd,
                Renderer = result.GetValue(renderer)!,
                RendererGiven = result.GetResult(renderer) is { Implicit: false },
                Outputs = result.GetValue(outputs),
                Scales = result.GetValue(scales)!,
                Frames = result.GetValue(frames),
                Screenshot = result.GetValue(screenshot),
                ConfigPath = string.Equals(configValue, "false", StringComparison.OrdinalIgnoreCase) ? null : configValue ?? GreeterConfig.DefaultPath,
                StatePath = result.GetValue(state),
                GreetdSocket = result.GetValue(greetd),
                Demo = result.GetValue(demo),
                Ipc = ipcPath is { Length: > 0 } && ipcPath != "false" ? new IpcChoice(true, Path.GetFullPath(ipcPath)) : IpcChoice.Off,
            };

            var status = Greeter.Run(options, BasinLog.For("greeter"), out var rendered);
            cli.ReportFrames(rendered);
            return status;
        });
    }
}
