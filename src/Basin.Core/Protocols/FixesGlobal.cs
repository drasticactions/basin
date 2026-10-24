using Wayland;
using Wayland.Server;

namespace Basin;

/// <summary>
/// Serves <c>wl_fixes</c>, which lets a client destroy a registry and acknowledge a global removal. The global exists only when libwayland supports it.
/// </summary>
public sealed class FixesGlobal : IDisposable, IWlFixesHandler
{
    /// <summary>
    /// The protocol version that the global advertises.
    /// </summary>
    public const int Version = 2;

    private readonly WlServerDisplay _display;
    private readonly WlGlobal? _global;

    /// <summary>
    /// Initializes a new instance of the <see cref="FixesGlobal"/> class. It creates no global when libwayland lacks the support.
    /// </summary>
    /// <param name="display">The display that the global belongs to.</param>
    public FixesGlobal(WlServerDisplay display)
    {
        ArgumentNullException.ThrowIfNull(display);
        _display = display;
        if (display.SupportsFixes)
        {
            _global = display.CreateGlobal(WlFixes.Interface, Version, OnBind);
        }
    }

    /// <summary>
    /// Gets a value indicating whether the global was created.
    /// </summary>
    public bool IsPublished => _global is not null;

    /// <summary>
    /// Destroys the global.
    /// </summary>
    public void Dispose() => _global?.Dispose();

    private void OnBind(WlClient client, uint version, uint id)
    {
        _ = new WlFixesResource(client, version, id, this);
    }

    void IWlFixesHandler.Destroy(WlFixesResource resource)
    {
    }

    void IWlFixesHandler.DestroyRegistry(WlFixesResource resource, in WlFixesResource.DestroyRegistryEventArgs args) =>
        _display.DestroyRegistry(resource.Client, args.RegistryHandle);

    void IWlFixesHandler.AckGlobalRemove(WlFixesResource resource, in WlFixesResource.AckGlobalRemoveEventArgs args) =>
        _display.AckGlobalRemove(resource.Client, resource.RawHandle, args.RegistryHandle, args.Name);
}
