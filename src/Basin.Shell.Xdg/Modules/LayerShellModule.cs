using Basin.Capabilities;

namespace Basin.Shell.Xdg;

public sealed class LayerShellModule : IProtocolModule
{
    public string WireInterface => "zwlr_layer_shell_v1";

    public int Version => LayerShell.Version;

    public LayerShell? Shell { get; private set; }

    public IDisposable Install(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Shell = new LayerShell(services.Display, services.Require<CompositorGlobal>());
        services.Use(Shell);
        return Shell;
    }
}
