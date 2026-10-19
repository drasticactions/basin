using Basin.Capabilities;

namespace Basin.Shell.Xdg;

public sealed class LayerShellModule : IProtocolModule
{
    public string WireInterface => "zwlr_layer_shell_v1";

    public int Version => LayerShell.Version;

    public IReadOnlyList<Type> Drivers => [typeof(ILayerShellDriver)];

    public LayerShell? Shell { get; private set; }

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
