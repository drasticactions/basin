using System.Globalization;
using Basin;
using Basin.Audio.PipeWire;
using Basin.Diagnostics;
using Basin.Freedesktop;

namespace TabWm;

internal sealed partial class TabWm
{
    private readonly Dictionary<long, int> _launchGroups = [];
    private long _nextAudioTag;

    internal TabLaunch? Launch(Tab? tab, string command, string? desktopId, string? name)
    {
        tab ??= PrimaryOrFocusedSet()?.Active;
        if (tab is null)
        {
            return null;
        }

        var token = _tokens.MintLaunch(desktopId);
        var tag = ++_nextAudioTag;
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["XDG_ACTIVATION_TOKEN"] = token,
            ["DESKTOP_STARTUP_ID"] = token,
        };
        foreach (var (key, value) in AudioTag(tag))
        {
            environment[key] = value;
        }

        var launch = new ProcessLaunch(["/bin/sh", "-c", command]) { DesktopId = desktopId, Env = environment };
        if (_processes.Spawn(launch, out var error) is not { } process)
        {
            _log.Warn($"launch: cannot start {command}: {error}");
            BasinReport.Line($"LAUNCH failed {name ?? command}");
            return null;
        }

        var now = Environment.TickCount64;
        var record = new TabLaunch(process.Pid, desktopId, command, token, now, tag);
        tab.Launches.Add(record);
        tab.LaunchedAtMs = now;
        _launchGroups[tag] = process.Pid;
        if (!tab.Shown)
        {
            SelectTab(tab);
        }

        BasinReport.Line($"LAUNCH tab={tab.Id} pid={process.Pid} token={token} app={desktopId ?? "-"}");
        MarkSessionDirty();
        return record;
    }

    internal TabLaunch? LaunchEntry(DesktopEntry entry, Tab? tab)
    {
        if (entry.LaunchArgv(Terminal()) is not { Length: > 0 } argv)
        {
            BasinReport.Line($"LAUNCH cannot {entry.Id}");
            return null;
        }

        return Launch(tab, ExecLine.Join(argv), entry.Id, entry.Name);
    }

    internal TabLaunch? LaunchId(string desktopId, Tab? tab)
    {
        var id = desktopId.EndsWith(".desktop", StringComparison.Ordinal) ? desktopId : desktopId + ".desktop";
        if (Desktop.Find(id) is not { } entry)
        {
            BasinReport.Line($"LAUNCH none {desktopId}");
            return null;
        }

        return LaunchEntry(entry, tab);
    }

    private string[]? Terminal() =>
        _config.Terminal is { Length: > 0 } terminal ? ExecLine.Split(terminal) : ExecLine.TerminalFromEnvironment();

    private static Dictionary<string, string> AudioTag(long tag)
    {
        var id = tag.ToString(CultureInfo.InvariantCulture);
        var pipewire = Environment.GetEnvironmentVariable("PIPEWIRE_PROPS") is { Length: > 0 } existing
            ? $"{{ {existing.Trim().TrimStart('{').TrimEnd('}')} {AudioStreams.LaunchKey} = {id} }}"
            : $"{{ {AudioStreams.LaunchKey} = {id} }}";
        var pulse = Environment.GetEnvironmentVariable("PULSE_PROP") is { Length: > 0 } list
            ? $"{list} {AudioStreams.LaunchKey}='{id}'"
            : $"{AudioStreams.LaunchKey}='{id}'";
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["PIPEWIRE_PROPS"] = pipewire,
            ["PULSE_PROP"] = pulse,
        };
    }

    private int LaunchGroup(long tag) => _launchGroups.TryGetValue(tag, out var pid) ? pid : 0;

    internal void Lock()
    {
        if (_layers.IsLocked)
        {
            return;
        }

        if (_config.LockCommand is not { Length: > 0 } command)
        {
            _log.Warn($"lock: [session] lock-command is empty, nothing locks the session");
            return;
        }

        if (_processes.SpawnShell(command, out var error) is null)
        {
            _log.Warn($"lock: cannot start {command}: {error}");
        }
    }
}
