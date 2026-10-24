namespace TabWm;

internal static class TabSession
{
    public static SessionFile Capture(IEnumerable<(string Connector, int Selected, IEnumerable<SessionTab> Tabs)> outputs)
    {
        var file = new SessionFile();
        foreach (var (connector, selected, tabs) in outputs)
        {
            file.Outputs.Add(new SessionOutput { Connector = connector, Selected = selected, Tabs = [.. tabs] });
        }

        return file;
    }

    public static SessionTab Describe(Tab tab, Func<TabLaunch, bool> alive)
    {
        var saved = new SessionTab { Label = tab.UserLabel, Muted = tab.Muted };
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var launch in tab.Launches)
        {
            if (!alive(launch))
            {
                continue;
            }

            var key = launch.DesktopId ?? launch.Command;
            if (!seen.Add(key))
            {
                continue;
            }

            saved.Apps.Add(launch.DesktopId is { } id
                ? new SessionApp { DesktopId = id }
                : new SessionApp { Command = launch.Command });
        }

        return saved;
    }

    public static bool IsValid(SessionFile? file) =>
        file is { Version: 1 } && file.Outputs.All(output => output.Tabs is not null && output.Tabs.All(tab => tab.Apps is not null));
}
