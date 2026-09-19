using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;

namespace Tarn.Android;

[Application]
public sealed class TarnApplication : AvaloniaAndroidApplication<TarnApp>
{
    public TarnApplication(nint javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    public override void OnCreate()
    {
        TarnApp.VideoDecoder = Basin.Video.MediaCodec.MediaCodecVideoDecoder.TryCreate(out var whyNot);
        TarnApp.CarriesDmabuf = TarnApp.VideoDecoder is not null;
        if (whyNot is not null)
        {
            Console.Error.WriteLine($"tarn: no video: {whyNot}");
        }

        base.OnCreate();
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) =>
        base.CustomizeAppBuilder(builder)
            .With(new AndroidPlatformOptions
            {
                RenderingMode = [AndroidRenderingMode.Egl, AndroidRenderingMode.Software],
            });
}
