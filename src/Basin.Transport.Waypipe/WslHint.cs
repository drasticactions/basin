using System.Globalization;
using System.Runtime.Versioning;

namespace Basin.Transport.Waypipe;

[SupportedOSPlatform("linux")]
internal static class WslHint
{
    internal static bool InsideWsl() =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WSL_DISTRO_NAME")) ||
        File.Exists("/proc/sys/fs/binfmt_misc/WSLInterop") ||
        File.Exists("/proc/sys/fs/binfmt_misc/WSLInterop-late");

    internal static string Text(VsockEndPoint local, bool loopbackLoaded)
    {
        var modprobe = loopbackLoaded ? string.Empty : ", which needs 'sudo modprobe vsock_loopback'";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"inside WSL2: a client in this VM reaches vsock:{local.Port} over CID 1{modprobe}; Windows reaches it as VmId=<this VM> ServiceId={HyperVEndPoint.VsockServiceId(local.Port)}");
    }

    internal static void Log(VsockEndPoint local)
    {
        if (InsideWsl())
        {
            WaypipeLog.Log.Info($"{Text(local, Directory.Exists("/sys/module/vsock_loopback"))}");
        }
    }
}
