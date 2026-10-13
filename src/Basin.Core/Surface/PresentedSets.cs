namespace Basin;

public sealed class PresentedSets
{
    private readonly Dictionary<IOutput, PresentedSet> _sets = [];

    public PresentedSet For(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (!_sets.TryGetValue(output, out var set))
        {
            set = new PresentedSet(output);
            _sets[output] = set;
        }

        return set;
    }

    public PresentedSet? Find(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return _sets.TryGetValue(output, out var set) && set.Recorded ? set : null;
    }

    public bool AnyRecorded
    {
        get
        {
            foreach (var set in _sets.Values)
            {
                if (set.Recorded)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public void Retire(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (_sets.Remove(output, out var set))
        {
            set.Retire();
        }
    }
}
