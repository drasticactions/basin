using Basin.WindowManager;

namespace DeskbarWm;

internal sealed class TeamTable
{
    private readonly Dictionary<string, Team> _byKey = [];
    private readonly List<Team> _order = [];
    private readonly List<Team> _sorted = [];

    public IReadOnlyList<Team> Teams(bool sorted)
    {
        if (!sorted)
        {
            return _order;
        }

        _sorted.Clear();
        _sorted.AddRange(_order);
        _sorted.Sort(static (a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
        return _sorted;
    }

    public Team? TeamOf(ManagedWindow mw)
    {
        foreach (var team in _order)
        {
            if (team.Windows.Contains(mw))
            {
                return team;
            }
        }

        return null;
    }

    public bool Refresh(IReadOnlyList<ManagedWindow> windows)
    {
        var changed = false;
        foreach (var team in _order)
        {
            team.Windows.Clear();
        }

        foreach (var mw in windows)
        {
            var key = KeyFor(mw.Window);
            if (!_byKey.TryGetValue(key, out var team))
            {
                team = new Team(key) { AppId = mw.Window.AppId };
                _byKey[key] = team;
                _order.Add(team);
                changed = true;
            }

            team.AppId ??= mw.Window.AppId;
            team.Windows.Add(mw);
        }

        for (var i = _order.Count - 1; i >= 0; i--)
        {
            if (_order[i].Windows.Count == 0)
            {
                _byKey.Remove(_order[i].Key);
                _order.RemoveAt(i);
                changed = true;
            }
        }

        return changed;
    }

    private static string KeyFor(WmWindow window)
    {
        if (window.UnreliablePid > 0)
        {
            return $"pid:{window.UnreliablePid}";
        }

        if (window.AppId is { Length: > 0 } appId)
        {
            return $"app:{appId}";
        }

        return $"win:{window.Identifier ?? window.GetHashCode().ToString()}";
    }
}
