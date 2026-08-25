using System.Diagnostics;
using Basin;
using Basin.Capabilities;

namespace Westonia;

internal sealed partial class Westonia
{
    private void WireIdle()
    {
        if (_ini.Core.IdleTimeSeconds <= 0)
        {
            _log.Info($"idle locking is off: [core] idle-time is zero");
            return;
        }

        if (_services.Find<IIdleSource>() is not { } idle)
        {
            _log.Warn($"no idle source; the idle timer will not fire");
            return;
        }

        _idleTimer = _host.Loop.AddTimer(() => OnIdleTick(idle));
        idle.Activity += () => OnActivity(idle);
        ArmIdle(idle);
    }

    private void OnActivity(IIdleSource idle)
    {
        StopScreensaver();
        ArmIdle(idle);
    }

    private void ArmIdle(IIdleSource idle)
    {
        if (_idleTimer is null || _idleTimer.IsRemoved)
        {
            return;
        }

        var remaining = (_ini.Core.IdleTimeSeconds * 1000L) - idle.IdleMillis;
        _idleTimer.UpdateTimer((int)Math.Clamp(remaining, 1, int.MaxValue));
    }

    private void OnIdleTick(IIdleSource idle)
    {
        if (idle.IsInhibited)
        {
            ArmIdle(idle);
            return;
        }

        if (idle.IdleMillis < _ini.Core.IdleTimeSeconds * 1000L)
        {
            ArmIdle(idle);
            return;
        }

        StartScreensaver();
        _lock?.Lock();
    }

    internal void StartScreensaver()
    {
        if (_screensaver is { HasExited: false } || _ini.Screensaver.Path is not { Length: > 0 } path)
        {
            return;
        }

        try
        {
            _screensaver = Basin.Diagnostics.BasinDiagnostics.StartClient(path, _host.Socket);
            _log.Info($"started the screensaver: {path}");
        }
        catch (Exception error)
        {
            _log.Error($"cannot start the screensaver {path}: {error.Message}");
        }
    }

    internal void StopScreensaver()
    {
        if (_screensaver is null)
        {
            return;
        }

        Basin.Diagnostics.BasinDiagnostics.StopClient(_screensaver);
        _screensaver = null;
    }
}
