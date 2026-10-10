using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Basin.Transport.Waypipe;

[SupportedOSPlatform("windows")]
internal static class HyperVRegistry
{
    internal const string ServicesKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Virtualization\GuestCommunicationServices";

    internal static bool IsRegistered(Guid serviceId) => Exists($@"{ServicesKey}\{serviceId}");

    internal static bool IsRegisteredWithBraces(Guid serviceId) => Exists($@"{ServicesKey}\{{{serviceId}}}");

    private static bool Exists(string path)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(path);
            return key is not null;
        }
        catch (System.Security.SecurityException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
