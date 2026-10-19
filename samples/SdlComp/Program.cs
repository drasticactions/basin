using Basin.Cli;
using Basin.Diagnostics;
using Basin.Sdl;

namespace SdlComp;

internal static class Program
{
    private static int Main(string[] args)
    {
        var cli = new BasinCommand("A Basin compositor inside an SDL window.");
        var frames = cli.Add(CommonOptions.Frames());
        var width = cli.Add(CommonOptions.Width(1280));
        var height = cli.Add(CommonOptions.Height(800));
        var socket = cli.Add(CommonOptions.Socket());
        var renderer = cli.Add(CommonOptions.Renderer(SdlRenderDevices.Names, SdlRenderDevices.DefaultName));
        var screenshot = cli.Add(CommonOptions.Screenshot());
        var scale = cli.Add(CommonOptions.Scale());
        var outputs = cli.Add(CommonOptions.Outputs());
        var waypipeListen = cli.Add(CommonOptions.WaypipeListen());
        var hyperVVm = cli.Add(CommonOptions.HyperVVm());
        var windows = cli.Add(new System.CommandLine.Option<string>("--windows")
        {
            Description = "nested: one window holds the shell; toplevel: one SDL window per client toplevel",
            HelpName = "MODE",
            DefaultValueFactory = _ => "nested",
        });
        windows.AcceptOnlyFromAmong("nested", "toplevel");
        var grabKeyboard = cli.Add(new System.CommandLine.Option<bool>("--grab-keyboard")
        {
            Description = "take every key while the window has focus, the host's own shortcuts included",
        });
        _ = IpcCli.AddOption(cli);

        return cli.Run(args, result =>
        {
            cli.ConfigureLogging(result);
            var log = BasinLog.For("SdlComp");
            var options = new SdlCompOptions
            {
                Frames = result.GetValue(frames),
                Width = result.GetValue(width),
                Height = result.GetValue(height),
                Socket = result.GetValue(socket),
                Renderer = result.GetValue(renderer)!,
                Screenshot = result.GetValue(screenshot),
                Ipc = IpcCli.Read(cli, result),
                Scale = result.GetValue(scale),
                GrabKeyboard = result.GetValue(grabKeyboard),
                Outputs = Math.Max(1, result.GetValue(outputs)),
                WaypipeListen = result.GetValue(waypipeListen),
                Toplevels = result.GetValue(windows) == "toplevel",
                HyperVVm = result.GetResult(hyperVVm) is { Implicit: false } ? result.GetValue(hyperVVm) : null,
            };

            try
            {
                using var comp = new SdlComp(options, log);
                var status = comp.Run();
                cli.ReportFrames(comp.Frames);
                BasinReport.Line(CompositorLines.Frames(comp.Frames));
                return status;
            }
            catch (Exception error) when (error is InvalidOperationException or DllNotFoundException)
            {
                log.Error($"{error.Message}");
                return 1;
            }
        });
    }
}
