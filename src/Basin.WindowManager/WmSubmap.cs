using Basin.WindowManager.Protocol;

namespace Basin.WindowManager;

public sealed class WmSubmap
{
    private readonly RiverWindowManager _wm;
    private readonly WmBindings _owner;
    private readonly WmSeat _seat;
    private readonly IReadOnlyList<KeyBinding> _bindings;
    private readonly List<KeyBinding> _suspended = [];
    private readonly TimeSpan _timeout;
    private IWmEventSource? _timer;
    private bool _exited;

    internal WmSubmap(
        RiverWindowManager wm,
        WmBindings owner,
        WmSeat seat,
        IReadOnlyList<KeyBinding> bindings,
        TimeSpan timeout)
    {
        _wm = wm;
        _owner = owner;
        _seat = seat;
        _bindings = bindings;
        _timeout = timeout;
    }

    public bool IsExited => _exited;

    public event Action? Exited;

    public void Exit()
    {
        _wm.EnsureManage(nameof(Exit));
        if (_exited)
        {
            return;
        }

        _exited = true;
        _timer?.Remove();
        _timer = null;

        foreach (var binding in _bindings)
        {
            binding.Disable();
        }

        foreach (var binding in _suspended)
        {
            binding.Enable();
        }

        _suspended.Clear();
        _owner.OnSubmapExited(_seat, this);
        Exited?.Invoke();
    }

    internal void Enter(IReadOnlyList<KeyBinding> allBindings)
    {
        foreach (var binding in allBindings)
        {
            if (binding.IsEnabled && !_bindings.Contains(binding))
            {
                _suspended.Add(binding);
                binding.Disable();
            }
        }

        foreach (var binding in _bindings)
        {
            if (!binding.IsEnabled)
            {
                binding.Enable();
            }

            binding.Pressed += OnBindingFired;
        }

        _owner.EnsureNextKeyEaten(_seat);

        if (_timeout > TimeSpan.Zero)
        {
            _timer = _wm.Loop.AddTimer(() =>
            {
                _pendingExit = true;
                _wm.RequestManage();
            });
            _timer.UpdateTimer((int)Math.Clamp(_timeout.TotalMilliseconds, 1, int.MaxValue));
        }

        _wm.Manage += OnManage;
    }

    internal void Cancel()
    {
        _exited = true;
        _timer?.Remove();
        _timer = null;
        _wm.Manage -= OnManage;
        foreach (var binding in _bindings)
        {
            binding.Pressed -= OnBindingFired;
        }
    }

    internal void OnUnboundKey()
    {
        _pendingExit = true;
    }

    private bool _pendingExit;

    private void OnBindingFired() => _pendingExit = true;

    private void OnManage(ManageContext context)
    {
        if (!_pendingExit || _exited)
        {
            return;
        }

        _wm.Manage -= OnManage;
        foreach (var binding in _bindings)
        {
            binding.Pressed -= OnBindingFired;
        }

        Exit();
    }
}
