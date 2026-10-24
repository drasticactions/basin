using System.Diagnostics;
using System.Runtime.InteropServices;
using Tmds.DBus.Protocol;
using Xunit;

namespace Basin.Testing;

/// <summary>
/// A private session bus with its own AT-SPI bus, for tests that read an accessibility tree.
/// It never touches the desktop's accessibility bus.
/// </summary>
public sealed partial class AccessibilityBus : IDisposable
{
    private static readonly string[] LauncherPaths =
    [
        "/usr/lib/at-spi-bus-launcher",
        "/usr/libexec/at-spi-bus-launcher",
        "/usr/lib/at-spi2-core/at-spi-bus-launcher",
        "/usr/libexec/at-spi2-core/at-spi-bus-launcher",
    ];

    private static AccessibilityBus? _process;
    private readonly Process _launcher;
    private DBusConnection? _listener;
    private bool _disposed;

    private AccessibilityBus(PrivateBus session, string runtime, Process launcher, string a11yAddress)
    {
        Session = session;
        Runtime = runtime;
        _launcher = launcher;
        A11yAddress = a11yAddress;
    }

    /// <summary>
    /// Gets the private session bus.
    /// </summary>
    public PrivateBus Session { get; }

    /// <summary>
    /// Gets the address of the private session bus.
    /// </summary>
    public string SessionAddress => Session.Address;

    /// <summary>
    /// Gets the address of the AT-SPI bus that the launcher started.
    /// </summary>
    public string A11yAddress { get; }

    /// <summary>
    /// Gets the private runtime directory that holds the AT-SPI bus socket.
    /// </summary>
    public string Runtime { get; }

    /// <summary>
    /// Gets a value indicating whether at-spi2-core and dbus-daemon are installed.
    /// </summary>
    public static bool IsAvailable => PrivateBus.IsAvailable && LauncherPaths.Any(File.Exists);

