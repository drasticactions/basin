using Basin.Shell.Xdg.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Shell.Xdg;

/// <summary>
/// The <c>xdg_toplevel_tag_manager_v1</c> global. It reports the tags and descriptions that clients set on their toplevels.
/// </summary>
public sealed class XdgToplevelTagManager : IDisposable, IXdgToplevelTagManagerV1Handler
{
    /// <summary>
    /// The version of <c>xdg_toplevel_tag_manager_v1</c> that the global advertises.
    /// </summary>
    public const int Version = 1;

    private readonly WlGlobal _global;

    /// <summary>
    /// Initializes a new instance of the <see cref="XdgToplevelTagManager"/> class. It creates the global on <paramref name="display"/> at once.
    /// </summary>
    /// <param name="display">The display that advertises the global.</param>
    public XdgToplevelTagManager(WlServerDisplay display)
    {
        _global = display.CreateGlobal(XdgToplevelTagManagerV1.Interface, Version, OnBind);
    }

    /// <summary>
    /// Occurs when a client sets the tag of a live toplevel. The arguments are the toplevel and the tag.
    /// </summary>
    public event Action<XdgToplevelWindow, string>? TagSet;

    /// <summary>
    /// Occurs when a client sets the description of a live toplevel. The arguments are the toplevel and the description.
    /// </summary>
    public event Action<XdgToplevelWindow, string>? DescriptionSet;

    /// <summary>
    /// Destroys the global.
    /// </summary>
    public void Dispose() => _global.Dispose();

    private void OnBind(WlClient client, uint version, uint id) => _ = new XdgToplevelTagManagerV1Resource(client, version, id, this);

    void IXdgToplevelTagManagerV1Handler.Destroy(XdgToplevelTagManagerV1Resource resource)
    {
    }

    void IXdgToplevelTagManagerV1Handler.SetToplevelTag(XdgToplevelTagManagerV1Resource resource, in XdgToplevelTagManagerV1Resource.SetToplevelTagEventArgs args)
    {
        if (args.Toplevel is not null && XdgToplevelRegistry.Resolve(args.Toplevel) is { } toplevel)
        {
            TagSet?.Invoke(toplevel, args.Tag);
        }
    }

    void IXdgToplevelTagManagerV1Handler.SetToplevelDescription(XdgToplevelTagManagerV1Resource resource, in XdgToplevelTagManagerV1Resource.SetToplevelDescriptionEventArgs args)
    {
        if (args.Toplevel is not null && XdgToplevelRegistry.Resolve(args.Toplevel) is { } toplevel)
        {
            DescriptionSet?.Invoke(toplevel, args.Description);
        }
    }
}
