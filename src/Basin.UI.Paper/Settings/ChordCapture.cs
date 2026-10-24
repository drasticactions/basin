namespace Basin.UI.Paper;

/// <summary>
/// Records which chord setting waits for the next key chord. The consumer reads keys and gives the chord to <see cref="Offer"/>.
/// </summary>
public sealed class ChordCapture
{
    private ISettingValue<string>? _target;

    /// <summary>
    /// Gets a value indicating whether a setting waits for a chord.
    /// </summary>
    public bool Active => _target is not null;

    /// <summary>
    /// Gets the setting that waits for a chord, or <see langword="null"/>.
    /// </summary>
    public ISettingValue<string>? Target => _target;

    /// <summary>
    /// Occurs when a capture starts, completes or is cancelled.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Makes a setting wait for the next chord. It replaces a capture that is in progress.
    /// </summary>
    /// <param name="target">The setting that gets the chord.</param>
    public void Begin(ISettingValue<string> target)
    {
        ArgumentNullException.ThrowIfNull(target);
        _target = target;
        Changed?.Invoke();
    }

    /// <summary>
    /// Writes a chord to the waiting setting and ends the capture.
    /// </summary>
    /// <param name="chord">The chord text, for example <c>Super+Return</c>.</param>
    /// <returns><see langword="true"/> when a setting took the chord, <see langword="false"/> when none was waiting.</returns>
    /// <exception cref="ArgumentException"><paramref name="chord"/> is empty.</exception>
    public bool Offer(string chord)
    {
        ArgumentException.ThrowIfNullOrEmpty(chord);
        if (_target is not { } target)
        {
            return false;
        }

        _target = null;
        target.Value = chord;
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// Ends the capture with no change. It does nothing when no setting waits.
    /// </summary>
    public void Cancel()
    {
        if (_target is null)
        {
            return;
        }

        _target = null;
        Changed?.Invoke();
    }
}
