using System.Text;
using Basin;
using Basin.Config;
using Basin.Session;

namespace LunaWm;

internal sealed partial class LunaShell
{
    internal const int LockoutAttempts = 5;
    internal const int LockoutMillis = 30_000;

    private readonly LunaLockState _lockState = new();
    private bool _ownLocked;
    private bool _locked;
    private Card? _lockedFocus;
    private PamCheck? _pamCheck;
    private int _failures;
    private IEventSource? _lockout;
    private IEventSource? _idleTimer;

    internal bool IsLocked => _locked;

    internal bool LockAcceptsChromeKeys => _ownLocked && !_sessionLocked;

    private void AttachLock(LunaView view)
    {
        var model = new LockScreenModel { Pin = _config.LockStyle == LockStyle.Pin };
        model.Submitted += () => SubmitUnlock(model);
        model.PadRequested += () => ShowPad(view, model);
        view.LockUi = new LockSurface(view.Lock, _ui, _chromeIndex, model);
        view.LockUi.Fade.Jump(0);
        view.LockUi.Frame.Alpha = 0;
        PlaceLock(view);
        ClockChanged(view);
    }

    private void PlaceLock(LunaView view) =>
        view.LockUi?.Chrome.Place(new Box(0, LunaView.StatusBarHeight, view.Box.Width, Math.Max(1, view.Box.Height - LunaView.StatusBarHeight)), view.Scale);

    private void ClockChanged(LunaView view)
    {
        if (view.LockUi is { } lockUi)
        {
            lockUi.Model.Clock = ClockText();
            lockUi.Model.Date = DateText();
        }
    }

    internal void Lock()
    {
        if (_ownLocked)
        {
            return;
        }

        _ownLocked = true;
        _failures = 0;
        foreach (var view in _views)
        {
            if (view.LockUi is { } lockUi)
            {
                lockUi.Model.PadShown = false;
                lockUi.Model.Pin = _config.LockStyle == LockStyle.Pin;
                lockUi.Model.Message = "";
                lockUi.Model.Clear();
                lockUi.Model.Busy = false;
            }
        }

        ApplyLockState();
        _report.Line("LOCKED");
    }

    internal void Unlock()
    {
        if (!_ownLocked)
        {
            return;
        }

        _ownLocked = false;
        _pamCheck?.Dispose();
        _pamCheck = null;
        _lockout?.Remove();
        _lockout = null;
        ApplyLockState();
        _report.Line("UNLOCKED");
    }

    private void ApplyLockState()
    {
        var locked = _ownLocked || _sessionLocked;
        if (locked && !_locked)
        {
            _lockedFocus = Focused;
            CancelDrag();
            foreach (var view in _views)
            {
                CloseShellUi(view);
            }

            Seat.Keyboard.NotifyClearFocus();
            Seat.Pointer.NotifyClearFocus();
            _seat.ClearChromeFocus();
        }

        _locked = locked;
        foreach (var view in _views)
        {
            view.Cards.Enabled = !locked;
            view.Launcher.Enabled = !locked;
            view.QuickLaunch.Enabled = !locked;
            view.Dashboard.Enabled = !locked;
            view.Menus.Enabled = !locked;
            view.LayerBottom.Enabled = !locked;
            view.LayerTop.Enabled = !locked;
            if (view.Bar is { } bar)
            {
                bar.Model.Locked = locked;
            }

            ShowLockScreen(view, _ownLocked && !_sessionLocked);
            SyncDock(view, animate: false);
        }

        _overrideRedirect.Enabled = !locked;
        _lockState.Set(locked);
        if (!locked)
        {
            RestoreKeyboard();
        }

        _seat.Refresh();
        _outputs.ScheduleAll();
    }

    private void ShowLockScreen(LunaView view, bool shown)
    {
        if (view.LockUi is not { } lockUi)
        {
            return;
        }

        lockUi.Chrome.Enabled = true;
        lockUi.Chrome.InputEnabled = shown;
        lockUi.Fade.Start(shown ? LunaMotion.LockWindowFade : LunaMotion.LockFade, lockUi.Fade.Value, shown ? 1 : 0, Now, 1);
        lockUi.Frame.Alpha = (float)lockUi.Fade.Value;
        if (shown && ReferenceEquals(view, PlacementView()))
        {
            FocusLockSurface(lockUi);
        }

        view.Scheduler?.ScheduleRepaint();
    }

    private void FocusLockSurface(LockSurface lockUi)
    {
        if (lockUi.Chrome.Surface is { } surface)
        {
            _seat.Router.SetKeyboardFocus(surface);
            Avalonia.Threading.Dispatcher.UIThread.Post(() => lockUi.View.Focus(), Avalonia.Threading.DispatcherPriority.Background);
        }
    }

