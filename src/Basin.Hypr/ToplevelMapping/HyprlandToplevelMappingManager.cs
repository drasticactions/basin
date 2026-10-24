using Basin.Capabilities;
using Basin.Desktop;
using Basin.Hypr.Protocol;
using Wayland.Server;

namespace Basin.Hypr;

/// <summary>
/// Advertises <c>hyprland_toplevel_mapping_manager_v1</c>. It answers a foreign-toplevel handle with the toplevel id, split into a high and a low 32-bit word.
/// </summary>
public sealed class HyprlandToplevelMappingManager : IDisposable, IHyprlandToplevelMappingManagerV1Handler
{
    /// <summary>
    /// The highest protocol version that the global advertises.
    /// </summary>
    public const int Version = 1;

    private readonly WlGlobal _global;
    private readonly IToplevelModel? _model;

    /// <summary>
    /// Initializes a new instance of the <see cref="HyprlandToplevelMappingManager"/> class.
    /// </summary>
    /// <param name="display">The display that hosts the global.</param>
    /// <param name="model">The model that confirms the toplevel exists. When it is <see langword="null"/>, every request fails.</param>
    /// <exception cref="ArgumentNullException"><paramref name="display"/> is <see langword="null"/>.</exception>
    public HyprlandToplevelMappingManager(WlServerDisplay display, IToplevelModel? model)
    {
        ArgumentNullException.ThrowIfNull(display);
        _model = model;
        _global = display.CreateGlobal(HyprlandToplevelMappingManagerV1.Interface, Version, OnBind);
    }

    /// <summary>
    /// Destroys the global.
    /// </summary>
    public void Dispose() => _global.Dispose();

    private void OnBind(WlClient client, uint version, uint id)
    {
        _ = new HyprlandToplevelMappingManagerV1Resource(client, version, id, this);
    }

    void IHyprlandToplevelMappingManagerV1Handler.GetWindowForToplevel(
        HyprlandToplevelMappingManagerV1Resource resource,
        in HyprlandToplevelMappingManagerV1Resource.GetWindowForToplevelEventArgs args)
    {
        var handle = new HyprlandToplevelWindowMappingHandleV1Resource(resource.Client, resource.Version, args.Handle);
        Answer(handle, ForeignToplevelListManager.ToplevelOf(args.ToplevelHandle));
    }

    void IHyprlandToplevelMappingManagerV1Handler.GetWindowForToplevelWlr(
        HyprlandToplevelMappingManagerV1Resource resource,
        in HyprlandToplevelMappingManagerV1Resource.GetWindowForToplevelWlrEventArgs args)
    {
        var handle = new HyprlandToplevelWindowMappingHandleV1Resource(resource.Client, resource.Version, args.Handle);
        Answer(handle, ForeignToplevelManager.ToplevelOf(args.ToplevelHandle));
    }

    void IHyprlandToplevelMappingManagerV1Handler.Destroy(HyprlandToplevelMappingManagerV1Resource resource)
    {
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
