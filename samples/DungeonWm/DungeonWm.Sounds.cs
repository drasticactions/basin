using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Basin;
using Basin.Audio.PipeWire;
using Basin.Diagnostics;
using Basin.Physics;

namespace DungeonWm;

internal sealed partial class DungeonWm
{
    private const float WalkInterval = 0.53f;
    private const float RunInterval = 0.4f;
    private const float SettleAfter = 0.25f;
    private const float WalkCutoff = 5000;
    private const float StepAzimuth = 8;
    private const double ImpactMinSpeed = 0.4;
    private const double HeavyImpactSpeed = 2.5;
    private static readonly TimeSpan ImpactGap = TimeSpan.FromMilliseconds(90);

    private readonly Dictionary<PhysicsBody3D, long> _lastImpact = [];
    private AudioSession? _effectsSession;
    private SoundPlayer? _effects;
    private SoundBank? _bank;
    private SpatialVoice? _roomVoice;
    private MapRegion? _roomVoiceRegion;
    private string _soundState = "off";
    private SoundClip? _lastFootstep;
    private float _sinceStep;
    private bool _striding;
    private bool _leftFoot;
    private float _fallSpeed;
    private bool _roomVoiceScheduled;
    private Action? _roomVoiceIdle;
    private int _steps;
    private int _jumps;
    private int _landings;
    private int _impacts;
    private int _hangs;
    private int _clicks;

    private float EffectsDb => _settings.Sounds.Volume <= 0 ? VoiceParams.SilentDb : 20 * MathF.Log10(_settings.Sounds.Volume);

    private void StartSounds()
    {
        if (!_settings.Sounds.Enabled)
        {
            _soundState = "off";
            return;
        }

        if (Environment.GetEnvironmentVariable("BASIN_MUTE") == "1")
        {
            _soundState = "muted";
            BasinReport.Line("SOUNDS muted");
            return;
        }

        _bank ??= SoundBank.Load();
        _effectsSession = AudioSession.TryCreate(_host.Loop, out var reason, clientName: "dungeon-wm-sounds");
        if (_effectsSession is null)
        {
            _soundState = "unavailable";
            BasinReport.Line($"SOUNDS unavailable: {reason}");
            return;
        }

        _effects = SoundPlayer.Create(_effectsSession, $"dungeon-wm.{Environment.ProcessId}.sounds", out var error);
        if (_effects is null)
        {
            _effectsSession.Dispose();
            _effectsSession = null;
            _soundState = "unavailable";
            BasinReport.Line($"SOUNDS unavailable: {error}");
            return;
        }

        _soundState = "on";
        RequestRoomVoice();
    }

    private void StopSounds()
    {
        _roomVoice?.Forget(_effects?.NodeId ?? 0);
        _roomVoice?.Dispose();
        _roomVoice = null;
        _roomVoiceRegion = null;
        _effects?.Dispose();
        _effects = null;
        _effectsSession?.Dispose();
        _effectsSession = null;
        _lastImpact.Clear();
    }

    private void ApplySounds(SoundSettings before)
    {
        if (before.Enabled != _settings.Sounds.Enabled)
        {
            StopSounds();
            StartSounds();
        }
    }

    private void RequestRoomVoice()
    {
        if (_effects is null || _roomVoiceScheduled)
        {
            return;
        }

        _roomVoiceScheduled = true;
        _host.Loop.AddIdle(_roomVoiceIdle ??= UpdateRoomVoice);
    }

    private void UpdateRoomVoice()
    {
        _roomVoiceScheduled = false;
        if (_effects is not { } player || _effectsSession is not { } session)
        {
            return;
        }

        var region = _settings.Audio.Reverb > 0 ? _doorways.RegionAt(_camera.Position) : null;
        if (ReferenceEquals(region, _roomVoiceRegion) && (_roomVoice is not null || region is null))
        {
            return;
        }

        SpatialVoice? voice = null;
        if (region is not null)
        {
            var name = string.Create(CultureInfo.InvariantCulture, $"dungeon-wm.{Environment.ProcessId}.room{++_nextVoice}");
            voice = SpatialVoice.Create(session, new VoiceGraph(VoiceMode.Stereo, null, ImpulseFor(region)), name, out var error, "dungeon-wm room");
            if (voice is null)
            {
                _log.Warn($"audio: no room voice for the effects: {error}");
            }
            else
            {
                voice.Update(VoiceParams.Centered with { WetDb = 20 * MathF.Log10(_settings.Audio.Reverb) });
                voice.Attach(player.NodeId);
            }
        }

        var previous = _roomVoice;
        _roomVoice = voice;
        _roomVoiceRegion = region;
        if (previous is not null)
        {
            previous.Forget(player.NodeId);
            previous.Dispose();
        }
    }

