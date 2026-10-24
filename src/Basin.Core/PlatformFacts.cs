using System.Runtime.Versioning;

namespace Basin;

/// <summary>
/// Static facts about the platform that the process runs on. The analyzer reads each fact as a platform guard.
/// </summary>
public static class PlatformFacts
{
    /// <summary>
    /// Gets a value indicating whether the platform has POSIX file descriptors. It is false on Windows and in the browser.
    /// </summary>
    [UnsupportedOSPlatformGuard("windows")]
    [UnsupportedOSPlatformGuard("browser")]
    public static bool HasDescriptors { get; } = !OperatingSystem.IsWindows() && !OperatingSystem.IsBrowser();

    /// <summary>
    /// Gets a value indicating whether local Wayland clients can connect over a Unix socket. It is true on Linux, macOS and FreeBSD.
    /// </summary>
    [SupportedOSPlatformGuard("linux")]
    [SupportedOSPlatformGuard("macos")]
    [SupportedOSPlatformGuard("freebsd")]
    public static bool HasLocalClients { get; } = OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD();

    /// <summary>
    /// Gets a value indicating whether the process can start threads. It is false in the browser.
    /// </summary>
    [UnsupportedOSPlatformGuard("browser")]
    public static bool HasThreads { get; } = !OperatingSystem.IsBrowser();

    /// <summary>
    /// Gets a value indicating whether the process can start child processes. It is false in the browser and on iOS.
    /// </summary>
    [UnsupportedOSPlatformGuard("browser")]
    [UnsupportedOSPlatformGuard("ios")]
    public static bool HasChildProcesses { get; } = !OperatingSystem.IsBrowser() && !OperatingSystem.IsIOS();

    /// <summary>
    /// Gets a value indicating whether the system supplies xkeyboard-config data. It is true on Linux and FreeBSD.
    /// </summary>
    public static bool HasSystemXkbData { get; } = OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD();

    /// <summary>
    /// Gets a value indicating whether the host operating system supplies the keyboard layout. It is true on macOS and Windows.
    /// </summary>
    public static bool HasHostKeyboardLayout { get; } = OperatingSystem.IsMacOS() || OperatingSystem.IsWindows();

    /// <summary>
    /// Gets a value indicating whether DRM sync objects can be available. It is true on Linux only.
    /// </summary>
    [SupportedOSPlatformGuard("linux")]
    public static bool HasSyncobj { get; } = OperatingSystem.IsLinux();

    /// <summary>
    /// Gets a value indicating whether the process can handle POSIX signals. It is true on Linux, macOS and FreeBSD.
    /// </summary>
    [SupportedOSPlatformGuard("linux")]
    [SupportedOSPlatformGuard("macos")]
    [SupportedOSPlatformGuard("freebsd")]
    public static bool HasPosixSignals { get; } = OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD();

    /// <summary>
    /// Gets a value indicating whether Linux system calls are available. It is true on Linux and Android.
    /// </summary>
    [SupportedOSPlatformGuard("linux")]
    [SupportedOSPlatformGuard("android")]
    public static bool HasLinuxSyscalls { get; } = OperatingSystem.IsLinux() || OperatingSystem.IsAndroid();

    /// <summary>
    /// Gets a value indicating whether Darwin system calls are available. It is true on macOS, iOS, Mac Catalyst and tvOS.
    /// </summary>
    [SupportedOSPlatformGuard("macos")]
    [SupportedOSPlatformGuard("ios")]
    [SupportedOSPlatformGuard("maccatalyst")]
    [SupportedOSPlatformGuard("tvos")]
    public static bool HasDarwinSyscalls { get; } =
        OperatingSystem.IsMacOS() || OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst() || OperatingSystem.IsTvOS();
}
