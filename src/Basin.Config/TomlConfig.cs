using Basin.Diagnostics;
using Tomlyn;
using Tomlyn.Model;

namespace Basin.Config;

/// <summary>
/// Finds, seeds and reads the TOML configuration file of a program.
/// </summary>
public static class TomlConfig
{
    /// <summary>
    /// Gives the default configuration path: <c>$XDG_CONFIG_HOME/NAME/NAME.toml</c>, or <c>~/.config/NAME/NAME.toml</c> when the variable is unset or relative.
    /// </summary>
    /// <param name="name">The program name.</param>
    /// <returns>The absolute path of the configuration file.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is <see langword="null"/> or empty.</exception>
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

    /// <summary>
    /// Loads the configuration of a program. A file at the default path that is missing is first written from the template.
    /// A named file that does not load sets <see cref="TomlLoad.Fatal"/>. A default file that does not parse only gives a warning.
    /// </summary>
    /// <param name="namedPath">The path that the user gave, or <see langword="null"/> or empty for the default path.</param>
    /// <param name="name">The program name for the default path.</param>
    /// <param name="none">If <see langword="true"/>, no file is read and the result is empty.</param>
    /// <param name="template">Opens the default file contents, or <see langword="null"/> to write no default file.</param>
    /// <param name="log">The logger for warnings.</param>
    /// <returns>The loaded table, the path that was tried and a fatal error, if any.</returns>
    public static TomlLoad Load(string? namedPath, string name, bool none, Func<Stream>? template, BasinLogger log)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (none)
        {
            return default;
        }

        var named = namedPath is { Length: > 0 };
        var file = named ? namedPath! : DefaultPath(name);
        if (!named && template is not null && !File.Exists(file))
        {
            using var stream = template();
            Seed(file, stream, log);
        }

        var table = ReadFile(file, out var failure, out var error);
        if (table is not null)
        {
            return new TomlLoad(table, file, null);
        }

        if (named)
        {
            return new TomlLoad(null, file, $"{file}: {failure}");
        }

        WarnUnread(file, error, log);
        return new TomlLoad(null, file, null);
    }

    /// <summary>
    /// Writes the template to a file that does not exist yet and creates its directory. An existing file is never replaced.
    /// </summary>
    /// <param name="file">The path to write.</param>
    /// <param name="template">The default file contents.</param>
    /// <param name="log">The logger for the result.</param>
    /// <returns><see langword="false"/> when the file exists or cannot be written.</returns>
    public static bool Seed(string file, Stream template, BasinLogger log)
    {
        ArgumentException.ThrowIfNullOrEmpty(file);
        ArgumentNullException.ThrowIfNull(template);
        try
        {
            if (Path.GetDirectoryName(file) is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
            }

            using (var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write))
            {
                template.CopyTo(stream);
            }

            log.Info($"wrote the default configuration to {file}");
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            log.Warn(error, $"cannot write {file}, keeping the built-in defaults");
            return false;
        }
    }

    /// <summary>
    /// Reads and parses a TOML file. It warns when the file exists but cannot be read or does not parse.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="log">The logger for warnings.</param>
    /// <returns>The table, or <see langword="null"/> when the file is missing, cannot be read or does not parse.</returns>
    public static TomlTable? Read(string path, BasinLogger log)
    {
        var table = ReadFile(path, out _, out var error);
        WarnUnread(path, error, log);
        return table;
    }

    /// <summary>
    /// Reads and parses a TOML file without logging.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="failure">The reason for a <see langword="null"/> result, else <see langword="null"/>.</param>
    /// <returns>The table, or <see langword="null"/> when the file is missing, cannot be read or does not parse.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is <see langword="null"/> or empty.</exception>
    public static TomlTable? Read(string path, out string? failure) => ReadFile(path, out failure, out _);

    private static TomlTable? ReadFile(string path, out string? failure, out Exception? error)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        error = null;
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error = exception;
            failure = OneLine(exception.Message);
            return null;
        }

        try
        {
            failure = null;
            return Toml.ToModel(text);
        }
        catch (TomlException exception)
        {
            error = exception;
            failure = OneLine(exception.Message);
            return null;
        }
    }

    private static void WarnUnread(string path, Exception? error, BasinLogger log)
    {
        switch (error)
        {
            case TomlException:
                log.Warn(error, $"{path} did not parse, keeping defaults");
                break;
            case not null:
                log.Warn(error, $"{path} cannot be read, keeping defaults");
                break;
        }
    }

    private static string OneLine(string text) =>
        string.Join(' ', text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    /// <summary>
    /// Reads a boolean key.
    /// </summary>
    /// <param name="table">The table to read.</param>
    /// <param name="key">The key.</param>
    /// <param name="fallback">The value when the key is missing or not a boolean.</param>
    /// <returns>The value of the key, or <paramref name="fallback"/>.</returns>
    public static bool Flag(TomlTable table, string key, bool fallback)
    {
        ArgumentNullException.ThrowIfNull(table);
        return table.TryGetValue(key, out var value) && value is bool flag ? flag : fallback;
    }

    /// <summary>
    /// Reads a string key.
    /// </summary>
    /// <param name="table">The table to read.</param>
    /// <param name="key">The key.</param>
    /// <returns>The string, or <see langword="null"/> when the key is missing, empty or not a string.</returns>
    public static string? Text(TomlTable table, string key)
    {
        ArgumentNullException.ThrowIfNull(table);
        return table.TryGetValue(key, out var value) && value is string text && text.Length > 0 ? text : null;
    }

    /// <summary>
    /// Reads an integer key. A value outside the <see cref="int"/> range is truncated.
    /// </summary>
    /// <param name="table">The table to read.</param>
    /// <param name="key">The key.</param>
    /// <param name="fallback">The value when the key is missing or not an integer.</param>
    /// <returns>The value of the key, or <paramref name="fallback"/>.</returns>
    public static int Number(TomlTable table, string key, int fallback)
    {
        ArgumentNullException.ThrowIfNull(table);
        return table.TryGetValue(key, out var value) && value is long number ? (int)number : fallback;
    }

    /// <summary>
    /// Reads a float or integer key as a <see cref="double"/>.
    /// </summary>
    /// <param name="table">The table to read.</param>
    /// <param name="key">The key.</param>
    /// <param name="fallback">The value when the key is missing or not a number.</param>
    /// <returns>The value of the key, or <paramref name="fallback"/>.</returns>
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
