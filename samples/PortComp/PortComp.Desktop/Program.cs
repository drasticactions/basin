using Avalonia;
using Basin.Video.FFmpeg;

namespace PortComp.Desktop;

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
                    PortCompApp.StartupEndpoint = args[++i];
                    break;
                case "--listen" when i + 1 < args.Length:
                    PortCompApp.StartupEndpoint = args[++i];
                    PortCompApp.StartupListen = true;
                    PortCompApp.AutoConnect = true;
                    break;
                case "--hyperv-vm" when i + 1 < args.Length:
                    PortCompApp.StartupHyperVVm = args[++i];
                    break;
                case "--connect":
                    PortCompApp.AutoConnect = true;
                    break;
            }
        }

        PortCompApp.VideoDecoder = FFmpegVideoDecoder.TryCreate(preferHardware: false, out var whyNot);
        PortCompApp.CarriesDmabuf = PortCompApp.VideoDecoder is not null;
        if (whyNot is not null)
        {
            Console.Error.WriteLine($"port-comp: no video: {whyNot}");
        }

        return AppBuilder.Configure<PortCompApp>()
            .UsePlatformDetect()
            .StartWithClassicDesktopLifetime(args);
    }
}
