using Basin.Config;
using Basin.Diagnostics;
using Tomlyn.Model;

namespace Basin.Greetd;

/// <summary>
/// Remembers the last user, and the session, layout and other values of each user, in a TOML file. A failed write logs one warning and the greeter continues.
/// </summary>
public sealed class GreeterMemory
{
    private readonly Dictionary<string, (string? Session, string? Layout)> _users = new(StringComparer.Ordinal);
    private readonly Dictionary<(string User, string Key), string> _values = [];
    private readonly BasinLogger _log;
    private bool _warned;

    private GreeterMemory(string path, BasinLogger log)
    {
        Path = path;
        _log = log;
    }

    /// <summary>
    /// Gets the path of the state file.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets the user of the last successful login, or <see langword="null"/> when none is remembered.
    /// </summary>
    public string? LastUser { get; private set; }

    /// <summary>
    /// Gives the default state file: <c>state.toml</c> in the first directory of <c>STATE_DIRECTORY</c>, or in <c>/var/lib/PROGRAM</c>.
    /// </summary>
    /// <param name="program">The program name for the fallback directory.</param>
    /// <returns>The path of the state file. The directory can be absent.</returns>
    /// <exception cref="ArgumentException"><paramref name="program"/> is null or empty.</exception>
    public static string DefaultPath(string program)
    {
        ArgumentException.ThrowIfNullOrEmpty(program);
        var directory = Environment.GetEnvironmentVariable("STATE_DIRECTORY") is { Length: > 0 } systemd
            ? systemd.Split(':')[0]
            : System.IO.Path.Combine("/var/lib", program);
        return System.IO.Path.Combine(directory, "state.toml");
    }

    /// <summary>
    /// Reads the state file. A missing file gives an empty memory. A file that does not parse logs a warning and gives an empty memory.
    /// </summary>
    /// <param name="path">The state file. The memory writes back to the same path.</param>
    /// <param name="log">The logger for read and write failures.</param>
    /// <returns>The memory, never <see langword="null"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null or empty.</exception>
    public static GreeterMemory Load(string path, BasinLogger log)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var memory = new GreeterMemory(path, log);
        if (!File.Exists(path))
        {
            return memory;
        }

        if (TomlConfig.Read(path, out var failure) is not { } table)
        {
            log.Warn($"{path}: {failure}, nothing is remembered from it");
            return memory;
        }

        memory.LastUser = table.TryGetValue("last_user", out var last) ? last as string : null;
        if (table.TryGetValue("users", out var users) && users is TomlTable byUser)
        {
            foreach (var (name, value) in byUser)
            {
                if (value is TomlTable user)
                {
                    memory._users[name] = (
                        user.TryGetValue("session", out var session) ? session as string : null,
                        user.TryGetValue("layout", out var layout) ? layout as string : null);
                    foreach (var (key, entry) in user)
                    {
                        if (key is not "session" and not "layout" && entry is string text)
                        {
                            memory._values[(name, key)] = text;
                        }
                    }
                }
            }
        }

        return memory;
    }

    /// <summary>
    /// Gives the session id that <paramref name="user"/> used last.
    /// </summary>
    /// <param name="user">The login name.</param>
    /// <returns>The session id, or <see langword="null"/> when none is remembered.</returns>
    public string? SessionOf(string user) => _users.TryGetValue(user, out var entry) ? entry.Session : null;

    /// <summary>
    /// Gives the keyboard layout id that <paramref name="user"/> used last.
    /// </summary>
    /// <param name="user">The login name.</param>
    /// <returns>The layout id, or <see langword="null"/> when none is remembered.</returns>
    public string? LayoutOf(string user) => _users.TryGetValue(user, out var entry) ? entry.Layout : null;

    /// <summary>
    /// Gives a value that <see cref="RememberValue"/> stored for <paramref name="user"/>.
    /// </summary>
    /// <param name="user">The login name.</param>
    /// <param name="key">The key in the user's table.</param>
    /// <returns>The value, or <see langword="null"/> when none is stored.</returns>
    public string? ValueOf(string user, string key) => _values.TryGetValue((user, key), out var value) ? value : null;

    /// <summary>
    /// Stores a program value in the user's table and writes the file. The value stays in memory also when the write fails.
    /// </summary>
    /// <param name="user">The login name.</param>
    /// <param name="key">The key in the user's table. It cannot be <c>session</c> or <c>layout</c>.</param>
    /// <param name="value">The value to store.</param>
    /// <returns><see langword="true"/> when the file was written.</returns>
    /// <exception cref="ArgumentException"><paramref name="user"/> or <paramref name="key"/> is empty, or <paramref name="key"/> is <c>session</c> or <c>layout</c>.</exception>
    public bool RememberValue(string user, string key, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(user);
        ArgumentException.ThrowIfNullOrEmpty(key);
        if (key is "session" or "layout")
        {
            throw new ArgumentException("The session and layout keys are set only through Remember.", nameof(key));
        }

        _values[(user, key)] = value;
        return Write(document => document.Set("users." + TomlValue.Key(user), key, TomlValue.From(value)));
    }

    /// <summary>
    /// Makes <paramref name="user"/> the last user, stores the session and the layout, and writes the file. A null value keeps the value that is remembered.
    /// </summary>
    /// <param name="user">The login name.</param>
    /// <param name="session">The session id, or <see langword="null"/> to keep the remembered one.</param>
    /// <param name="layout">The layout id, or <see langword="null"/> to keep the remembered one.</param>
    /// <returns><see langword="true"/> when the file was written.</returns>
    /// <exception cref="ArgumentException"><paramref name="user"/> is null or empty.</exception>
    public bool Remember(string user, string? session, string? layout)
    {
        ArgumentException.ThrowIfNullOrEmpty(user);
        LastUser = user;
        _users[user] = (session ?? SessionOf(user), layout ?? LayoutOf(user));
        return Write(document =>
        {
            document.Set("", "last_user", TomlValue.From(user));
            var table = "users." + TomlValue.Key(user);
            if (_users[user].Session is { } chosen)
            {
                document.Set(table, "session", TomlValue.From(chosen));
            }

            if (_users[user].Layout is { } keymap)
            {
                document.Set(table, "layout", TomlValue.From(keymap));
            }
        });
    }

    private bool Write(Action<TomlDocument> change)
    {
        try
        {
            var document = TomlDocument.LoadOrEmpty(Path);
            change(document);
            document.Save(Path);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            if (!_warned)
            {
                _warned = true;
                _log.Warn(error, $"{Path}: not written, the greeter runs without memory");
            }

            return false;
        }
    }
}
