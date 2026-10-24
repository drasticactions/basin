using System.Runtime.InteropServices;

namespace Basin.Greetd;

/// <summary>
/// Prepares the environment of the <c>greeter</c> user. Each variable is set in the managed environment and through libc <c>setenv</c>, so native libraries see it too.
/// </summary>
public static partial class GreeterEnvironment
{
    /// <summary>
    /// Sets <c>DBUS_SESSION_BUS_ADDRESS</c> to <c>$XDG_RUNTIME_DIR/bus</c> when that socket exists and no address is set. Call it before the toolkit starts.
    /// </summary>
    public static void PrepareSessionBus()
    {
        if (Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS") is { Length: > 0 } ||
            Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is not { Length: > 0 } runtime)
        {
            return;
        }

        var socket = Path.Combine(runtime, "bus");
        if (File.Exists(socket))
        {
            Set("DBUS_SESSION_BUS_ADDRESS", "unix:path=" + socket);
        }
    }

    /// <summary>
    /// Sets <c>XDG_CACHE_HOME</c>, <c>XDG_CONFIG_HOME</c> and <c>XDG_DATA_HOME</c> under <paramref name="stateDirectory"/> when <c>HOME</c> is not writable. It keeps a variable that is already set and creates each directory.
    /// </summary>
    /// <param name="stateDirectory">A writable directory for the greeter's state. <see langword="null"/>, empty or not writable does nothing.</param>
    public static void PrepareDirectories(string? stateDirectory)
    {
        if (Environment.GetEnvironmentVariable("HOME") is { Length: > 0 } home && Writable(home))
        {
            return;
        }

        if (stateDirectory is { Length: > 0 } directory && Writable(directory))
        {
            foreach (var (variable, name) in new[] { ("XDG_CACHE_HOME", "cache"), ("XDG_CONFIG_HOME", "config"), ("XDG_DATA_HOME", "data") })
            {
                if (Environment.GetEnvironmentVariable(variable) is not { Length: > 0 })
                {
                    var path = Path.Combine(directory, name);
                    Directory.CreateDirectory(path);
                    Set(variable, path);
                }
            }
        }
    }

    private static void Set(string variable, string value)
    {
        Environment.SetEnvironmentVariable(variable, value);
        _ = SetNative(variable, value, 1);
    }

    private static bool Writable(string directory) => Directory.Exists(directory) && Access(directory, 2) == 0;

    [LibraryImport("libc", EntryPoint = "setenv", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int SetNative(string name, string value, int overwrite);

    [LibraryImport("libc", EntryPoint = "access", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Access(string path, int mode);
}
