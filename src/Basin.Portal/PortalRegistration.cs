using System.Text;

namespace Basin.Portal;

public static class PortalRegistration
{
    public const string DesktopName = "basin";

    public static readonly string[] AllInterfaces =
    [
        "org.freedesktop.impl.portal.ScreenCast",
        "org.freedesktop.impl.portal.RemoteDesktop",
        "org.freedesktop.impl.portal.Screenshot",
        "org.freedesktop.impl.portal.Clipboard",
        "org.freedesktop.impl.portal.InputCapture",
        "org.freedesktop.impl.portal.GlobalShortcuts",
        "org.freedesktop.impl.portal.Access",
    ];

    public static string PortalFile(IReadOnlyList<string> interfaces, string? busName = null)
    {
        ArgumentNullException.ThrowIfNull(interfaces);
        var text = new StringBuilder();
        text.Append("[portal]\n");
        text.Append("DBusName=").Append(busName ?? PortalBus.DefaultBusName).Append('\n');
        text.Append("Interfaces=");
        foreach (var name in interfaces)
        {
            text.Append(name).Append(';');
        }

        text.Append('\n');
        text.Append("UseIn=").Append(DesktopName).Append('\n');
        return text.ToString();
    }

    public static string ConfigFile(IReadOnlyList<string> interfaces)
    {
        ArgumentNullException.ThrowIfNull(interfaces);
        var text = new StringBuilder();
        text.Append("[preferred]\n");
        text.Append("default=gtk;kde;gnome;\n");
        foreach (var name in interfaces)
        {
            text.Append(name).Append('=').Append(DesktopName).Append(";\n");
        }

        return text.ToString();
    }

    public static string ServiceFile(string executable, string? busName = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(executable);
        var text = new StringBuilder();
        text.Append("[D-BUS Service]\n");
        text.Append("Name=").Append(busName ?? PortalBus.DefaultBusName).Append('\n');
        text.Append("Exec=").Append(executable).Append('\n');
        return text.ToString();
    }

    public static string DataHome()
    {
        var home = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrEmpty(home))
        {
            home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        }

        return home;
    }

    public static (string PortalPath, string ConfigPath, string? ServicePath) Write(IReadOnlyList<string> interfaces, string? executable = null, string? dataHome = null)
    {
        ArgumentNullException.ThrowIfNull(interfaces);
        var home = dataHome ?? DataHome();
        var root = Path.Combine(home, "xdg-desktop-portal");
        var portalsDirectory = Path.Combine(root, "portals");
        Directory.CreateDirectory(portalsDirectory);
        var portalPath = Path.Combine(portalsDirectory, DesktopName + ".portal");
        var configPath = Path.Combine(root, DesktopName + "-portals.conf");
        File.WriteAllText(portalPath, PortalFile(interfaces));
        File.WriteAllText(configPath, ConfigFile(interfaces));
        string? servicePath = null;
        if (!string.IsNullOrEmpty(executable))
        {
            var servicesDirectory = Path.Combine(home, "dbus-1", "services");
            Directory.CreateDirectory(servicesDirectory);
            servicePath = Path.Combine(servicesDirectory, PortalBus.DefaultBusName + ".service");
            File.WriteAllText(servicePath, ServiceFile(executable));
        }

        return (portalPath, configPath, servicePath);
    }

    public static bool ReloadBus()
    {
        try
        {
            var info = new System.Diagnostics.ProcessStartInfo("busctl")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var argument in new[] { "--user", "call", "org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "ReloadConfig" })
            {
                info.ArgumentList.Add(argument);
            }

            using var reload = System.Diagnostics.Process.Start(info);
            if (reload is null)
            {
                return false;
            }

            reload.WaitForExit(2000);
            return reload.HasExited && reload.ExitCode == 0;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
