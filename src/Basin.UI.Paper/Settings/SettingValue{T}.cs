namespace Basin.UI.Paper;

/// <summary>
/// An <see cref="ISettingValue{T}"/> that reads and writes through delegates.
/// </summary>
/// <typeparam name="T">The type of the value.</typeparam>
public sealed class SettingValue<T> : ISettingValue<T>
{
    private readonly Func<T> _get;
    private readonly Action<T> _set;
    private readonly Func<bool>? _dirty;
    private readonly Func<string?>? _badge;
    private readonly Func<string?>? _error;

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingValue{T}"/> class.
    /// </summary>
    /// <param name="get">Reads the value.</param>
    /// <param name="set">Writes the value.</param>
    /// <param name="dirty">Tells whether the value is not saved, or <see langword="null"/> for never.</param>
    /// <param name="badge">Gives the badge text, or <see langword="null"/> for none.</param>
    /// <param name="error">Gives the error text, or <see langword="null"/> for none.</param>
    public SettingValue(
        Func<T> get, Action<T> set, Func<bool>? dirty = null, Func<string?>? badge = null, Func<string?>? error = null)
    {
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(set);

        _get = get;
        _set = set;
        _dirty = dirty;
        _badge = badge;
        _error = error;
    }

    /// <inheritdoc/>
    public T Value
    {
        get => _get();
        set => _set(value);
    }

    /// <inheritdoc/>
    public bool IsDirty => _dirty?.Invoke() ?? false;

    /// <inheritdoc/>
    public string? Badge => _badge?.Invoke();

    /// <inheritdoc/>
    public string? Error => _error?.Invoke();
}
