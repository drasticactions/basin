using System.Numerics;
using Basin;
using Basin.Diagnostics;

namespace DungeonWm;

internal sealed partial class DungeonWm
{
    private void Reload()
    {
        if (_options.NoConfig || _options.ConfigPath is not { } path)
        {
            BasinReport.Line("RELOAD skipped no-config");
            return;
        }

        var loaded = DungeonWmConfig.Load(path, none: false, _log, out _, out var fatal);
        if (fatal is not null)
        {
            _log.Warn($"config: reload failed, the running config stays: {fatal}");
            BasinReport.Line("RELOAD failed");
            return;
        }

        var restart = DungeonWmConfig.RestartChanges(_settings, loaded);
        var avatarChanged = AvatarChanged(_settings, loaded);
        var sizeChanged = loaded.PixelsPerMeter != _settings.PixelsPerMeter || !loaded.Frames.SameGeometry(_settings.Frames);
        var audioBefore = _settings.Audio;
        var soundsBefore = _settings.Sounds;
        CarryOver(loaded);
        ApplyLive(sizeChanged);
        ApplyInspectorSettings();
        _outputs.ConfigureHandoff(_settings.Handoff.Transition == Basin.Config.HandoffTransitionKind.Crossfade, _settings.Handoff.DurationMs, _settings.Handoff.HoldLimitMs);
        if (audioBefore != _settings.Audio)
        {
            ApplyAudio(audioBefore);
            _roomVoiceRegion = null;
            RequestRoomVoice();
        }

        if (soundsBefore != _settings.Sounds)
        {
            ApplySounds(soundsBefore);
        }

        if (avatarChanged)
        {
            StartAvatar();
        }

        BasinReport.Line(
            $"RELOAD bindings={_settings.Bindings.Count} avatar={(avatarChanged ? "reloaded" : "kept")}"
            + (restart.Count == 0 ? string.Empty : $" restart-required={string.Join(',', restart)}"));
    }

    private void CarryOver(DungeonWmSettings loaded)
    {
        _settings.Render.RoomDepth = loaded.Render.RoomDepth;
        _settings.Render.MaxLights = loaded.Render.MaxLights;
        _settings.Render.DirectSampleMax = loaded.Render.DirectSampleMax;
        _settings.Render.Anisotropy = loaded.Render.Anisotropy;
        _settings.Launch = loaded.Launch;
        _settings.FieldOfView = loaded.FieldOfView;
        _settings.Near = loaded.Near;
        _settings.Far = loaded.Far;
        _settings.LookSensitivity = loaded.LookSensitivity;
        _settings.CrosshairOutput = loaded.CrosshairOutput;
        _settings.Reach = loaded.Reach;
        _settings.ReticleColor = loaded.ReticleColor;
        _settings.ReticleSize = loaded.ReticleSize;
        _settings.WalkSpeed = loaded.WalkSpeed;
        _settings.RunSpeed = loaded.RunSpeed;
        _settings.EyeHeight = loaded.EyeHeight;
        _settings.SkyColor = loaded.SkyColor;
        _settings.PixelsPerMeter = loaded.PixelsPerMeter;
        _settings.Frames = loaded.Frames;
        _settings.WindowDepth = loaded.WindowDepth;
        _settings.HangHeight = loaded.HangHeight;
        _settings.EdgeColor = loaded.EdgeColor;
        _settings.FocusColor = loaded.FocusColor;
        _settings.UnfocusedOpacity = loaded.UnfocusedOpacity;
        _settings.OpenMilliseconds = loaded.OpenMilliseconds;
        _settings.CloseMilliseconds = loaded.CloseMilliseconds;
        _settings.Gravity = loaded.Gravity;
        _settings.PullDistance = loaded.PullDistance;
        _settings.RehangDistance = loaded.RehangDistance;
        _settings.ThrowScale = loaded.ThrowScale;
        _settings.ThrowMinSpeed = loaded.ThrowMinSpeed;
        _settings.Audio = loaded.Audio;
        _settings.Sounds = loaded.Sounds;
        _settings.AvatarModel = loaded.AvatarModel;
        _settings.AvatarScale = loaded.AvatarScale;
        _settings.AvatarShow = loaded.AvatarShow;
        _settings.AvatarSampleHz = loaded.AvatarSampleHz;
        _settings.AvatarClips.Clear();
        foreach (var (state, clip) in loaded.AvatarClips)
        {
            _settings.AvatarClips[state] = clip;
        }

        _settings.InspectorEnabled = loaded.InspectorEnabled;
        _settings.InspectorOpen = loaded.InspectorOpen;
        _settings.InspectorOutput = loaded.InspectorOutput;
        _settings.InspectorPanels = loaded.InspectorPanels;
        _settings.InspectorFontSize = loaded.InspectorFontSize;
        _settings.InspectorRefreshHz = loaded.InspectorRefreshHz;
        _settings.Bindings.Clear();
        foreach (var (chord, action) in loaded.Bindings)
        {
            _settings.Bindings[chord] = action;
        }
    }

    private void ApplyLive(bool sizeChanged)
    {
        _camera.FieldOfView = _settings.FieldOfViewRadians;
        _camera.Near = _settings.Near;
        _camera.Far = _settings.Far;
        var sky = new RenderColor(_settings.SkyColor.X, _settings.SkyColor.Y, _settings.SkyColor.Z, 1);
        _world.World.ClearColor = _settings.SkyColor;
        _capturePack.Capture.Background = sky;
        _outputs.Background = sky;
        if (_physics is { } physics)
        {
            physics.World.Gravity = new Vector3(0, -_settings.Gravity, 0);
        }

        _bindings = new DungeonWmBindings(_settings.Bindings, _log);
        DefineReticles();
        Relock();
        if (sizeChanged)
        {
            _frames.Configure(_settings.Frames, _settings.Map.Seed);
        }

        foreach (var window in _windows.ToArray())
        {
            if (window.State != DungeonWmWindowState.Hung || window.Room is not { } room)
            {
                continue;
            }

            if (sizeChanged)
            {
                Hang(window, room, window.Slot);
            }
            else
            {
                SyncSize(window);
                PlaceOnSlot(window);
            }
        }

        ScheduleAll();
    }

    private static bool AvatarChanged(DungeonWmSettings running, DungeonWmSettings loaded) =>
        running.AvatarModel != loaded.AvatarModel ||
        running.AvatarScale != loaded.AvatarScale ||
        running.AvatarSampleHz != loaded.AvatarSampleHz ||
        !running.AvatarShow.SequenceEqual(loaded.AvatarShow) ||
        running.AvatarClips.Count != loaded.AvatarClips.Count ||
        running.AvatarClips.Any(pair => !loaded.AvatarClips.TryGetValue(pair.Key, out var clip) || clip != pair.Value);
}
