using System.Diagnostics;
using Basin;
using Basin.Capabilities;
using Microsoft.Extensions.Logging;

namespace Westonia;

internal sealed partial class Westonia
{
    private void WireIdle()
    {
        if (_ini.Core.IdleTimeSeconds <= 0)
        {
            _log.LogInformation("idle locking is off: [core] idle-time is zero");
            return;
        }

        if (_services.Find<IIdleSource>() is not { } idle)
        {
            _log.LogWarning("no idle source; the idle timer will not fire");
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
            var info = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
            info.ArgumentList.Add("-c");
            info.ArgumentList.Add(path);
            info.Environment["WAYLAND_DISPLAY"] = _host.Socket;
            _screensaver = Process.Start(info);
            _log.LogInformation("started the screensaver: {Path}", path);
        }
        catch (Exception error)
        {
            _log.LogError("cannot start the screensaver {Path}: {Reason}", path, error.Message);
        }
    }

    internal void StopScreensaver()
    {
        if (_screensaver is null)
        {
            return;
        }

        try
        {
            if (!_screensaver.HasExited)
            {
                _screensaver.Kill(entireProcessTree: true);
            }
        }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException)
        {
        }

        _screensaver.Dispose();
        _screensaver = null;
    }
}
