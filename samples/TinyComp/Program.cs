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
        var backend = cli.Add(CommonOptions.Backend([BackendKind.Nested, BackendKind.Drm, BackendKind.Headless], acceptsSocketFd: true), report: false);
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

        var quillDemo = cli.Add(new Option<bool>("--quill-demo")
        {
            Description = "draw one Prowl.Quill chrome mock beside the Skia chrome, on the gl renderer only",
        }, report: false);

        var commandOption = cli.Add(new Option<string?>("--command", "-c")
        {
            Description = "run `sh -c <command>` on startup instead of the init executable",
            HelpName = "CMD",
        });

        var framesOption = cli.Add(CommonOptions.Frames(), report: false);
        var transport = cli.Add(CommonOptions.Transport());
        var channel = cli.Add(CommonOptions.WaypipeListen());
        _ = IpcCli.AddOption(cli);

        var settings = new Config();
        string? fatal = null;

        cli.Prepare(result =>
        {
            cli.ConfigureLogging(result);
            settings = Config.Load(result.GetValue(configPath), BasinLog.For("TinyComp"), out fatal);
            if (fatal is null && settings.Shaders.Count > 0 &&
                !Basin.Rashader.RashaderLibrary.IsAvailable(out var shaderWhy))
            {
                fatal = $"[effects] shader: {shaderWhy}";
            }

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
            settings.QuillDemo = result.GetValue(quillDemo);

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
        cli.AddReport(_ => BasinCommand.Report("quill-demo", settings.QuillDemo));
        cli.AddReport(_ => BasinCommand.Report("offload", settings.Offload));
        cli.AddReport(_ => BasinCommand.Report("transactions", settings.Transactions));
        cli.AddReport(_ => BasinCommand.Report(
            "frame",
            settings.FrameStyle == FrameStyle.Metacity ? $"metacity:{settings.MetacityTheme}" : settings.FrameStyle));
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
        cli.AddReport(_ => BasinCommand.Report("canvas", settings.Canvas.Enabled));
        cli.AddReport(_ => BasinCommand.Report("canvas-zone", settings.Canvas.ZoneFraction));
        cli.AddReport(_ => BasinCommand.Report("canvas-extension", settings.Canvas.ExtensionFraction));
        cli.AddReport(_ => BasinCommand.Report("canvas-edge-scale", settings.Canvas.EdgeScaleValue));
        cli.AddReport(_ => BasinCommand.Report("canvas-slope", settings.Canvas.SlopeValue));
        cli.AddReport(_ => BasinCommand.Report("canvas-mesh-cell", settings.Canvas.MeshCellSize));
        cli.AddReport(_ => BasinCommand.Report("canvas-grid", settings.Canvas.GridMode));
        cli.AddReport(_ => BasinCommand.Report("canvas-grid-cell", settings.Canvas.GridCellSize));
        cli.AddReport(_ => BasinCommand.Report("canvas-grid-color", $"#{settings.Canvas.GridRgba:x8}"));
        cli.AddReport(_ => BasinCommand.Report("canvas-animation-ms", settings.Canvas.AnimationMillis));
        cli.AddReport(_ => BasinCommand.Report("canvas-sides", settings.Canvas.SideNames));
        cli.AddReport(_ => BasinCommand.Report("canvas-corner", settings.Canvas.CornerName));
        cli.AddReport(_ => BasinCommand.Report("canvas-corner-radius", settings.Canvas.CornerRadiusValue));
        cli.AddReport(_ => BasinCommand.Report("canvas-window", settings.Canvas.WindowName));
        cli.AddReport(_ => BasinCommand.Report("canvas-min-scale", settings.Canvas.MinScaleValue));
        cli.AddReport(_ => BasinCommand.Report("canvas-scale-reach", settings.Canvas.ScaleReachValue));
        cli.AddReport(_ => BasinCommand.Report("canvas-shelf", settings.Canvas.ShelfFraction));
        cli.AddReport(_ => BasinCommand.Report("canvas-shelf-scale", settings.Canvas.ShelfScaleValues.Names));
        cli.AddReport(_ => BasinCommand.Report("canvas-shelf-min-scale", settings.Canvas.ShelfMinScaleValue));
        cli.AddReport(_ => BasinCommand.Report("canvas-shelf-step", settings.Canvas.ShelfStepValue));
        cli.AddReport(_ => BasinCommand.Report("canvas-shelf-shape", settings.Canvas.ShapeName));
        cli.AddReport(_ => BasinCommand.Report("canvas-slope-window", settings.Canvas.OnSlopeName));
        cli.AddReport(_ => BasinCommand.Report("canvas-drag", settings.Canvas.DragName));
        cli.AddReport(_ => BasinCommand.Report("overview", settings.Overview.Enabled));
        cli.AddReport(_ => BasinCommand.Report("overview-scale", settings.Overview.ScaleValue));
        cli.AddReport(_ => BasinCommand.Report("overview-threshold-in", settings.Overview.ThresholdInValue));
        cli.AddReport(_ => BasinCommand.Report("overview-threshold-out", settings.Overview.ThresholdOutValue));
        cli.AddReport(_ => BasinCommand.Report("overview-hot-corner", settings.Overview.HotCornerName));
        cli.AddReport(_ => BasinCommand.Report("overview-wall", settings.Overview.WallName));
        cli.AddReport(_ => BasinCommand.Report("overview-wall-width", settings.Overview.WallWidthValue));
        cli.AddReport(_ => BasinCommand.Report("overview-wall-texture", settings.Overview.WallTextureValue));
        cli.AddReport(_ => BasinCommand.Report("overview-shelf-texture", settings.Overview.ShelfTextureValue));
        cli.AddReport(_ => BasinCommand.Report("overview-texture-scale", settings.Overview.TextureScaleValue));
        cli.AddReport(_ => BasinCommand.Report("overview-shelf-color", $"#{settings.Overview.ShelfRgba:x8}"));
        cli.AddReport(_ => BasinCommand.Report("overview-texture-grid", settings.Overview.TextureGridValue));
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

            var init = result.GetValue(commandOption);
            if (init is null && result.GetValue(configPath) is null
                && !Basin.Host.InitProcess.TryFind(["tinycomp"], log, out init))
            {
                return 1;
            }

            int status;
            long rendered;
            using (var comp = new TinyComp(
                settings,
                result.GetValue(backend).Kind,
                result.GetValue(backend).SocketFd,
                log,
                result.GetValue(transport).Kind == TransportKind.Managed,
                result.GetValue(channel),
                result.GetValue(configPath),
                IpcCli.Read(cli, result),
                init))
            {
                status = comp.Run();
                rendered = comp.Rendered;
            }

            BasinReport.Line(CompositorLines.Frames(rendered));
            if (BasinCounters.Enabled && (BasinCounters.LiveObjects != 0 || BasinCounters.PendingFrees != 0))
            {
                log.Error($"{BasinCounters.CensusReport()}");
            }

            cli.ReportFrames(rendered);
            return status;
        });
    }
}
