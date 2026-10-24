using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Basin.Freedesktop;

namespace TabWm;

internal static class StateFiles
{
    public static string Directory => Path.Combine(XdgDirectories.StateHome, "tab-wm");

    public static string LauncherPath => Path.Combine(Directory, "launcher.json");

    public static string SessionPath => Path.Combine(Directory, "session.json");

    public static T? Read<T>(string path, JsonTypeInfo<T> type, out string? error)
        where T : class
    {
        error = null;
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize(stream, type);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or JsonException)
        {
            error = $"{path}: {failure.Message}";
            return null;
        }
    }

    public static bool Write<T>(string path, T value, JsonTypeInfo<T> type, out string? error)
    {
        error = null;
        var temp = path + ".tmp";
        try
        {
            if (Path.GetDirectoryName(path) is { Length: > 0 } directory)
            {
                System.IO.Directory.CreateDirectory(directory);
            }

            using (var stream = File.Create(temp))
            {
                JsonSerializer.Serialize(stream, value, type);
            }

            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            error = $"{path}: {failure.Message}";
            return false;
        }
    }
}
