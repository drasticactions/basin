using Basin.Capabilities;

namespace Basin.Shell.Xdg;

/// <summary>
/// Installs <c>zwlr_layer_shell_v1</c>. It requires an <see cref="ILayerShellDriver"/>, attaches the new <see cref="LayerShell"/> to it, and registers a default <see cref="ILayerDirectory"/>.
/// </summary>
public sealed class LayerShellModule : IProtocolModule
{
    /// <inheritdoc/>
    public string WireInterface => "zwlr_layer_shell_v1";

    /// <inheritdoc/>
    public int Version => LayerShell.Version;

    /// <inheritdoc/>
    public IReadOnlyList<Type> Drivers => [typeof(ILayerShellDriver)];

    /// <summary>
    /// Gets the global that <see cref="Install"/> created. It is <see langword="null"/> before the install.
    /// </summary>
    public LayerShell? Shell { get; private set; }

    /// <inheritdoc/>
    public IDisposable Install(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Shell = new LayerShell(services.Display, services.Require<CompositorGlobal>());
        services.Use(Shell);
        services.Require<ILayerShellDriver>().Attach(Shell);
        var directory = new LayerDirectory(Shell);
        services.UseDefault<ILayerDirectory>(directory);
        return new LayerHandle(Shell, directory);
    }

    private sealed class LayerHandle(LayerShell shell, LayerDirectory directory) : IDisposable
    {
        public void Dispose()
        {
            directory.Dispose();
            shell.Dispose();
        }
    }
}
