using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using Basin.Diagnostics;

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
