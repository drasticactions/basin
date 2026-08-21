using System.Diagnostics;
using Basin;
using Basin.Backend.Headless;
using Basin.Cli;
using Basin.Diagnostics;
using Basin.Render.Pixman;
using Basin.Scene;
using Microsoft.Extensions.Logging;
using Wayland;
using Wayland.Server;

namespace Basin.Samples.Headless;

internal sealed class XdgShell
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
        var wmBase = new XdgWmBaseResource(client, version, id);
        wmBase.GetXdgSurface += (_, e) =>
        {
            var surface = _compositor.ResolveSurface(e.Surface);
            if (surface is null)
            {
                wmBase.PostError((uint)XdgWmBase.Error.Role, "unknown surface");
                return;
            }

            var xdgSurface = new XdgSurfaceResource(client, wmBase.Version, e.Id);
            xdgSurface.GetToplevel += (_, t) => OnGetToplevel(client, xdgSurface, surface, t.Id);
        };
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
}
