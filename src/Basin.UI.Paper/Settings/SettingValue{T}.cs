namespace Basin.UI.Paper;

public sealed class SettingValue<T> : ISettingValue<T>
{
    private readonly Func<T> _get;
    private readonly Action<T> _set;
    private readonly Func<bool>? _dirty;
    private readonly Func<string?>? _badge;
    private readonly Func<string?>? _error;

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

    public T Value
    {
        get => _get();
        set => _set(value);
    }

    public bool IsDirty => _dirty?.Invoke() ?? false;

    public string? Badge => _badge?.Invoke();

    public string? Error => _error?.Invoke();
}
