using System.CommandLine;
using Basin.Cli;
using Basin.Render.Pixman;

using Basin.Diagnostics;

namespace TinyComp;

internal static class Program
{
    private static int Main(string[] args)
    {
        var cli = new BasinCommand(
            "Test implementation compositor.");
        var configPath = cli.Add(CommonOptions.Config("tinycomp"));
        var renderer = cli.Add(CommonOptions.Renderer(
            Basin.Renderers.RendererCatalog.Names, "vulkan"), report: false);
        var backend = cli.Add(CommonOptions.Backend([BackendKind.Nested, BackendKind.Drm], acceptsSocketFd: true), report: false);
        var outputs = cli.Add(CommonOptions.Outputs(), report: false);
        var scales = cli.Add(CommonOptions.Scales(), report: false);
        var fullRepaint = cli.Add(new Option<bool>("--full-repaint")
        {
            Description = "repaint every output whole, bypassing damage tracking",
        }, report: false);
        var damageTint = cli.Add(new Option<bool>("--damage-tint")
        {
            Description = "tint each repainted region, to see what damage tracking chose",
        }, report: false);
        var offload = cli.Add(new Option<bool>("--offload")
        {
            Description = "hand what it can to overlay planes; --offload false keeps everything composited",
            DefaultValueFactory = _ => true,
        }, report: false);

        var framesOption = cli.Add(CommonOptions.Frames(), report: false);
        var transport = cli.Add(CommonOptions.Transport());
        var channel = cli.Add(CommonOptions.WaypipeListen());

        var settings = new Config();
        var drm = false;
        string? fatal = null;

        cli.Prepare(result =>
        {
            cli.ConfigureLogging(result);
            settings = Config.Load(result.GetValue(configPath), BasinLog.For("TinyComp"), out fatal);
            if (fatal is not null)
            {
                return;
            }

            void Given(Option option, string key)
            {
                if (result.GetResult(option) is not (null or { Implicit: true }))
                {
                    settings.FromFlags.Add(key);
                }
            }

            T Layered<T>(Option<T> option, string key, T fromFile) =>
                BasinCommand.Effective(result, option, fromFile, settings.FromFile.Contains(key));

            settings.Renderer = Layered(renderer, "renderer", settings.Renderer)!;
            settings.Outputs = Layered(outputs, "outputs", settings.Outputs);
            settings.Scales = Layered(scales, "scale", settings.Scales)!;
            settings.Frames = Layered(framesOption, "frames", settings.Frames);
            settings.Offload = Layered(offload, "offload", settings.Offload);
            settings.FullRepaint = Layered(fullRepaint, "full_repaint", settings.FullRepaint);
            settings.DamageTint = Layered(damageTint, "damage_tint", settings.DamageTint);
            drm = result.GetValue(backend).Kind == BackendKind.Drm;

            Given(renderer, "renderer");
            Given(outputs, "outputs");
            Given(framesOption, "frames");
            Given(scales, "scale");
            Given(offload, "offload");
            Given(fullRepaint, "full_repaint");
            Given(damageTint, "damage_tint");
        });

        cli.AddReport(_ => BasinCommand.Report("renderer", settings.Renderer));
        cli.AddReport(result => BasinCommand.Report("backend", result.GetValue(backend)));
        cli.AddReport(_ => BasinCommand.Report("outputs", settings.Outputs));
        cli.AddReport(_ => BasinCommand.Report("scale", settings.Scales));
        cli.AddReport(_ => BasinCommand.Report("frames", settings.Frames));
        cli.AddReport(_ => BasinCommand.Report("full-repaint", settings.FullRepaint));
        cli.AddReport(_ => BasinCommand.Report("damage-tint", settings.DamageTint));
        cli.AddReport(_ => BasinCommand.Report("offload", settings.Offload));
        cli.AddReport(_ => BasinCommand.Report("transactions", settings.Transactions));
        cli.AddReport(_ => BasinCommand.Report("frame", settings.FrameStyle));
        cli.AddReport(_ => BasinCommand.Report("corner-radius", settings.CornerRadius));
        cli.AddReport(_ => BasinCommand.Report("color-source", settings.ColorSource));
        cli.AddReport(_ => BasinCommand.Report("icc", settings.IccProfile));
        cli.AddReport(_ => BasinCommand.Report("hdr", settings.Hdr));
        cli.AddReport(_ => BasinCommand.Report("night-light", settings.NightLight));
        cli.AddReport(_ => BasinCommand.Report("wobbly", settings.Wobbly));
        cli.AddReport(_ => BasinCommand.Report("open-animation", settings.OpenAnimation));
        cli.AddReport(_ => BasinCommand.Report("close-animation", settings.CloseAnimation));
        cli.AddReport(_ => BasinCommand.Report("switcher", settings.Switcher));
        cli.AddReport(_ => BasinCommand.Report("post", settings.Post));
        cli.AddReport(_ => BasinCommand.Report("bindings", settings.Bindings.Count));
        cli.AddReport(_ => BasinCommand.Report("rules", settings.Rules.Count));

        return cli.Run(args, result =>
        {
            var log = BasinLog.For("TinyComp");
            if (fatal is { } failure)
            {
                log.Error($"{failure}");
                return 1;
            }

            using var comp = new TinyComp(
                settings,
                drm,
                result.GetValue(backend).SocketFd,
                log,
                result.GetValue(transport).Kind == TransportKind.Managed,
                result.GetValue(channel),
                result.GetValue(configPath));
            var status = comp.Run();
            cli.ReportFrames(comp.Rendered);
            return status;
        });
    }
}
