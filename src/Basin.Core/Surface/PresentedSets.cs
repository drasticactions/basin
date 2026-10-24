namespace Basin;

/// <summary>
/// One <see cref="PresentedSet"/> per output. The scene and the presentation pump share it.
/// </summary>
public sealed class PresentedSets
{
    private readonly Dictionary<IOutput, PresentedSet> _sets = [];

    /// <summary>Gets the set of an output, and creates it on the first call.</summary>
    /// <param name="output">The output.</param>
    /// <returns>The set of <paramref name="output"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="output"/> is <see langword="null"/>.</exception>
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

    /// <summary>Finds the set of an output that has recorded a frame.</summary>
    /// <param name="output">The output.</param>
    /// <returns>The set, or <see langword="null"/> when the output has no set or its set never recorded a frame.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="output"/> is <see langword="null"/>.</exception>
    public PresentedSet? Find(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return _sets.TryGetValue(output, out var set) && set.Recorded ? set : null;
    }

    /// <summary>Gets a value indicating whether any output recorded a frame. When none did, the presentation pump presents every surface.</summary>
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

    /// <summary>Removes an output's set and retires it, so that its surfaces need a new main output.</summary>
    /// <param name="output">The output that is gone.</param>
    /// <exception cref="ArgumentNullException"><paramref name="output"/> is <see langword="null"/>.</exception>
    public void Retire(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (_sets.Remove(output, out var set))
        {
            set.Retire();
        }
    }
}
