using Basin.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin;

/// <summary>
/// Serves <c>wp_viewporter</c>. A viewport writes its source crop and destination size into the pending state of its surface. A request on a viewport whose surface is destroyed raises <c>no_surface</c>.
/// </summary>
public sealed class ViewporterGlobal : IDisposable, IWpViewporterHandler
{
    /// <summary>
    /// The protocol version that the global advertises.
    /// </summary>
    public const int Version = 1;

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly HashSet<Surface> _withViewport = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="ViewporterGlobal"/> class.
    /// </summary>
    /// <param name="display">The display that the global belongs to.</param>
    /// <param name="compositor">The compositor global that resolves surface resources.</param>
    public ViewporterGlobal(WlServerDisplay display, CompositorGlobal compositor)
    {
        _compositor = compositor;
        _global = display.CreateGlobal(WpViewporter.Interface, Version, OnBind);
    }

    /// <summary>
    /// Destroys the global. Viewports that exist stay usable until their clients destroy them.
    /// </summary>
    public void Dispose() => _global.Dispose();

    private void OnBind(WlClient client, uint version, uint id)
    {
        _ = new WpViewporterResource(client, version, id, this);
    }

    void IWpViewporterHandler.Destroy(WpViewporterResource resource)
    {
    }

    void IWpViewporterHandler.GetViewport(WpViewporterResource resource, in WpViewporterResource.GetViewportEventArgs args)
    {
        var surface = _compositor.ResolveSurface(args.Surface);
        if (surface is null)
        {
            resource.PostError((uint)WpViewporter.Error.ViewportExists, "unknown surface");
            return;
        }

        if (!_withViewport.Add(surface))
        {
            resource.PostError((uint)WpViewporter.Error.ViewportExists, "surface already has a viewport");
            return;
        }

        var viewport = new WpViewportResource(resource.Client, resource.Version, args.Id, new Viewport(surface));
        WireViewport(viewport, surface);
    }

    private void WireViewport(WpViewportResource viewport, Surface surface)
    {
        surface.ViewportResource = viewport;
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

    private sealed class Viewport(Surface surface) : IWpViewportHandler
    {
        void IWpViewportHandler.Destroy(WpViewportResource resource)
        {
        }

        void IWpViewportHandler.SetSource(WpViewportResource resource, in WpViewportResource.SetSourceEventArgs args)
        {
            if (surface.IsDestroyed)
            {
                resource.PostError((uint)WpViewport.Error.NoSurface, "the surface of the viewport is destroyed");
                return;
            }

            var x = args.X.ToDouble();
            var y = args.Y.ToDouble();
            var width = args.Width.ToDouble();
            var height = args.Height.ToDouble();
            var unset = x == -1 && y == -1 && width == -1 && height == -1;
            if (!unset && (width <= 0 || height <= 0 || x < 0 || y < 0))
            {
                resource.PostError((uint)WpViewport.Error.BadValue, "invalid viewport source rectangle");
                return;
            }

            surface.Pending.ViewportSourceX = unset ? 0 : x;
            surface.Pending.ViewportSourceY = unset ? 0 : y;
            surface.Pending.ViewportSourceWidth = unset ? -1 : width;
            surface.Pending.ViewportSourceHeight = unset ? -1 : height;
            surface.Pending.Committed |= SurfaceStateFields.Viewport;
        }

        void IWpViewportHandler.SetDestination(WpViewportResource resource, in WpViewportResource.SetDestinationEventArgs args)
        {
            if (surface.IsDestroyed)
            {
                resource.PostError((uint)WpViewport.Error.NoSurface, "the surface of the viewport is destroyed");
                return;
            }

            var unset = args.Width == -1 && args.Height == -1;
            if (!unset && (args.Width <= 0 || args.Height <= 0))
            {
                resource.PostError((uint)WpViewport.Error.BadValue, "invalid viewport destination size");
                return;
            }

            surface.Pending.ViewportDestinationWidth = unset ? -1 : args.Width;
            surface.Pending.ViewportDestinationHeight = unset ? -1 : args.Height;
            surface.Pending.Committed |= SurfaceStateFields.Viewport;
        }
    }
}
