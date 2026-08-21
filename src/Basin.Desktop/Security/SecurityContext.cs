using System.Runtime.InteropServices;
using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

public sealed record SecurityContext(string? SandboxEngine, string? AppId, string? InstanceId);
