using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Basin.Shell.Xdg.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Shell.Xdg;

public sealed class XdgShell : IDisposable
{
    public const int Version = 7;

    private readonly WlGlobal _global;
    private readonly Dictionary<XdgPositionerResource, XdgPositionerRules> _positioners = [];
    private readonly List<XdgWmBaseResource> _bases = [];
    private uint _pingSerial;

    public XdgShell(WlServerDisplay display, CompositorGlobal compositor, Basin.Seat.Seat? seat = null)
    {
        Display = display;
        Compositor = compositor;
        Seat = seat;
        _global = display.CreateGlobal(XdgWmBase.Interface, Version, OnBind);
    }

    public WlServerDisplay Display { get; }

    public CompositorGlobal Compositor { get; }

    public Basin.Seat.Seat? Seat { get; }

    public event Action<XdgToplevelWindow>? NewToplevel;

    public event Action<XdgPopupWindow>? NewPopup;

    public event Action<WlClient, uint>? Ponged;

    public IReadOnlyList<WlClient> BoundClients =>
        _bases.Where(b => !b.IsDestroyed).Select(b => b.Client).Distinct().ToArray();

    public uint Ping(WlClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        var serial = 0u;
        foreach (var wmBase in _bases)
        {
            if (wmBase.IsDestroyed || wmBase.Client != client)
            {
                continue;
            }

            if (serial == 0)
            {
                serial = ++_pingSerial;
            }

            wmBase.SendPing(serial);
        }

        return serial;
    }

    public void Dispose() => _global.Dispose();

    private void OnBind(WlClient client, uint version, uint id)
    {
        var wmBase = new XdgWmBaseResource(client, version, id);

        wmBase.CreatePositioner += (_, e) =>
        {
            var resource = new XdgPositionerResource(client, wmBase.Version, e.Id);
            _positioners[resource] = default;
            resource.Destroyed += (_, _) => _positioners.Remove(resource);
            WirePositioner(resource);
        };

        wmBase.GetXdgSurface += (_, e) =>
        {
            var surface = Compositor.ResolveSurface(e.Surface);
            if (surface is null)
            {
                wmBase.PostError((uint)XdgWmBase.Error.Role, "unknown wl_surface");
                return;
            }

            if (surface.Current.Buffer is not null || surface.Pending.Buffer is not null)
            {
                wmBase.PostError((uint)XdgWmBase.Error.InvalidSurfaceState, "surface already has a buffer at xdg_surface creation");
                return;
            }

            var resource = new XdgSurfaceResource(client, wmBase.Version, e.Id);
            _ = new XdgSurfaceState(this, wmBase, resource, surface);
        };

        _bases.Add(wmBase);
        wmBase.Destroyed += (_, _) => _bases.Remove(wmBase);
        wmBase.Pong += (_, e) => Ponged?.Invoke(client, e.Serial);
    }

    private void WirePositioner(XdgPositionerResource resource)
    {
        resource.SetSize += (_, e) =>
        {
            if (e.Width <= 0 || e.Height <= 0)
            {
                resource.PostError((uint)XdgPositioner.Error.InvalidInput, "positioner size must be positive");
                return;
            }

            ref var rules = ref RulesOf(resource);
            if (!Unsafe.IsNullRef(ref rules))
            {
                rules = rules with { Width = e.Width, Height = e.Height };
            }
        };
        resource.SetAnchorRect += (_, e) =>
        {
            if (e.Width < 0 || e.Height < 0)
            {
                resource.PostError((uint)XdgPositioner.Error.InvalidInput, "anchor rect size must be non-negative");
                return;
            }

            ref var rules = ref RulesOf(resource);
            if (!Unsafe.IsNullRef(ref rules))
            {
                rules = rules with { AnchorRect = new Box(e.X, e.Y, e.Width, e.Height) };
            }
        };
        resource.SetAnchor += (_, e) =>
        {
            ref var rules = ref RulesOf(resource);
            if (!Unsafe.IsNullRef(ref rules))
            {
                rules = rules with { Anchor = e.Anchor };
            }
        };
        resource.SetGravity += (_, e) =>
        {
            ref var rules = ref RulesOf(resource);
            if (!Unsafe.IsNullRef(ref rules))
            {
                rules = rules with { Gravity = e.Gravity };
            }
        };
        resource.SetConstraintAdjustment += (_, e) =>
        {
            ref var rules = ref RulesOf(resource);
            if (!Unsafe.IsNullRef(ref rules))
            {
                rules = rules with { ConstraintAdjustment = e.ConstraintAdjustment };
            }
        };
        resource.SetOffset += (_, e) =>
        {
            ref var rules = ref RulesOf(resource);
            if (!Unsafe.IsNullRef(ref rules))
            {
                rules = rules with { OffsetX = e.X, OffsetY = e.Y };
            }
        };
        resource.SetReactive += (_, _) =>
        {
            ref var rules = ref RulesOf(resource);
            if (!Unsafe.IsNullRef(ref rules))
            {
                rules = rules with { Reactive = true };
            }
        };
    }

    private ref XdgPositionerRules RulesOf(XdgPositionerResource resource) =>
        ref CollectionsMarshal.GetValueRefOrNullRef(_positioners, resource);

    internal bool TryGetPositioner(XdgPositionerResource? resource, out XdgPositionerRules rules)
    {
        if (resource is not null && _positioners.TryGetValue(resource, out rules) && rules.IsComplete)
        {
            return true;
        }

        rules = default;
        return false;
    }

    internal void EmitNewToplevel(XdgToplevelWindow toplevel) => NewToplevel?.Invoke(toplevel);

    internal void EmitNewPopup(XdgPopupWindow popup) => NewPopup?.Invoke(popup);
}
