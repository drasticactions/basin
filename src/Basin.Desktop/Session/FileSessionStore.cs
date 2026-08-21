using System.Globalization;
using System.Text;
using Basin.Capabilities;

namespace Basin.Desktop;

public sealed class FileSessionStore : ISessionStore
{
    private readonly string _path;
    private readonly Dictionary<string, Dictionary<string, ToplevelSessionState>> _sessions = [];

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

    public string? CreateSessionId()
    {
        var id = Guid.NewGuid().ToString("n");
        _sessions[id] = [];
        Save();
        return id;
    }

    public bool IsValidSessionId(string sessionId) => _sessions.ContainsKey(sessionId);

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

    public void Save(string sessionId, string toplevelName, in ToplevelSessionState state)
    {
        if (!_sessions.TryGetValue(sessionId, out var toplevels))
        {
            _sessions[sessionId] = toplevels = [];
        }

        toplevels[toplevelName] = state;
        Save();
    }

    public void ForgetToplevel(string sessionId, string toplevelName)
    {
        if (_sessions.TryGetValue(sessionId, out var toplevels) && toplevels.Remove(toplevelName))
        {
            Save();
        }
    }

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

        foreach (var line in File.ReadAllLines(_path))
        {
            var parts = line.Split('\t');
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
        foreach (var (session, toplevels) in _sessions)
        {
            if (toplevels.Count == 0)
            {
                builder.Append(session).Append("\t\t0\t0\t0\t0\t0\t\n");
                continue;
            }

            foreach (var (name, state) in toplevels)
            {
                var g = state.Geometry;
                var workspace = state.WorkspaceName is { } raw
                    ? raw.Replace('\t', ' ').Replace('\n', ' ')
                    : string.Empty;
                builder.Append(CultureInfo.InvariantCulture,
                    $"{session}\t{name}\t{g.X}\t{g.Y}\t{g.Width}\t{g.Height}\t{(int)state.States}\t{state.OutputLayoutId}\t{workspace}\n");
            }
        }

        File.WriteAllText(_path, builder.ToString());
    }

    private static int Int(string text) => int.TryParse(text, CultureInfo.InvariantCulture, out var value) ? value : 0;
}
