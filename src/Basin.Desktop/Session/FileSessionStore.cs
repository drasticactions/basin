using System.Globalization;
using System.Text;
using Basin.Capabilities;

namespace Basin.Desktop;

/// <summary>
/// Keeps toplevel session state in a <c>sessions.tsv</c> file in the application directory under <c>XDG_STATE_HOME</c>. Each change writes the whole file to a temporary file and then replaces the old one, so a crash during the write keeps the old file. Each text field is escaped with backslash codes, so a client string cannot split a record.
/// </summary>
public sealed class FileSessionStore : ISessionStore
{
    private const string Header = "#basin-sessions 2";
    private readonly string _path;
    private readonly Dictionary<string, Dictionary<string, ToplevelSessionState>> _sessions = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="FileSessionStore"/> class. It creates the directory and loads the file when it exists.
    /// </summary>
    /// <param name="appName">The directory name under the state directory. Without <c>XDG_STATE_HOME</c>, the state directory is <c>~/.local/state</c>.</param>
    /// <exception cref="ArgumentException"><paramref name="appName"/> is <see langword="null"/> or empty.</exception>
    public FileSessionStore(string appName)
    {
        ArgumentException.ThrowIfNullOrEmpty(appName);
        var state = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        if (string.IsNullOrEmpty(state))
        {
            state = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state");
        }

        var directory = Path.Combine(state, appName);
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "sessions.tsv");
        Load();
    }

    /// <summary>
    /// Creates a random session id, records it with no toplevels and saves the file.
    /// </summary>
    /// <returns>A new id of 32 hex digits. It is never <see langword="null"/>.</returns>
    public string? CreateSessionId()
    {
        var id = Guid.NewGuid().ToString("n");
        _sessions[id] = [];
        Save();
        return id;
    }

    /// <inheritdoc/>
    public bool IsValidSessionId(string sessionId) => _sessions.ContainsKey(sessionId);

    /// <inheritdoc/>
    public bool TryRestore(
        string sessionId, string toplevelName, SessionRestoreReason reason, out ToplevelSessionState state)
    {
        state = default;
        if (!_sessions.TryGetValue(sessionId, out var toplevels) ||
            !toplevels.TryGetValue(toplevelName, out var saved))
        {
            return false;
        }

        state = saved;
        return true;
    }

    /// <summary>
    /// Saves the state of one toplevel and rewrites the file. An unknown session id becomes a new session.
    /// </summary>
    /// <param name="sessionId">The session id.</param>
    /// <param name="toplevelName">The name that the client gave the toplevel.</param>
    /// <param name="state">The state to save.</param>
    public void Save(string sessionId, string toplevelName, in ToplevelSessionState state)
    {
        if (!_sessions.TryGetValue(sessionId, out var toplevels))
        {
            _sessions[sessionId] = toplevels = [];
        }

        toplevels[toplevelName] = state;
        Save();
    }

    /// <inheritdoc/>
    public void ForgetToplevel(string sessionId, string toplevelName)
    {
        if (_sessions.TryGetValue(sessionId, out var toplevels) && toplevels.Remove(toplevelName))
        {
            Save();
        }
    }

    /// <inheritdoc/>
    public void Forget(string sessionId)
    {
        if (_sessions.Remove(sessionId))
        {
            Save();
        }
    }

    private void Load()
    {
        if (!File.Exists(_path))
        {
            return;
        }

        var lines = File.ReadAllLines(_path);
        var escaped = lines.Length > 0 && lines[0] == Header;
        foreach (var line in lines)
        {
            var parts = line.Split('\t');
            if (escaped)
            {
                for (var i = 0; i < parts.Length; i++)
                {
                    parts[i] = Unescape(parts[i]);
                }
            }

            if (parts.Length < 8)
            {
                continue;
            }

            var session = parts[0];
            if (!_sessions.TryGetValue(session, out var toplevels))
            {
                _sessions[session] = toplevels = [];
            }

            if (parts[1].Length == 0)
            {
                continue;
            }

            toplevels[parts[1]] = new ToplevelSessionState
            {
                Geometry = new Box(Int(parts[2]), Int(parts[3]), Int(parts[4]), Int(parts[5])),
                States = (ToplevelSessionStates)Int(parts[6]),
                OutputLayoutId = parts[7].Length > 0 ? parts[7] : null,
                WorkspaceName = parts.Length > 8 && parts[8].Length > 0 ? parts[8] : null,
            };
        }
    }

    private void Save()
    {
        var builder = new StringBuilder();
        builder.Append(Header).Append('\n');
        foreach (var (session, toplevels) in _sessions)
        {
            if (toplevels.Count == 0)
            {
                AppendEscaped(builder, session);
                builder.Append("\t\t0\t0\t0\t0\t0\t\n");
                continue;
            }

            foreach (var (name, state) in toplevels)
            {
                var g = state.Geometry;
                AppendEscaped(builder, session);
                builder.Append('\t');
                AppendEscaped(builder, name);
                builder.Append(CultureInfo.InvariantCulture, $"\t{g.X}\t{g.Y}\t{g.Width}\t{g.Height}\t{(int)state.States}\t");
                AppendEscaped(builder, state.OutputLayoutId);
                builder.Append('\t');
                AppendEscaped(builder, state.WorkspaceName);
                builder.Append('\n');
            }
        }

        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, builder.ToString());
        File.Move(temporary, _path, overwrite: true);
    }

    private static void AppendEscaped(StringBuilder builder, string? text)
    {
        if (text is null)
        {
            return;
        }

        foreach (var c in text)
        {
            _ = c switch
            {
                '\\' => builder.Append("\\\\"),
                '\t' => builder.Append("\\t"),
                '\n' => builder.Append("\\n"),
                '\r' => builder.Append("\\r"),
                _ => builder.Append(c),
            };
        }
    }

    private static string Unescape(string field)
    {
        if (!field.Contains('\\', StringComparison.Ordinal))
        {
            return field;
        }

        var builder = new StringBuilder(field.Length);
        for (var i = 0; i < field.Length; i++)
        {
            var c = field[i];
            if (c != '\\' || i + 1 == field.Length)
            {
                builder.Append(c);
                continue;
            }

            i++;
            builder.Append(field[i] switch
            {
                't' => '\t',
                'n' => '\n',
                'r' => '\r',
                _ => field[i],
            });
        }

        return builder.ToString();
    }

    private static int Int(string text) => int.TryParse(text, CultureInfo.InvariantCulture, out var value) ? value : 0;
}
