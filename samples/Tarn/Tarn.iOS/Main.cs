using Basin.Video.VideoToolbox;
using UIKit;

namespace Tarn.iOS;

public static class Program
{
    public static void Main(string[] args)
    {
        TarnApp.VideoDecoder = VideoToolboxVideoDecoder.TryCreate(out var whyNot);
        TarnApp.CarriesDmabuf = TarnApp.VideoDecoder is not null;
        if (whyNot is not null)
        {
            Console.Error.WriteLine($"tarn: no video: {whyNot}");
        }

        UIApplication.Main(args, null, typeof(AppDelegate));
    }
}
