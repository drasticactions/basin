using Basin.Desktop;
using Basin.Scene;

namespace EightWm;

internal sealed partial class Shell
{
    private SceneTree _lockLayer = null!;
    private SessionLockSceneDriver _lockDriver = null!;
    private AppWindow? _lockedFocus;
    private bool _locked;

    internal bool IsLocked => _locked;

    private void CreateLockDriver()
    {
        _lockLayer = new SceneTree(_scene.Root);
        _lockDriver = new SessionLockSceneDriver(_services, _lockLayer, _layout, SetExternalLocked);
        CreateOwnLock();
    }

    private void AttachSessionLock()
    {
        _lockDriver.Locked += () => _report.Line($"LOCKED");
        _lockDriver.Unlocked += () =>
        {
            if (_ownLocked)
            {
                _report.Line($"UNLOCKED external");
                ApplyLock();
                return;
            }

            if (_lockedFocus is { Surface: { } surface } && _apps.Contains(_lockedFocus))
            {
                Seat.Keyboard.NotifyEnter(surface);
            }
            else
            {
                RestoreLayerKeyboard();
            }

            _lockedFocus = null;
            _report.Line($"UNLOCKED");
        };
        _lockDriver.Abandoned += () => _report.Line($"LOCK ABANDONED (staying blanked)");
        _outputs.Added += driver =>
        {
            ViewOf(driver).Root.Enabled = !_locked;
            if (_ownLocked)
            {
                AddLockScreen(ViewOf(driver));
                UpdateLockClock();
            }

            _ownLockLayer.RaiseToTop();
            _lockLayer.RaiseToTop();
        };
        _outputs.LayoutChanged += _lockDriver.Reconfigure;
        _outputs.LayoutChanged += PlaceLockScreens;
    }

    private void SetLocked(bool locked)
    {
        if (locked && !_locked)
        {
            _lockedFocus = Focused;
        }

        _locked = locked;
        foreach (var view in _shellViews)
        {
            view.Root.Enabled = !locked;
        }

        _popupLayer.Enabled = !locked;
        _lockLayer.RaiseToTop();
        if (locked)
        {
            _router.SetKeyboardFocus(null);
            _router.PointerLeave();
        }

        _outputs.ScheduleAll();
    }
}
