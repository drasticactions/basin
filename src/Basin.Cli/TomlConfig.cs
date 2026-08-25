using Basin.Diagnostics;
using Tomlyn;
using Tomlyn.Model;

namespace Basin.Cli;

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
        ArgumentException.ThrowIfNullOrEmpty(path);

        string text;
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            text = File.ReadAllText(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            log.Warn($"cannot read {path}: {error.Message}");
            return null;
        }

        try
        {
            return Toml.ToModel(text);
        }
        catch (TomlException error)
        {
            log.Warn($"{path} did not parse, keeping defaults: {error.Message}");
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
