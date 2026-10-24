using System.Diagnostics;
using Basin;
using Basin.Backend.Headless;
using Basin.Cli;
using Basin.Diagnostics;
using Basin.Render.Pixman;
using Basin.Scene;
using Wayland;
using Wayland.Server;

namespace Basin.Samples.Headless;

internal sealed class XdgShell : IXdgWmBaseHandler
{
    public const string ToplevelRole = "xdg_toplevel";

    private readonly CompositorGlobal _compositor;
    private readonly WlGlobal _global;
    private uint _configureSerial;

    public XdgShell(WlServerDisplay display, CompositorGlobal compositor)
    {
        _compositor = compositor;
        _global = display.CreateGlobal(XdgWmBase.Interface, 5, OnBind);
    }

    public event Action<Surface>? ToplevelMapped;

    private void OnBind(WlClient client, uint version, uint id)
    {
        _ = new XdgWmBaseResource(client, version, id, this);
    }

    void IXdgWmBaseHandler.Destroy(XdgWmBaseResource resource)
    {
    }

    void IXdgWmBaseHandler.CreatePositioner(XdgWmBaseResource resource, in XdgWmBaseResource.CreatePositionerEventArgs args)
    {
    }

    void IXdgWmBaseHandler.GetXdgSurface(XdgWmBaseResource resource, in XdgWmBaseResource.GetXdgSurfaceEventArgs args)
    {
        var surface = _compositor.ResolveSurface(args.Surface);
        if (surface is null)
        {
            resource.PostError((uint)XdgWmBase.Error.Role, "unknown surface");
            return;
        }

        _ = new XdgSurfaceResource(resource.Client, resource.Version, args.Id, new ShellSurface(this, surface));
    }

    void IXdgWmBaseHandler.Pong(XdgWmBaseResource resource, in XdgWmBaseResource.PongEventArgs args)
    {
    }

    private void OnGetToplevel(WlClient client, XdgSurfaceResource xdgSurface, Surface surface, uint id)
    {
        var toplevel = new XdgToplevelResource(client, xdgSurface.Version, id);
        if (!surface.TrySetRole(ToplevelRole, toplevel))
        {
            toplevel.PostError((uint)XdgWmBase.Error.Role, $"surface already has the '{surface.Role}' role");
            return;
        }

        var configured = false;
        var mapped = false;
        surface.Committed += () =>
        {
            if (!configured && !toplevel.IsDestroyed && !xdgSurface.IsDestroyed)
            {
                toplevel.SendConfigure(0, 0, ReadOnlySpan<byte>.Empty);
                xdgSurface.SendConfigure(++_configureSerial);
                configured = true;
            }

            if (!mapped && surface.IsMapped)
            {
                mapped = true;
                ToplevelMapped?.Invoke(surface);
            }
        };

        toplevel.Destroyed += (_, _) => surface.ClearRoleObject();
    }

    private sealed class ShellSurface(XdgShell owner, Surface surface) : IXdgSurfaceHandler
    {
        void IXdgSurfaceHandler.Destroy(XdgSurfaceResource resource)
        {
        }

        void IXdgSurfaceHandler.GetToplevel(XdgSurfaceResource resource, in XdgSurfaceResource.GetToplevelEventArgs args) =>
            owner.OnGetToplevel(resource.Client, resource, surface, args.Id);

        void IXdgSurfaceHandler.GetPopup(XdgSurfaceResource resource, in XdgSurfaceResource.GetPopupEventArgs args)
        {
        }

        void IXdgSurfaceHandler.SetWindowGeometry(XdgSurfaceResource resource, in XdgSurfaceResource.SetWindowGeometryEventArgs args)
        {
        }

        void IXdgSurfaceHandler.AckConfigure(XdgSurfaceResource resource, in XdgSurfaceResource.AckConfigureEventArgs args)
        {
        }
    }
}
