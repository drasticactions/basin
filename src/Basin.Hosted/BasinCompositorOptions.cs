using Basin.Backend.Hosted;
using Basin.Desktop;
using Basin.Diagnostics;
using Basin.Shell.Xdg;
using Wayland.Server;

namespace Basin.Hosted;

/// <summary>
/// The choices that a <see cref="BasinCompositorHost"/> reads once in its constructor.
/// A change after construction has no effect.
/// </summary>
public sealed record BasinCompositorOptions
{
    /// <summary>
    /// Gets the application name that the desktop protocol pack receives. It is <see langword="null"/> when the pack uses its own default.
    /// </summary>
    public string? AppName { get; init; }

    /// <summary>
    /// Gets the name of the local socket to bind. When it is <see langword="null"/>, the host picks the first free <c>wayland-N</c> name.
    /// </summary>
    public string? SocketName { get; init; }

    /// <summary>
    /// Gets a value indicating whether the display uses the managed wire transport instead of libwayland.
    /// It is <see langword="true"/> by default off Linux and on a platform with no local clients.
    /// </summary>
    public bool ManagedTransport { get; init; } = !PlatformFacts.HasLocalClients || !OperatingSystem.IsLinux();

    /// <summary>
    /// Gets the input method that the host registers as <see cref="Capabilities.ITextInputMethod"/>. When it is <see langword="null"/>, the host registers none.
    /// </summary>
    public Capabilities.ITextInputMethod? TextInput { get; init; }

    /// <summary>
    /// Gets the protocol modules that the host installs after the desktop pack and before <see cref="BasinServices.Freeze"/>.
    /// </summary>
    public IReadOnlyList<IProtocolModule>? ExtraModules { get; init; }

    /// <summary>
    /// Gets the callback that can change the service table before it freezes.
    /// The host properties <see cref="BasinCompositorHost.Services"/>, <see cref="BasinCompositorHost.Shell"/> and <see cref="BasinCompositorHost.Seat"/> are not set while it runs.
    /// </summary>
    public Action<BasinCompositorHost, BasinServices>? ConfigureServices { get; init; }

    /// <summary>
    /// Gets a value indicating whether the host advertises linux-dmabuf when the renderer reports a usable import path. It is <see langword="true"/> by default.
    /// </summary>
    public bool Dmabuf { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether the host installs a <see cref="ColorCapabilityPack"/>.
    /// When it is <see langword="false"/>, the host removes <c>wp_color_manager_v1</c>.
    /// </summary>
    public bool ColorManagement { get; init; }
}
