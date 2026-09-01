namespace DeskbarWm;

internal sealed class SwitcherState
{
    private readonly List<Team> _teams = [];

    public IReadOnlyList<Team> Teams => _teams;

    public int TeamIndex { get; private set; }

    public int WindowIndex { get; private set; }

    public SwitcherState(IReadOnlyList<Team> teams, Team? current)
    {
        _teams.AddRange(teams);
        if (current is not null)
        {
            var index = _teams.IndexOf(current);
            TeamIndex = index >= 0 ? index : 0;
        }
    }

    public Team? SelectedTeam =>
        _teams.Count > 0 && TeamIndex < _teams.Count ? _teams[TeamIndex] : null;

    public ManagedWindow? SelectedWindow
    {
        get
        {
            if (SelectedTeam is not { Windows.Count: > 0 } team)
            {
                return null;
            }

            return team.Windows[Math.Clamp(WindowIndex, 0, team.Windows.Count - 1)];
        }
    }

    public void Prune()
    {
        _teams.RemoveAll(static team => team.Windows.Count == 0);
        if (_teams.Count == 0)
        {
            TeamIndex = 0;
            WindowIndex = 0;
            return;
        }

        TeamIndex = Math.Clamp(TeamIndex, 0, _teams.Count - 1);
        WindowIndex = Math.Clamp(WindowIndex, 0, _teams[TeamIndex].Windows.Count - 1);
    }

    public void CycleTeam(int direction)
    {
        if (_teams.Count == 0)
        {
            return;
        }

        TeamIndex = (((TeamIndex + direction) % _teams.Count) + _teams.Count) % _teams.Count;
        WindowIndex = 0;
    }

    public void CycleWindow(int direction)
    {
        if (SelectedTeam is not { Windows.Count: > 0 } team)
        {
            return;
        }

        var count = team.Windows.Count;
        WindowIndex = (((WindowIndex + direction) % count) + count) % count;
    }
}
