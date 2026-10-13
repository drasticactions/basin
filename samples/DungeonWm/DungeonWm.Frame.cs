using Basin;
using Basin.Diagnostics;
using Basin.Host;
using Basin.Scene3D;
using Basin.Scene3D.Scene;

namespace DungeonWm;

internal sealed partial class DungeonWm
{

    private void OnBeforeRepaint(OutputView view)
    {
        var now = MonotonicClock.Nanos;
        var refresh = view.Output.CurrentMode.RefreshIntervalNanoseconds is var interval and > 0 ? interval : 16_666_667L;
        var animating = false;
        if (_lastStep == 0)
        {
            _lastStep = now;
        }

        var elapsed = now - _lastStep;
        if (elapsed > 1_000_000)
        {
            _lastStep = now;
            var seconds = elapsed / 1e9f;
            animating |= _walker.Step(seconds, _world.World);
            animating |= StepHangs(seconds);
            animating |= StepWorld(seconds, now);
            animating |= StepSnapshots(seconds);
            TrackRoom();
            CompletePendingTakeover();
            UpdateAudio();
            UpdateSounds(seconds);
        }

        if (_crosshair && ReferenceEquals(view, CrosshairView()))
        {
            UpdateReticle();
        }

        animating |= _walker.Moving || Hangs() || _snapshots.Count > 0 || WorldBusy();
        if (_takeover is not null || !_worldOutputs.TryGetValue(view, out var output))
        {
            return;
        }

        foreach (var window in _windows)
        {
            SyncLayers(window);
            _plaques.Sync(window, ReferenceEquals(_focused, window));
        }

        var tick = new FrameTick(Math.Max(view.Scheduler?.PredictedVblankNanos ?? now, now), refresh);
        var slice = _layout.BoxOf(view.Output);
        output.Render(_worldRenderer, _world.World, _camera, _layout.Bounds, slice, tick, _settings.Render, view.Width, view.Height, view.SwapModifiers);
        if (animating || _options.Frames > 0)
        {
            view.Scheduler?.ScheduleRepaint();
        }
    }

    private void OnPainted(OutputView view)
    {
        var time = (uint)Environment.TickCount;
        if (_worldOutputs.TryGetValue(view, out var output) && _takeover is null)
        {
            WorldFrameCallbacks.SendVisible(output.Visible, time);
            if (view.Scene?.Presented is { } presented)
            {
                WorldFrameCallbacks.AddPresented(presented, output.Visible);
            }
        }

        if (_frameCallbacks.FloorDue(MonotonicClock.Nanos))
        {
            foreach (var window in _windows)
            {
                if (!ReferenceEquals(window, _takeover))
                {
                    window.Scene.SendFrameDone(time);
                }
            }
        }

        UpdatePresence();
    }

    private void UpdatePresence()
    {
        _scene.CollectSurfaces(_presenceScratch);
        foreach (var (view, output) in _worldOutputs)
        {
            WorldFrameCallbacks.AddPresence(_presenceScratch, output.Visible, _layout.BoxOf(view.Output));
        }

        _presence.Update(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_presenceScratch));
    }

    private void TrackRoom()
    {
        if (_world.RoomAt(_camera.Position) is not { } room || ReferenceEquals(room, _workspaces.Active))
        {
            return;
        }

        _workspaces.SetActive(room);
        BasinReport.Line($"ROOM entered {room.Workspace + 1}");
        RaiseEvent("dungeonwm/room-entered");
    }

    private partial bool StepWorld(float seconds, long now);

    private partial bool WorldBusy();
}
