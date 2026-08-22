using Basin.Backend.Hosted;
using Basin.Desktop;
using Basin.Diagnostics;
using Basin.Shell.Xdg;
using Wayland.Server;

namespace Basin.Avalonia;

public sealed record BasinCompositorOptions
{
    public string? AppName { get; init; }

    public string? SocketName { get; init; }

    public bool ManagedTransport { get; init; } = !OperatingSystem.IsLinux();

    public AvaloniaTextInput? TextInput { get; init; }

    public IReadOnlyList<IProtocolModule>? ExtraModules { get; init; }
}
