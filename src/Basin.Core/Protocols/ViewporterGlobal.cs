using Basin.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin;

public sealed class ViewporterGlobal : IDisposable
{
    public const int Version = 1;

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly HashSet<Surface> _withViewport = [];

    public ViewporterGlobal(WlServerDisplay display, CompositorGlobal compositor)
    {
        _compositor = compositor;
        _global = display.CreateGlobal(WpViewporter.Interface, Version, OnBind);
    }

    public void Dispose() => _global.Dispose();

    private void OnBind(WlClient client, uint version, uint id)
    {
        var viewporter = new WpViewporterResource(client, version, id);
        viewporter.GetViewport += (_, e) =>
        {
            var surface = _compositor.ResolveSurface(e.Surface);
            if (surface is null)
            {
                viewporter.PostError((uint)WpViewporter.Error.ViewportExists, "unknown surface");
                return;
            }

            if (!_withViewport.Add(surface))
            {
                viewporter.PostError((uint)WpViewporter.Error.ViewportExists, "surface already has a viewport");
                return;
            }

            var viewport = new WpViewportResource(client, viewporter.Version, e.Id);
            WireViewport(viewport, surface);
        };
    }

    private void WireViewport(WpViewportResource viewport, Surface surface)
    {
        surface.ViewportResource = viewport;
        viewport.SetSource += (_, e) =>
        {
            var x = e.X.ToDouble();
            var y = e.Y.ToDouble();
            var width = e.Width.ToDouble();
            var height = e.Height.ToDouble();
            var unset = x == -1 && y == -1 && width == -1 && height == -1;
            if (!unset && (width <= 0 || height <= 0 || x < 0 || y < 0))
            {
                viewport.PostError((uint)WpViewport.Error.BadValue, "invalid viewport source rectangle");
                return;
            }

            surface.Pending.ViewportSourceX = unset ? 0 : x;
            surface.Pending.ViewportSourceY = unset ? 0 : y;
            surface.Pending.ViewportSourceWidth = unset ? -1 : width;
            surface.Pending.ViewportSourceHeight = unset ? -1 : height;
            surface.Pending.Committed |= SurfaceStateFields.Viewport;
        };

        viewport.SetDestination += (_, e) =>
        {
            var unset = e.Width == -1 && e.Height == -1;
            if (!unset && (e.Width <= 0 || e.Height <= 0))
            {
                viewport.PostError((uint)WpViewport.Error.BadValue, "invalid viewport destination size");
                return;
            }

            surface.Pending.ViewportDestinationWidth = unset ? -1 : e.Width;
            surface.Pending.ViewportDestinationHeight = unset ? -1 : e.Height;
            surface.Pending.Committed |= SurfaceStateFields.Viewport;
        };

        viewport.Destroyed += (_, _) =>
        {
            _withViewport.Remove(surface);
            if (!surface.IsDestroyed)
            {
                surface.ViewportResource = null;
                surface.Pending.ViewportSourceWidth = -1;
                surface.Pending.ViewportSourceHeight = -1;
                surface.Pending.ViewportDestinationWidth = -1;
                surface.Pending.ViewportDestinationHeight = -1;
                surface.Pending.Committed |= SurfaceStateFields.Viewport;
            }
        };
    }
}