    private bool AdvanceLock(LunaView view, long now)
    {
        if (view.LockUi is not { } lockUi || !lockUi.Fade.Advance(now))
        {
            return false;
        }

        lockUi.Frame.Alpha = (float)lockUi.Fade.Value;
        if (!lockUi.Fade.IsRunning && lockUi.Fade.Value <= 0)
        {
            lockUi.Chrome.Enabled = false;
        }

        return lockUi.Fade.IsRunning;
    }

    private void SettleLock(LunaView view)
    {
        if (view.LockUi is { } lockUi && lockUi.Fade.IsRunning)
        {
            lockUi.Fade.Settle();
            lockUi.Frame.Alpha = (float)lockUi.Fade.Value;
            lockUi.Chrome.Enabled = lockUi.Fade.Value > 0;
        }
    }

    private void ShowPad(LunaView view, LockScreenModel model)
    {
        if (!_ownLocked || model.PadShown)
        {
            return;
        }

        model.PadShown = true;
        if (view.LockUi is { } lockUi)
        {
            FocusLockSurface(lockUi);
        }

        _report.Line($"UNLOCK pad {(model.Pin ? "pin" : "password")}");
    }

    private bool LockKey(uint timeMs, uint key, uint keysym, Modifiers held, bool pressed) => false;

    private LockScreenModel? ActiveLockModel => _views.Count > 0 ? PlacementView().LockUi?.Model : null;

    private void UnlockType(string text)
    {
        if (!_ownLocked || _views.Count == 0 || PlacementView() is not { LockUi: { } lockUi } view)
        {
            _report.Line("UNLOCK not-locked");
            return;
        }

        ShowPad(view, lockUi.Model);
        lockUi.Model.Entry = text;
    }

    private void UnlockSubmit()
    {
        if (ActiveLockModel is { } model)
        {
            SubmitUnlock(model);
        }
    }

    private void SubmitUnlock(LockScreenModel model)
    {
        if (!_ownLocked || model.Busy)
        {
            return;
        }

        if (model.Entry.Length == 0)
        {
            model.Message = model.Pin ? "Enter your PIN" : "Enter your password";
            return;
        }

        var password = Encoding.UTF8.GetBytes(model.Entry);
        model.Clear();
        model.Busy = true;
        model.Message = "Checking...";
        IReadOnlyList<string> argv = _options.PamHelperCommand is { Length: > 0 } stub
            ? ["sh", "-c", stub]
            : [Environment.ProcessPath ?? "luna-wm", "--pam-helper", _config.PamService, Environment.UserName];
        _pamCheck = PamCheck.Start(_host.Loop, argv, password, success => UnlockResult(model, success), out var error);
        Array.Clear(password);
        if (_pamCheck is null)
        {
            model.Busy = false;
            model.Message = "Cannot check the password";
            _log.Warn($"unlock check did not start: {error}");
            _report.Line("UNLOCK error");
            return;
        }

        _report.Line("UNLOCK checking");
    }

    private void UnlockResult(LockScreenModel model, bool success)
    {
        _pamCheck = null;
        model.Busy = false;
        if (success)
        {
            model.Message = "";
            _failures = 0;
            Unlock();
            return;
        }

        _failures++;
        model.Shakes++;
        _report.Line($"UNLOCK failed {_failures}");
        if (_failures < LockoutAttempts)
        {
            model.Message = model.Pin ? "Wrong PIN" : "Wrong password";
            return;
        }

        model.Busy = true;
        model.Message = "Try again in 30 seconds";
        _report.Line("UNLOCK lockout");
        _lockout = _host.Loop.AddTimer(() =>
        {
            _lockout?.Remove();
            _lockout = null;
            _failures = 0;
            model.Busy = false;
            model.Message = "";
            _report.Line("UNLOCK ready");
        });
        _lockout.UpdateTimer(LockoutMillis);
    }

    private void StartIdleLock()
    {
        _idleTimer?.Remove();
        _idleTimer = null;
        if (_config.IdleLockSeconds <= 0 || _services.Find<Basin.Capabilities.IIdleSource>() is not Basin.Seat.SeatIdleSource idle)
        {
            return;
        }

        _idleTimer = _host.Loop.AddTimer(() =>
        {
            var limit = _config.IdleLockSeconds * 1000L;
            var waited = idle.IdleMillis;
            if (!_ownLocked && limit > 0 && waited >= limit)
            {
                Lock();
            }

            _idleTimer?.UpdateTimer((int)Math.Max(1000, limit - (_ownLocked ? 0 : waited)));
        });
        _idleTimer.UpdateTimer(_config.IdleLockSeconds * 1000);
    }

    private void StopLock()
    {
        _idleTimer?.Remove();
        _idleTimer = null;
        _lockout?.Remove();
        _lockout = null;
        _pamCheck?.Dispose();
        _pamCheck = null;
    }
}
