using Basin.Shell.Xdg.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Shell.Xdg;

public sealed class XdgToplevelTagManager : IDisposable
{
    public const int Version = 1;

    private readonly WlGlobal _global;

    public XdgToplevelTagManager(WlServerDisplay display)
    {
        _global = display.CreateGlobal(XdgToplevelTagManagerV1.Interface, Version, OnBind);
    }

    public event Action<XdgToplevelWindow, string>? TagSet;

    public event Action<XdgToplevelWindow, string>? DescriptionSet;

    public void Dispose() => _global.Dispose();

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new XdgToplevelTagManagerV1Resource(client, version, id);
        manager.SetToplevelTag += (_, e) =>
        {
            if (e.Toplevel is not null && XdgToplevelRegistry.Resolve(e.Toplevel) is { } toplevel)
            {
                TagSet?.Invoke(toplevel, e.Tag);
            }
        };
        manager.SetToplevelDescription += (_, e) =>
        {
            if (e.Toplevel is not null && XdgToplevelRegistry.Resolve(e.Toplevel) is { } toplevel)
            {
                DescriptionSet?.Invoke(toplevel, e.Description);
            }
        };
    }
}
