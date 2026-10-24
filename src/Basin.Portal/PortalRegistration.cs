using System.Text;

namespace Basin.Portal;

/// <summary>
/// Writes the files that make <c>xdg-desktop-portal</c> and the D-Bus daemon find the basin backend.
/// </summary>
public static class PortalRegistration
{
    /// <summary>
    /// The desktop name in the <c>UseIn</c> key and in the config file name. <c>XDG_CURRENT_DESKTOP</c> must contain it.
    /// </summary>
    public const string DesktopName = "basin";

    /// <summary>
    /// The names of every interface that the backend can serve.
    /// </summary>
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

    /// <summary>
    /// Creates the text of the <c>.portal</c> file.
    /// </summary>
    /// <param name="interfaces">The interfaces to list.</param>
    /// <param name="busName">The bus name, or <see langword="null"/> for <see cref="PortalBus.DefaultBusName"/>.</param>
    /// <returns>The file text.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="interfaces"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Creates the text of the <c>basin-portals.conf</c> file, which prefers basin for each interface and gtk, kde or gnome for the rest.
    /// </summary>
    /// <param name="interfaces">The interfaces that basin serves.</param>
    /// <returns>The file text.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="interfaces"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Creates the text of the D-Bus service file that starts the backend on demand.
    /// </summary>
    /// <param name="executable">The command line of the backend.</param>
    /// <param name="busName">The bus name, or <see langword="null"/> for <see cref="PortalBus.DefaultBusName"/>.</param>
    /// <returns>The file text.</returns>
    /// <exception cref="ArgumentException"><paramref name="executable"/> is <see langword="null"/> or empty.</exception>
    public static string ServiceFile(string executable, string? busName = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(executable);
        var text = new StringBuilder();
        text.Append("[D-BUS Service]\n");
        text.Append("Name=").Append(busName ?? PortalBus.DefaultBusName).Append('\n');
        text.Append("Exec=").Append(executable).Append('\n');
        return text.ToString();
    }

    /// <summary>
    /// Gets the user data directory: <c>XDG_DATA_HOME</c>, else <c>~/.local/share</c>.
    /// </summary>
    /// <returns>The directory path.</returns>
    public static string DataHome()
    {
        var home = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrEmpty(home))
        {
            home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        }

        return home;
    }

    /// <summary>
    /// Writes the <c>.portal</c> file and the config file, and the service file when an executable is given. The files use the default bus name.
    /// </summary>
    /// <param name="interfaces">The interfaces that basin serves.</param>
    /// <param name="executable">The command line of the backend, or <see langword="null"/> to write no service file.</param>
    /// <param name="dataHome">The data directory, or <see langword="null"/> for <see cref="DataHome"/>.</param>
    /// <returns>The paths of the files that the call wrote.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="interfaces"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Runs <c>busctl --user call ... ReloadConfig</c> so that the D-Bus daemon reads new service files. The call waits up to 2 seconds.
    /// </summary>
    /// <returns><see langword="true"/> when <c>busctl</c> exited with code 0 in time.</returns>
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
