using Basin;
using Basin.Diagnostics;

namespace TabWm;

internal sealed partial class TabWm
{
    private const int SessionSaveMs = 30_000;

    private IEventSource? _sessionTimer;
    private bool _sessionDirty;
    private bool _sessionStarted;

    private void StartSession()
    {
        _sessionStarted = true;
        if (_config.Restore)
        {
            RestoreSession();
        }

        foreach (var command in _config.Startup)
        {
            Launch(PrimarySet?.Tabs.FirstOrDefault(), command, null, command);
        }

        if (_options.Command is { Length: > 0 } startup)
        {
            Launch(PrimarySet?.Tabs.FirstOrDefault(), startup, null, startup);
        }
    }

    private void MarkSessionDirty()
    {
        if (!_sessionStarted || _sessionDirty || _stopping)
        {
            return;
        }

        _sessionDirty = true;
        _sessionTimer ??= _host.Loop.AddTimer(() => SaveSession("timer"));
        _sessionTimer.UpdateTimer(SessionSaveMs);
    }

    internal bool SaveSession(string reason)
    {
        if (!_sessionStarted)
        {
            return false;
        }

        _sessionDirty = false;
        _sessionTimer?.UpdateTimer(0);
        var file = TabSession.Capture(_sets.Select(set => (
            set.Output.Name,
            set.ActiveIndex,
            set.Tabs.Select(tab => TabSession.Describe(tab, launch => HasWindow(tab, launch))))));
        if (!StateFiles.Write(StateFiles.SessionPath, file, TabWmJsonContext.Default.SessionFile, out var error))
        {
            _log.Warn($"session: cannot save {error}");
            return false;
        }

        BasinReport.Line($"SESSION saved reason={reason} tabs={file.Outputs.Sum(output => output.Tabs.Count)}");
        return true;
    }

    private bool HasWindow(Tab tab, TabLaunch launch)
    {
        foreach (var window in tab.Windows)
        {
            if (window.Mapped && _processes.RootOf(window.Pid) is { } root && root.Pid == launch.Pid)
            {
                return true;
            }
        }

        foreach (var window in tab.Windows)
        {
            if (window.Mapped && window.Surface is { } surface && _redeemed.TryGetValue(surface, out var redemption) && redemption.Token == launch.Token)
            {
                return true;
            }
        }

        return false;
    }

    private void RestoreSession()
    {
        var file = StateFiles.Read(StateFiles.SessionPath, TabWmJsonContext.Default.SessionFile, out var error);
        if (error is not null || (file is not null && !TabSession.IsValid(file)))
        {
            _log.Warn($"session: ignoring {error ?? StateFiles.SessionPath + ": not a tab-wm session"}");
            BasinReport.Line("SESSION corrupt");
            return;
        }

        if (file is null || file.Outputs.Count == 0)
        {
            return;
        }

        var restored = 0;
        foreach (var output in file.Outputs)
        {
            if ((_sets.FirstOrDefault(set => set.Output.Name == output.Connector) ?? PrimarySet) is not { } set)
            {
                continue;
            }

            var placeholder = set.Tabs.Count == 1 && set.Tabs[0].IsEmpty && set.Tabs[0].Launches.Count == 0 && set.Tabs[0].UserLabel is null
                ? set.Tabs[0]
                : null;
            var first = set.Tabs.Count;
            foreach (var saved in output.Tabs)
            {
                var tab = NewTab(set, select: false, label: saved.Label);
                tab.Muted = saved.Muted;
                foreach (var app in saved.Apps)
                {
                    if (app.DesktopId is { } id)
                    {
                        LaunchId(id, tab);
                    }
                    else if (app.Command is { Length: > 0 } command)
                    {
                        Launch(tab, command, null, command);
                    }
                }

                RefreshTab(tab);
                restored++;
            }

            if (placeholder is not null && set.Tabs.Count > 1)
            {
                RemoveTab(placeholder);
                first--;
            }

            var selected = first + Math.Clamp(output.Selected, 0, Math.Max(0, output.Tabs.Count - 1));
            if (selected >= 0 && selected < set.Tabs.Count)
            {
                SelectTab(set.Tabs[selected]);
            }
        }

        BasinReport.Line($"SESSION restored tabs={restored}");
    }
}
