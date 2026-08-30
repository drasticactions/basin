using Basin.Diagnostics;
using Tomlyn;
using Tomlyn.Model;

namespace Basin.Config;

public static class TomlConfig
{
    public static string DefaultPath(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrEmpty(configHome) || !Path.IsPathRooted(configHome))
        {
            configHome = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        }

        return Path.Combine(configHome, name, name + ".toml");
    }

    public static TomlTable? Read(string path, BasinLogger log)
    {
        var table = Read(path, out var failure);
        if (failure is not null && File.Exists(path))
        {
            log.Warn($"{path} did not parse, keeping defaults: {failure}");
        }

        return table;
    }

    public static TomlTable? Read(string path, out string? failure)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        string text;
        try
        {
            if (!File.Exists(path))
            {
                failure = "no such file";
                return null;
            }

            text = File.ReadAllText(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            failure = error.Message;
            return null;
        }

        try
        {
            failure = null;
            return Toml.ToModel(text);
        }
        catch (TomlException error)
        {
            failure = error.Message;
            return null;
        }
    }

    public static bool Flag(TomlTable table, string key, bool fallback)
    {
        ArgumentNullException.ThrowIfNull(table);
        return table.TryGetValue(key, out var value) && value is bool flag ? flag : fallback;
    }

    public static string? Text(TomlTable table, string key)
    {
        ArgumentNullException.ThrowIfNull(table);
        return table.TryGetValue(key, out var value) && value is string text && text.Length > 0 ? text : null;
    }

    public static int Number(TomlTable table, string key, int fallback)
    {
        ArgumentNullException.ThrowIfNull(table);
        return table.TryGetValue(key, out var value) && value is long number ? (int)number : fallback;
    }

    public static double Number(TomlTable table, string key, double fallback)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (!table.TryGetValue(key, out var value))
        {
            return fallback;
        }

        return value switch
        {
            double number => number,
            long integer => integer,
            _ => fallback,
        };
    }
}
