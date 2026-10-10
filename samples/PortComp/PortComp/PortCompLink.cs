using Basin;

namespace PortComp;

public static class PortCompLink
{
    public static string DefaultEndpoint => PlatformFacts.HasDescriptors ? "127.0.0.1:9800" : "ws://127.0.0.1:9801";

    public static string DefaultListenEndpoint =>
        OperatingSystem.IsWindows() || (OperatingSystem.IsLinux() && File.Exists("/dev/vsock")) ? "vsock:9800" : "127.0.0.1:9800";

    public static bool CanListen => OperatingSystem.IsWindows() || PlatformFacts.HasDescriptors;

    public static string? GuestCommand(string endpoint)
    {
        if (!endpoint.StartsWith("vsock:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var port = endpoint["vsock:".Length..];
        return OperatingSystem.IsWindows()
            ? $"wsl -- waypipe --vsock -s {port} server -- foot"
            : $"waypipe --vsock -s 1:{port} server -- foot";
    }
}
