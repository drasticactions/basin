using System.Diagnostics;
using Basin;
using Basin.Capabilities;
using Basin.Cli;

namespace Westonia;

internal sealed partial class Westonia
{
    private void WireIdle()
    {
        if (_ini.Core.IdleTimeSeconds <= 0)
        {
            _log.Info($"weston.ini: idle locking is off, [core] idle-time is zero");
            return;
        }

        if (_services.Find<IIdleSource>() is not { } idle)
        {
            _log.Warn($"idle: no idle source, idle locking is off");
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
        if (_screensaver is { IsRunning: true } || _ini.Screensaver.Path is not { Length: > 0 } path)
        {
            return;
        }

        _screensaver = _processes.SpawnShell(path, out var error);
        if (_screensaver is null)
        {
            _log.Warn($"idle: cannot start the screensaver {path}, {CliMessage.Text(error ?? string.Empty)}");
            return;
        }

        _log.Info($"idle: started the screensaver {path}");
    }

    internal void StopScreensaver()
    {
        if (_screensaver is null)
        {
            return;
        }

        _processes.Kill(_screensaver.LaunchId);
        _screensaver = null;
    }
}
