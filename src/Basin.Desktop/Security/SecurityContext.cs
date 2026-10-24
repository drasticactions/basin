using System.Runtime.InteropServices;
using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

/// <summary>
/// Holds the sandbox metadata that a sandbox engine attached to the clients of one security context listener.
/// </summary>
/// <param name="SandboxEngine">The reverse-DNS name of the sandbox engine, for example <c>org.flatpak</c>, or <see langword="null"/>.</param>
/// <param name="AppId">The application id inside the sandbox, or <see langword="null"/>.</param>
/// <param name="InstanceId">The id of the running sandbox instance, or <see langword="null"/>.</param>
public sealed record SecurityContext(string? SandboxEngine, string? AppId, string? InstanceId);
