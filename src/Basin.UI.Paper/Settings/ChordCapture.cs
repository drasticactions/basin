namespace Basin.UI.Paper;

public sealed class ChordCapture
{
    private ISettingValue<string>? _target;

    public bool Active => _target is not null;

    public ISettingValue<string>? Target => _target;

    public event Action? Changed;

    public void Begin(ISettingValue<string> target)
    {
        ArgumentNullException.ThrowIfNull(target);
        _target = target;
        Changed?.Invoke();
    }

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
