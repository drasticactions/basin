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
        var renderer = cli.Add(CommonOptions.Renderer(
            Basin.Renderers.RendererCatalog.Names, "vulkan"));
        var backend = cli.Add(CommonOptions.Backend([BackendKind.Nested, BackendKind.Drm], acceptsSocketFd: true));
        var outputs = cli.Add(CommonOptions.Outputs());
        var scales = cli.Add(CommonOptions.Scales());
        var frame = cli.Add(new Option<string>("--frame")
        {
            Description = "window decoration: beos | flat | none",
            HelpName = "STYLE",
            DefaultValueFactory = _ => "beos",
        }.AcceptOnlyFromAmong("beos", "flat", "none"));
        var fullRepaint = cli.Add(new Option<bool>("--full-repaint")
        {
            Description = "repaint every output whole, bypassing damage tracking",
        });
        var damageTint = cli.Add(new Option<bool>("--damage-tint")
        {
            Description = "tint each repainted region, to see what damage tracking chose",
        });
        var transactions = cli.Add(new Option<bool>("--transactions")
        {
            Description = "apply resizes as transactions, so a resize lands on one frame",
            DefaultValueFactory = _ => true,
        });
        var offload = cli.Add(new Option<bool>("--offload")
        {
            Description = "hand what it can to overlay planes; --offload false keeps everything composited",
            DefaultValueFactory = _ => true,
        });
        var hdr = cli.Add(new Option<bool>("--hdr")
        {
            Description = "drive the outputs in HDR where they support it",
        });
        var colorSource = cli.Add(new Option<string>("--color-source")
        {
            Description = "what the outputs are described as: edid | srgb | icc:PATH",
            HelpName = "NAME",
            DefaultValueFactory = _ => "edid",
        });
        colorSource.Validators.Add(result =>
        {
            var value = result.GetValueOrDefault<string>();
            if (value is not ("edid" or "srgb") && !value.StartsWith("icc:", StringComparison.Ordinal))
            {
                result.AddError($"--color-source must be edid, srgb or icc:PATH, not '{value}'");
            }
        });
        var nightLight = cli.Add(new Option<double?>("--night-light")
        {
            Description = "warm the outputs to this color temperature in kelvin",
            HelpName = "K",
        });
        var wobbly = cli.Add(new Option<bool>("--wobbly")
        {
            Description = "wobble windows on interactive move",
        });
        var openAnimation = cli.Add(new Option<string?>("--open-animation")
        {
            Description = "animate window map: fade | zoom",
            HelpName = "KIND",
        }.AcceptOnlyFromAmong("fade", "zoom"));
        var closeAnimation = cli.Add(new Option<string?>("--close-animation")
        {
            Description = "animate window close from a snapshot: fade | zoom | fire | fire-gpu",
            HelpName = "KIND",
        }.AcceptOnlyFromAmong("fade", "zoom", "fire", "fire-gpu"));
        var switcher = cli.Add(new Option<bool>("--switcher")
        {
            Description = "flip through windows as 3d cards on alt+tab",
        });
        var post = cli.Add(new Option<string?>("--post")
        {
            Description = "post-process every frame: invert | magnify",
            HelpName = "STAGE",
        }.AcceptOnlyFromAmong("invert", "magnify"));
        var cornerRadius = cli.Add(new Option<int>("--corner-radius")
        {
            Description = "round window corners with a texture shader, radius in logical pixels",
            HelpName = "N",
            DefaultValueFactory = _ => 0,
        });

        var framesOption = cli.Add(CommonOptions.Frames());
        var transport = cli.Add(CommonOptions.Transport());
        var channel = cli.Add(CommonOptions.WaypipeListen());

        return cli.Run(args, result =>
        {
            var chosen = result.GetValue(backend);
            cli.ConfigureLogging(result);
            var status = Run(
                BasinLog.For("TinyComp"),
                result.GetValue(outputs),
                result.GetValue(renderer)!,
                chosen.Kind == BackendKind.Drm,
                result.GetValue(fullRepaint),
                result.GetValue(damageTint),
                result.GetValue(scales)!,
                result.GetValue(offload),
                result.GetValue(nightLight),
                result.GetValue(hdr),
                result.GetValue(colorSource) switch
                {
                    "srgb" => Basin.Capabilities.OutputColorProfileSource.Srgb,
                    var icc when icc!.StartsWith("icc:", StringComparison.Ordinal) =>
                        Basin.Capabilities.OutputColorProfileSource.Icc,
                    _ => Basin.Capabilities.OutputColorProfileSource.Edid,
                },
                result.GetValue(colorSource) is { } source && source.StartsWith("icc:", StringComparison.Ordinal)
                    ? source["icc:".Length..]
                    : null,
                result.GetValue(frame) switch
                {
                    "flat" => FrameStyle.Flat,
                    "none" => FrameStyle.None,
                    _ => FrameStyle.Beos,
                },
                !result.GetValue(transactions),
                chosen.SocketFd,
                result.GetValue(wobbly),
                result.GetValue(openAnimation),
                result.GetValue(post),
                result.GetValue(closeAnimation),
                result.GetValue(switcher),
                result.GetValue(cornerRadius),
                result.GetValue(framesOption),
                result.GetValue(transport).Kind == TransportKind.Managed,
                result.GetValue(channel),
                out var rendered);
            cli.ReportFrames(rendered);
            return status;
        });
    }

    private static int Run(
        BasinLogger log,
        int outputCount,
        string rendererName,
        bool drm,
        bool fullRepaint,
        bool damageTint,
        double[] scales,
        bool offload,
        double? nightLight,
        bool hdr,
        Basin.Capabilities.OutputColorProfileSource colorSource,
        string? iccProfile,
        FrameStyle frameStyle,
        bool noTransactions,
        int socketFd,
        bool wobbly,
        string? openAnimation,
        string? post,
        string? closeAnimation,
        bool switcher,
        int cornerRadius,
        long frames,
        bool managedTransport,
        string? channelEndpoint,
        out long renderedFrames)
    {
        using var comp = new TinyComp(outputCount, rendererName, drm, fullRepaint, damageTint, scales, offload, nightLight, hdr, colorSource, frameStyle, noTransactions, socketFd, log, wobbly, openAnimation, post, closeAnimation, switcher, cornerRadius, frames, managedTransport, channelEndpoint, iccProfile);
        var status = comp.Run();
        renderedFrames = comp.Rendered;
        return status;
    }
}