    /// <summary>
    /// Starts a session bus and an AT-SPI bus. The test is skipped when at-spi2-core or dbus-daemon is missing.
    /// The kit holds one AT-SPI event listener for the life of the buses, so the launcher never turns IsEnabled off when a test's own listener leaves.
    /// </summary>
    /// <param name="enabled">Sets the bus's IsEnabled property, as a running screen reader does.</param>
    /// <returns>The running buses.</returns>
    public static async Task<AccessibilityBus> StartAsync(bool enabled = true)
    {
        var launcherPath = LauncherPaths.FirstOrDefault(File.Exists);
        Assert.SkipWhen(launcherPath is null, "at-spi2-core is not installed");
        var session = PrivateBus.Start();
        var runtime = Directory.CreateTempSubdirectory("basin-a11y-").FullName;
        File.SetUnixFileMode(runtime, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var info = new ProcessStartInfo(launcherPath!) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add("--launch-immediately");
        info.Environment["DBUS_SESSION_BUS_ADDRESS"] = session.Address;
        info.Environment["XDG_RUNTIME_DIR"] = runtime;
        info.Environment["ATSPI_DBUS_IMPLEMENTATION"] = "dbus-daemon";
        info.Environment["GIO_USE_VFS"] = "local";
        info.Environment["GSETTINGS_BACKEND"] = "memory";
        info.Environment["NO_AT_BRIDGE"] = "1";
        info.Environment.Remove("DISPLAY");
        info.Environment.Remove("WAYLAND_DISPLAY");
        info.Environment.Remove("AT_SPI_BUS_ADDRESS");
        var launcher = Process.Start(info) ?? throw new InvalidOperationException("at-spi-bus-launcher did not start");
        launcher.OutputDataReceived += static (_, _) => { };
        launcher.ErrorDataReceived += static (_, _) => { };
        launcher.BeginOutputReadLine();
        launcher.BeginErrorReadLine();
        try
        {
            var address = await WaitForAddressAsync(session.Address).ConfigureAwait(false);
            var bus = new AccessibilityBus(session, runtime, launcher, address);
            await bus.HoldListenerAsync().ConfigureAwait(false);
            if (enabled)
            {
                await bus.SetEnabledAsync(true).ConfigureAwait(false);
            }

            return bus;
        }
        catch
        {
            Stop(launcher);
            session.Dispose();
            Directory.Delete(runtime, recursive: true);
            throw;
        }
    }

    /// <summary>
    /// Starts one pair of buses for the whole test process and points this process's session bus at it.
    /// AccessKit reads the session bus address once per process, so in-process adapters need this.
    /// </summary>
    /// <returns>The shared buses. They stop when the process exits.</returns>
    public static async Task<AccessibilityBus> ForProcessAsync()
    {
        if (_process is { } running)
        {
            return running;
        }

        var bus = await StartAsync().ConfigureAwait(false);
        SetEnvironment("DBUS_SESSION_BUS_ADDRESS", bus.SessionAddress);
        SetEnvironment("AT_SPI_BUS_ADDRESS", bus.A11yAddress);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => bus.Dispose();
        _process = bus;
        return bus;
    }

    /// <summary>
    /// Points a child process's environment at these buses. AT_SPI_BUS_ADDRESS is set too, because toolkits read it before they ask the session bus.
    /// </summary>
    /// <param name="environment">The environment to change.</param>
    /// <returns>The same dictionary.</returns>
    public Dictionary<string, string?> Apply(Dictionary<string, string?> environment)
    {
        environment["DBUS_SESSION_BUS_ADDRESS"] = SessionAddress;
        environment["AT_SPI_BUS_ADDRESS"] = A11yAddress;
        environment["XDG_RUNTIME_DIR"] = Runtime;
        return environment;
    }

    /// <summary>
    /// Points a child process's start info at these buses, AT_SPI_BUS_ADDRESS included.
    /// </summary>
    /// <param name="info">The start info to change.</param>
    /// <returns>The same start info.</returns>
    public ProcessStartInfo Apply(ProcessStartInfo info)
    {
        info.Environment["DBUS_SESSION_BUS_ADDRESS"] = SessionAddress;
        info.Environment["AT_SPI_BUS_ADDRESS"] = A11yAddress;
        info.Environment["XDG_RUNTIME_DIR"] = Runtime;
        return info;
    }

    /// <summary>
    /// Connects a screen-reader-like client to the AT-SPI bus.
    /// </summary>
    /// <returns>The connected client.</returns>
    public Task<AtSpiClient> ConnectAsync() => AtSpiClient.ConnectAsync(A11yAddress);

    /// <summary>
    /// Sets org.a11y.Status.IsEnabled, which toolkits read to decide if they expose a tree.
    /// </summary>
    /// <param name="enabled">The new value.</param>
    /// <returns>A task that completes when the bus has the value.</returns>
    public Task SetEnabledAsync(bool enabled) => Task.Run(async () =>
    {
        using var session = new DBusConnection(SessionAddress);
        await session.ConnectAsync().ConfigureAwait(false);
        await session.CallMethodAsync(SetEnabled(session, enabled)).ConfigureAwait(false);
    });

    private async Task HoldListenerAsync()
    {
        var listener = new DBusConnection(A11yAddress);
        try
        {
            await listener.ConnectAsync().ConfigureAwait(false);
            await listener.CallMethodAsync(RegisterListener(listener)).ConfigureAwait(false);
            _listener = listener;
        }
        catch
        {
            listener.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Stops the AT-SPI bus and the session bus and deletes the runtime directory.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _listener?.Dispose();
        _listener = null;
        Stop(_launcher);
        Session.Dispose();
        try
        {
            Directory.Delete(Runtime, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [LibraryImport("libc", EntryPoint = "setenv", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int SetNativeEnvironment(string name, string value, int overwrite);

    private static void SetEnvironment(string name, string value)
    {
        Environment.SetEnvironmentVariable(name, value);
        _ = SetNativeEnvironment(name, value, 1);
    }

    private static void Stop(Process launcher)
    {
        try
        {
            if (!launcher.HasExited)
            {
                launcher.Kill(entireProcessTree: true);
                launcher.WaitForExit(2000);
            }
        }
        catch (InvalidOperationException)
        {
        }

        launcher.Dispose();
    }

    private static async Task<string> WaitForAddressAsync(string sessionAddress)
    {
        using var session = new DBusConnection(sessionAddress);
        await session.ConnectAsync().ConfigureAwait(false);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (true)
        {
            try
            {
                var address = await session.CallMethodAsync(GetAddress(session), static (m, _) => m.GetBodyReader().ReadString(), null).ConfigureAwait(false);
                if (address.StartsWith("unix:", StringComparison.Ordinal))
                {
                    return address;
                }
            }
            catch (DBusErrorReplyException) when (DateTime.UtcNow < deadline)
            {
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new InvalidOperationException("at-spi-bus-launcher started no accessibility bus");
            }

            await Task.Delay(50).ConfigureAwait(false);
        }
    }

    private static MessageBuffer GetAddress(DBusConnection bus)
    {
        using var writer = bus.GetMessageWriter();
        writer.WriteMethodCallHeader("org.a11y.Bus", "/org/a11y/bus", "org.a11y.Bus", "GetAddress");
        return writer.CreateMessage();
    }

    private static MessageBuffer RegisterListener(DBusConnection bus)
    {
        using var writer = bus.GetMessageWriter();
        writer.WriteMethodCallHeader("org.a11y.atspi.Registry", "/org/a11y/atspi/registry", "org.a11y.atspi.Registry", "RegisterEvent", "sass");
        writer.WriteString("object:children-changed");
        writer.WriteArray(Array.Empty<string>());
        writer.WriteString("");
        return writer.CreateMessage();
    }

    private static MessageBuffer SetEnabled(DBusConnection bus, bool enabled)
    {
        using var writer = bus.GetMessageWriter();
        writer.WriteMethodCallHeader("org.a11y.Bus", "/org/a11y/bus", "org.freedesktop.DBus.Properties", "Set", "ssv");
        writer.WriteString("org.a11y.Status");
        writer.WriteString("IsEnabled");
        writer.WriteVariantBool(enabled);
        return writer.CreateMessage();
    }
}
