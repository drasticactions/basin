using Basin.Capabilities;
using Basin.Desktop;
using Basin.Hypr.Protocol;
using Wayland.Server;

namespace Basin.Hypr;

public sealed class HyprlandToplevelMappingManager : IDisposable
{
    public const int Version = 1;

    private readonly WlGlobal _global;
    private readonly IToplevelModel? _model;

    public HyprlandToplevelMappingManager(WlServerDisplay display, IToplevelModel? model)
    {
        ArgumentNullException.ThrowIfNull(display);
        _model = model;
        _global = display.CreateGlobal(HyprlandToplevelMappingManagerV1.Interface, Version, OnBind);
    }

    public void Dispose() => _global.Dispose();

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new HyprlandToplevelMappingManagerV1Resource(client, version, id);
        manager.GetWindowForToplevel += (_, e) =>
        {
            var handle = new HyprlandToplevelWindowMappingHandleV1Resource(client, manager.Version, e.Handle);
            Answer(handle, ForeignToplevelListManager.ToplevelOf(e.ToplevelHandle));
        };
        manager.GetWindowForToplevelWlr += (_, e) =>
        {
            var handle = new HyprlandToplevelWindowMappingHandleV1Resource(client, manager.Version, e.Handle);
            Answer(handle, ForeignToplevelManager.ToplevelOf(e.ToplevelHandle));
        };
    }

    private void Answer(HyprlandToplevelWindowMappingHandleV1Resource handle, ulong toplevelId)
    {
        if (toplevelId == 0 || _model is null || !_model.TryGet(toplevelId, out _))
        {
            handle.SendFailed();
            return;
        }

        handle.SendWindowAddress((uint)(toplevelId >> 32), (uint)toplevelId);
    }
}
