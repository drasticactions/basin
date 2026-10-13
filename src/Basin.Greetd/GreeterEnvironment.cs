using System.Runtime.InteropServices;

namespace Basin.Greetd;

public static partial class GreeterEnvironment
{
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
