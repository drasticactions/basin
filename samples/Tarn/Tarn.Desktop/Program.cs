using Avalonia;
using Basin.Video.FFmpeg;

namespace Tarn.Desktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--endpoint" when i + 1 < args.Length:
                    TarnApp.StartupEndpoint = args[++i];
                    break;
                case "--connect":
                    TarnApp.AutoConnect = true;
                    break;
            }
        }

        TarnApp.VideoDecoder = FFmpegVideoDecoder.TryCreate(preferHardware: false, out var whyNot);
        TarnApp.CarriesDmabuf = TarnApp.VideoDecoder is not null;
        if (whyNot is not null)
        {
            Console.Error.WriteLine($"tarn: no video: {whyNot}");
        }

        return AppBuilder.Configure<TarnApp>()
            .UsePlatformDetect()
            .StartWithClassicDesktopLifetime(args);
    }
}
