using Basin.Backend.Hosted;
using Basin.Desktop;
using Basin.Diagnostics;
using Basin.Shell.Xdg;
using Wayland.Server;

namespace Basin.Hosted;

public sealed record BasinCompositorOptions
{
    public string? AppName { get; init; }

    public string? SocketName { get; init; }

    public bool ManagedTransport { get; init; } = !PlatformFacts.HasLocalClients || !OperatingSystem.IsLinux();

    public Capabilities.ITextInputMethod? TextInput { get; init; }

    public IReadOnlyList<IProtocolModule>? ExtraModules { get; init; }

    public Action<BasinCompositorHost, BasinServices>? ConfigureServices { get; init; }
}
