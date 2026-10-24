namespace Basin.Seat.Backends;

/// <summary>
/// Offers each new contact to <see cref="First"/>, then to <see cref="Then"/>, and sends the later events of a contact only to the chrome that took it.
/// </summary>
public sealed class TouchChromeChain : ITouchChrome
{
    private readonly Dictionary<int, ITouchChrome> _owners = [];

    /// <summary>
    /// Gets or sets the chrome that gets the first offer of each new contact.
    /// </summary>
    public ITouchChrome? First { get; set; }

    /// <summary>
    /// Gets or sets the chrome that gets a new contact when <see cref="First"/> refuses it.
    /// </summary>
    public ITouchChrome? Then { get; set; }

    /// <inheritdoc/>
    public bool TryPress(int id, uint timeMs, double x, double y)
    {
        if (First is { } first && first.TryPress(id, timeMs, x, y))
        {
            _owners[id] = first;
            return true;
        }

        if (Then is { } then && then.TryPress(id, timeMs, x, y))
        {
            _owners[id] = then;
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public void Motion(int id, uint timeMs, double x, double y)
    {
        if (_owners.TryGetValue(id, out var owner))
        {
            owner.Motion(id, timeMs, x, y);
        }
    }

    /// <inheritdoc/>
    public void Release(int id, uint timeMs, double x, double y)
    {
        if (_owners.Remove(id, out var owner))
        {
            owner.Release(id, timeMs, x, y);
        }
    }

    /// <summary>
    /// Forgets every contact owner and cancels both chromes.
    /// </summary>
    public void Cancel()
    {
        _owners.Clear();
        First?.Cancel();
        Then?.Cancel();
    }
}
