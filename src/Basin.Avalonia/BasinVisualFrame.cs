using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using Basin.Diagnostics;
using SkiaSharp;
using Basin.Hosted;
using static Basin.Avalonia.AvaloniaLog;

namespace Basin.Avalonia;

internal static class BasinVisualFrame
{
    public static void Commit(BasinCompositorHost host, BasinViewOutput view, ISkiaSharpApiLeaseFeature? feature, TimeSpan stamp)
    {
        var began = host.EnterFrame(stamp);
        try
        {
            if (feature is not null)
            {
                using var lease = feature.Lease();
                if (host.Renderer.BindFrame(lease))
                {
                    try
                    {
                        view.SceneOutput.Ring.AddWhole();
                        var options = new Basin.Scene.SceneCommitOptions { Background = RenderColor.Transparent };
                        if (host.Session.CommitOutput(view.SceneOutput, host.Renderer, view.Target, 0, options))
                        {
                            host.NotifyComposited();
                        }
                    }
                    finally
                    {
                        host.Renderer.UnbindFrame();
                    }

                    if (host.TakeScreenshotRequest(out var path, out var done))
                    {
                        done(Screenshot(host, view, lease, path));
                    }
                }
            }
        }
        finally
        {
            if (began)
            {
                host.Scene.SendFrameDone((uint)Environment.TickCount);
                host.ExitFrame();
            }
        }
    }

    private static bool Screenshot(BasinCompositorHost host, BasinViewOutput view, ISkiaSharpApiLease lease, string path)
    {
        var target = view.Target;
        var info = new SKImageInfo(target.Width, target.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = (lease.GrContext is { } gr ? SKSurface.Create(gr, false, info) : null) ?? SKSurface.Create(info);
        if (surface is null)
        {
            Log.Error($"no surface for a {target.Width}x{target.Height} screenshot");
            return false;
        }

        surface.Canvas.Scale((float)target.Scale);
        if (!host.Renderer.BindFrame(surface.Canvas, lease.GrContext))
        {
            return false;
        }

        try
        {
            var options = new Basin.Scene.SceneRenderOptions
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
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        if (data is null)
        {
            return false;
        }

        try
        {
            using var file = File.Create(path);
            data.SaveTo(file);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Log.Error($"the screenshot could not be written to {path}: {error.Message}");
            return false;
        }

        return true;
    }

    public static void Pump(BasinCompositorHost host, TimeSpan stamp)
    {
        var began = host.EnterFrame(stamp);
        if (began)
        {
            host.Scene.SendFrameDone((uint)Environment.TickCount);
            host.ExitFrame();
        }
    }
}
