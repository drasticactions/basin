using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Browser;

[assembly: SupportedOSPlatform("browser")]

namespace Tarn.Browser;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        if (args.Length > 0 && Uri.TryCreate(args[0], UriKind.Absolute, out var location))
        {
            var query = System.Web.HttpUtility.ParseQueryString(location.Query);
            TarnApp.StartupEndpoint = query["endpoint"];
            TarnApp.AutoConnect = query["connect"] is "1" or "true";
            if (query["log"] is { Length: > 0 } level && Enum.TryParse<Basin.Diagnostics.BasinLogLevel>(level, true, out var threshold))
            {
                Basin.Diagnostics.BasinLog.Level = threshold;
                Basin.Diagnostics.BasinLog.Sink = new Basin.Diagnostics.StandardErrorLogSink();
            }
        }

        TarnApp.VideoDecoder = await Basin.Video.WebCodecs.WebCodecsVideoDecoder.TryCreateAsync();
        TarnApp.CarriesDmabuf = TarnApp.VideoDecoder is not null;
        Console.WriteLine(Basin.Video.WebCodecs.WebCodecsVideoDecoder.WhyNot is { } whyNot
            ? $"tarn: no video: {whyNot}"
            : $"tarn: video over WebCodecs, h264 {TarnApp.VideoDecoder!.Supports(Basin.Capabilities.VideoCodec.H264)}");

        await AppBuilder.Configure<TarnApp>()
            .StartBrowserAppAsync("out", new BrowserPlatformOptions
            {
                RenderingMode = [BrowserRenderingMode.WebGL2, BrowserRenderingMode.WebGL1, BrowserRenderingMode.Software2D],
            });
    }
}
