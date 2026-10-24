using Basin.Capabilities;

namespace Basin.Shell.Xdg;

/// <summary>
/// Installs <c>zxdg_decoration_manager_v1</c>. The default mode is server-side when an <see cref="IFrameRenderer"/> is registered, and client-side when none is.
/// </summary>
public sealed class XdgDecorationModule : IProtocolModule
{
    /// <inheritdoc/>
    public string WireInterface => "zxdg_decoration_manager_v1";

    /// <inheritdoc/>
    public int Version => XdgDecorationManager.Version;

    /// <inheritdoc/>
    public IReadOnlyList<Type> Capabilities => [typeof(IFrameRenderer)];

    /// <summary>
    /// Gets the global that <see cref="Install"/> created. It is <see langword="null"/> before the install.
    /// </summary>
    public XdgDecorationManager? Manager { get; private set; }

    /// <inheritdoc/>
    public IDisposable Install(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Manager = new XdgDecorationManager(services.Display)
        {
            DefaultMode = services.Find<IFrameRenderer>() is null
                ? DecorationMode.ClientSide
                : DecorationMode.ServerSide,
        };
        services.Use(Manager);
        return Manager;
    }
}
