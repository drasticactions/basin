using Basin.Shell.Xdg.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Shell.Xdg;

/// <summary>
/// The <c>xdg_toplevel_icon_manager_v1</c> global. It reports the icon name that a client sets on a toplevel and ignores icon buffers.
/// </summary>
public sealed class XdgToplevelIconManager : IDisposable, IXdgToplevelIconManagerV1Handler, IXdgToplevelIconV1Handler
{
    /// <summary>
    /// The version of <c>xdg_toplevel_icon_manager_v1</c> that the global advertises.
    /// </summary>
    public const int Version = 1;

    private readonly WlGlobal _global;
    private static readonly Dictionary<nint, string?> IconNames = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="XdgToplevelIconManager"/> class. It creates the global on <paramref name="display"/> at once.
    /// </summary>
    /// <param name="display">The display that advertises the global.</param>
    public XdgToplevelIconManager(WlServerDisplay display)
    {
        _global = display.CreateGlobal(XdgToplevelIconManagerV1.Interface, Version, OnBind);
    }

    /// <summary>
    /// Occurs when a client sets the icon of a live toplevel. The arguments are the toplevel and the icon name, or <see langword="null"/> when the icon is unset or has no name.
    /// </summary>
    public event Action<XdgToplevelWindow, string?>? IconChanged;

    /// <summary>
    /// Destroys the global.
    /// </summary>
    public void Dispose() => _global.Dispose();

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new XdgToplevelIconManagerV1Resource(client, version, id, this);
        manager.SendDone();
    }

    void IXdgToplevelIconManagerV1Handler.Destroy(XdgToplevelIconManagerV1Resource resource)
    {
    }

    void IXdgToplevelIconManagerV1Handler.CreateIcon(XdgToplevelIconManagerV1Resource resource, in XdgToplevelIconManagerV1Resource.CreateIconEventArgs args)
    {
        var icon = new XdgToplevelIconV1Resource(resource.Client, resource.Version, args.Id, this);
        var raw = icon.RawHandle;
        IconNames[raw] = null;
        icon.Destroyed += (_, _) => IconNames.Remove(raw);
    }

    void IXdgToplevelIconManagerV1Handler.SetIcon(XdgToplevelIconManagerV1Resource resource, in XdgToplevelIconManagerV1Resource.SetIconEventArgs args)
    {
        if (args.Toplevel is null || XdgToplevelRegistry.Resolve(args.Toplevel) is not { } toplevel)
        {
            return;
        }

        var name = args.Icon is { } icon ? IconNames.GetValueOrDefault(icon.RawHandle) : null;
        IconChanged?.Invoke(toplevel, name);
    }

    void IXdgToplevelIconV1Handler.Destroy(XdgToplevelIconV1Resource resource)
    {
    }

    void IXdgToplevelIconV1Handler.SetName(XdgToplevelIconV1Resource resource, in XdgToplevelIconV1Resource.SetNameEventArgs args) =>
        IconNames[resource.RawHandle] = args.IconName;

    void IXdgToplevelIconV1Handler.AddBuffer(XdgToplevelIconV1Resource resource, in XdgToplevelIconV1Resource.AddBufferEventArgs args)
    {
    }
}
