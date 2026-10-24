using SkiaSharp;
using static Basin.Hosted.HostedLog;

namespace Basin.Hosted;

internal static class HostedViewCapture
{
    public static SKImageInfo Info(BasinViewOutput view) =>
        new(view.Target.Width, view.Target.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);

    public static byte[]? Read(BasinCompositorHost host, BasinViewOutput view, GRContext? context)
    {
        var info = Info(view);
        var pixels = new byte[info.BytesSize];
        unsafe
        {
            fixed (byte* data = pixels)
            {
                return Render(host, view, info, (nint)data, context) ? pixels : null;
            }
        }
    }

    public static bool Render(BasinCompositorHost host, BasinViewOutput view, SKImageInfo info, nint pixels, GRContext? context)
    {
        var gpu = context is not null ? SKSurface.Create(context, false, info.WithAlphaType(SKAlphaType.Premul)) : null;
        if (gpu is null)
        {
            context = null;
        }

        using var surface = gpu ?? SKSurface.Create(info);
        if (surface is null)
        {
            return false;
        }

        var target = view.Target;
        surface.Canvas.Scale((float)target.Scale);
        if (!host.Renderer.BindFrame(surface.Canvas, context))
        {
            return false;
        }

        try
        {
            var options = new Scene.SceneRenderOptions
            {
                Background = new RenderColor(0f, 0f, 0f, 1f),
                Scale = target.Scale,
                OriginX = view.SceneOutput.Position.X,
                OriginY = view.SceneOutput.Position.Y,
            };
            if (!host.Scene.Render(host.Renderer, target, options))
            {
                return false;
            }
        }
        finally
        {
            host.Renderer.UnbindFrame();
        }

        surface.Canvas.Flush();
        return surface.ReadPixels(info, pixels, info.RowBytes, 0, 0);
    }

    public static bool WritePng(byte[] pixels, int width, int height, string path)
    {
        try
        {
            var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            using var image = SKImage.FromPixelCopy(info, pixels);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var file = File.Create(path);
            data.SaveTo(file);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Log.Warn(error, $"screenshot: could not write {path}");
            return false;
        }
    }
}
