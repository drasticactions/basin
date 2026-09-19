using System.Runtime.Versioning;

namespace Basin;

public static class PlatformFacts
{
    [UnsupportedOSPlatformGuard("windows")]
    [UnsupportedOSPlatformGuard("browser")]
    public static bool HasDescriptors { get; } = !OperatingSystem.IsWindows() && !OperatingSystem.IsBrowser();

    [SupportedOSPlatformGuard("linux")]
    [SupportedOSPlatformGuard("macos")]
    [SupportedOSPlatformGuard("freebsd")]
    public static bool HasLocalClients { get; } = OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD();

    [UnsupportedOSPlatformGuard("browser")]
    public static bool HasThreads { get; } = !OperatingSystem.IsBrowser();

    [UnsupportedOSPlatformGuard("browser")]
    [UnsupportedOSPlatformGuard("ios")]
    public static bool HasChildProcesses { get; } = !OperatingSystem.IsBrowser() && !OperatingSystem.IsIOS();

    public static bool HasSystemXkbData { get; } = OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD();

    public static bool HasHostKeyboardLayout { get; } = OperatingSystem.IsMacOS() || OperatingSystem.IsWindows();

    [SupportedOSPlatformGuard("linux")]
    public static bool HasSyncobj { get; } = OperatingSystem.IsLinux();

    [SupportedOSPlatformGuard("linux")]
    [SupportedOSPlatformGuard("macos")]
    [SupportedOSPlatformGuard("freebsd")]
    public static bool HasPosixSignals { get; } = OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD();

    [SupportedOSPlatformGuard("linux")]
    [SupportedOSPlatformGuard("android")]
    public static bool HasLinuxSyscalls { get; } = OperatingSystem.IsLinux() || OperatingSystem.IsAndroid();

    [SupportedOSPlatformGuard("macos")]
    [SupportedOSPlatformGuard("ios")]
    [SupportedOSPlatformGuard("maccatalyst")]
    [SupportedOSPlatformGuard("tvos")]
    public static bool HasDarwinSyscalls { get; } =
        OperatingSystem.IsMacOS() || OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst() || OperatingSystem.IsTvOS();
}
