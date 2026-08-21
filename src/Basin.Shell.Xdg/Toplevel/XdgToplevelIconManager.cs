using Basin.Shell.Xdg.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Shell.Xdg;

public sealed class XdgToplevelIconManager : IDisposable
{
    public const int Version = 1;

    private readonly WlGlobal _global;
    private static readonly Dictionary<nint, string?> IconNames = [];

    public XdgToplevelIconManager(WlServerDisplay display)
    {
        _global = display.CreateGlobal(XdgToplevelIconManagerV1.Interface, Version, OnBind);
    }

    public event Action<XdgToplevelWindow, string?>? IconChanged;

    public void Dispose() => _global.Dispose();

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new XdgToplevelIconManagerV1Resource(client, version, id);
        manager.SendDone();

        manager.CreateIcon += (_, e) =>
        {
            var icon = new XdgToplevelIconV1Resource(client, manager.Version, e.Id);
            var raw = icon.RawHandle;
            IconNames[raw] = null;
            icon.SetName += (_, ne) => IconNames[raw] = ne.IconName;
            icon.Destroyed += (_, _) => IconNames.Remove(raw);
        };
        manager.SetIcon += (_, e) =>
        {
            if (e.Toplevel is null || XdgToplevelRegistry.Resolve(e.Toplevel) is not { } toplevel)
            {
                return;
            }

            var name = e.Icon is { } icon ? IconNames.GetValueOrDefault(icon.RawHandle) : null;
            IconChanged?.Invoke(toplevel, name);
        };
    }
}