    private void UpdateSounds(float seconds)
    {
        if (!_walker.Grounded)
        {
            _fallSpeed = MathF.Max(_fallSpeed, -_walker.Velocity.Y);
        }

        if (_effects is null)
        {
            return;
        }

        if (!ReferenceEquals(_doorways.RegionAt(_camera.Position), _roomVoiceRegion))
        {
            RequestRoomVoice();
        }

        _sinceStep += seconds;
        var quiet = _takeover is not null || _walker.Flying;
        if (_avatar is { WalkContacts: not null } avatar)
        {
            if (!quiet && avatar.StepFoot >= 0)
            {
                PlayStep(avatar.StepFoot, avatar.StepRunning, settle: false);
            }

            Settle(avatar.Striding && !quiet);
            return;
        }

        var horizontal = new Vector2(_walker.Velocity.X, _walker.Velocity.Z).Length();
        var striding = !quiet && _walker.Grounded && horizontal > 0.3f;
        if (striding && _sinceStep >= (_walker.Run ? RunInterval : WalkInterval))
        {
            PlayStep(_leftFoot ? 1 : 0, _walker.Run, settle: false);
        }

        Settle(striding);
    }

    private void Settle(bool striding)
    {
        if (_striding && !striding && _walker.Grounded && _sinceStep > SettleAfter)
        {
            PlayStep(_leftFoot ? 1 : 0, running: false, settle: true);
        }

        _striding = striding;
    }

    private void PlayStep(int foot, bool running, bool settle)
    {
        _sinceStep = 0;
        _leftFoot = foot == 0;
        _steps++;
        var bank = _bank!;
        _lastFootstep = bank.PickOther(bank.Steps, _lastFootstep);
        var level = settle ? -15 : running ? -6 : -10;
        _effects?.Play(_lastFootstep, new SoundPlacement(
            EffectsDb + level + bank.Jitter(2),
            foot == 0 ? StepAzimuth : 360 - StepAzimuth,
            running ? VoiceParams.OpenCutoffHz : WalkCutoff,
            running ? bank.Between(0.98f, 1.08f) : bank.Between(0.93f, 1.03f)));
    }

    private void SoundJump()
    {
        if (_effects is null)
        {
            return;
        }

        _jumps++;
        PlayNear(_bank!.Pick(_bank.Jumps), -8 + _bank.Jitter(1), 0);
    }

    private void SoundLand()
    {
        var speed = _fallSpeed;
        _fallSpeed = 0;
        if (_effects is null)
        {
            return;
        }

        _landings++;
        _sinceStep = 0;
        var level = Math.Clamp(20 * MathF.Log10(MathF.Max(0.5f, speed) / 6), -18, 0);
        var bank = _bank!;
        _lastFootstep = bank.PickOther(bank.Steps, _lastFootstep);
        _effects.Play(_lastFootstep, new SoundPlacement(EffectsDb + level - 4, 0, VoiceParams.OpenCutoffHz, bank.Between(0.9f, 0.98f)));
        PlayNear(bank.Pick(bank.Landings), level - 6, 0);
    }

    private void SoundImpact(in PhysicsImpact3D impact)
    {
        if (_effects is null || _physics is not { } physics || impact.Speed < ImpactMinSpeed)
        {
            return;
        }

        PhysicsBody3D body;
        if (physics.WindowOf(impact.A) is not null || physics.SnapshotOf(impact.A) is not null)
        {
            body = impact.A;
        }
        else if (physics.WindowOf(impact.B) is not null || physics.SnapshotOf(impact.B) is not null)
        {
            body = impact.B;
        }
        else
        {
            return;
        }

        var now = Stopwatch.GetTimestamp();
        if (_lastImpact.TryGetValue(body, out var last) && Stopwatch.GetElapsedTime(last, now) < ImpactGap)
        {
            return;
        }

        if (_lastImpact.Count > 64)
        {
            _lastImpact.Clear();
        }

        _lastImpact[body] = now;
        _impacts++;
        var heavy = impact.Speed >= HeavyImpactSpeed;
        var level = Math.Clamp(20 * MathF.Log10((float)impact.Speed / 4), -24, 0);
        PlayAt(_bank!.Pick(heavy ? _bank.HeavyImpacts : _bank.LightImpacts), impact.Point, level);
    }

    private void SoundHung(DungeonWmWindow window)
    {
        if (_effects is null)
        {
            return;
        }

        _hangs++;
        PlayAt(_bank!.Latch, window.World.Position, -6);
    }

    private void SoundPlaque(DungeonWmWindow window)
    {
        if (_effects is null)
        {
            return;
        }

        _clicks++;
        PlayAt(_bank!.Click, window.World.Position, -8);
    }

    private void PlayNear(SoundClip clip, float levelDb, float azimuth) =>
        _effects?.Play(clip, new SoundPlacement(EffectsDb + levelDb, azimuth));

    private void PlayAt(SoundClip clip, Vector3 position, float levelDb)
    {
        var path = _doorways.Path(_camera.Position, position);
        var placed = Acoustics.Place(_camera.Position, _camera.Orientation, path, _settings.Audio, pan: true);
        _effects?.Play(clip, new SoundPlacement(placed.GainDb + EffectsDb + levelDb, placed.Azimuth, placed.CutoffHz));
    }

    private void PrintSounds() =>
        _report.Line(string.Create(CultureInfo.InvariantCulture,
            $"SOUNDS state={_soundState} played={_effects?.Started ?? 0} playing={_effects?.Playing ?? 0} steps={_steps} jumps={_jumps} landings={_landings} impacts={_impacts} hangs={_hangs} clicks={_clicks} room={(_roomVoice?.Graph.ImpulseFile is { } file ? Path.GetFileNameWithoutExtension(file) : "none")}"));
}
