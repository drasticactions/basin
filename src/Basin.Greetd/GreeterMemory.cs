using Basin.Config;
using Basin.Diagnostics;
using Tomlyn.Model;

namespace Basin.Greetd;

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

    public string Path { get; }

    public string? LastUser { get; private set; }

    public static string DefaultPath(string program)
    {
        ArgumentException.ThrowIfNullOrEmpty(program);
        var directory = Environment.GetEnvironmentVariable("STATE_DIRECTORY") is { Length: > 0 } systemd
            ? systemd.Split(':')[0]
            : System.IO.Path.Combine("/var/lib", program);
        return System.IO.Path.Combine(directory, "state.toml");
    }

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
            log.Warn($"{path}: {failure}; nothing is remembered from it");
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

    public string? SessionOf(string user) => _users.TryGetValue(user, out var entry) ? entry.Session : null;

    public string? LayoutOf(string user) => _users.TryGetValue(user, out var entry) ? entry.Layout : null;

    public string? ValueOf(string user, string key) => _values.TryGetValue((user, key), out var value) ? value : null;

    public bool RememberValue(string user, string key, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(user);
        ArgumentException.ThrowIfNullOrEmpty(key);
        if (key is "session" or "layout")
        {
            throw new ArgumentException("session and layout are remembered through Remember", nameof(key));
        }

        _values[(user, key)] = value;
        return Write(document => document.Set("users." + TomlValue.Key(user), key, TomlValue.From(value)));
    }

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
            var document = File.Exists(Path) ? TomlDocument.Load(Path) : TomlDocument.Parse("");
            change(document);
            document.Save(Path);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            if (!_warned)
            {
                _warned = true;
                _log.Warn($"{Path}: not written ({error.Message}); the greeter runs without memory");
            }

            return false;
        }
    }
}
