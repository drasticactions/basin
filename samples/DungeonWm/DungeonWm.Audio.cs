using System.Diagnostics;
using System.Globalization;
using Basin;
using Basin.Audio.PipeWire;
using Basin.Diagnostics;

namespace DungeonWm;

internal sealed partial class DungeonWm
{
    private static readonly TimeSpan RebalanceInterval = TimeSpan.FromSeconds(1);

    private readonly Dictionary<long, int> _launchGroups = [];
    private readonly HashSet<uint> _unmatched = [];
    private readonly HashSet<uint> _released = [];
    private readonly Dictionary<uint, WindowSound> _streamSounds = [];
    private readonly Dictionary<DungeonWmWindow, WindowSound> _sounds = [];
    private readonly Dictionary<DungeonWmSnapshot, WindowSound> _fadingSounds = [];
    private readonly List<WindowSound> _soundScratch = [];
    private readonly DoorwayGraph _doorways = new();
    private readonly Dictionary<MapRegion, string> _impulses = [];
    private AudioSession? _audio;
    private AudioStreams? _audioStreams;
    private AudioStreamOwners? _audioOwners;
    private AudioOutputWatch? _audioOutput;
    private string? _hrtfUnavailable;
    private bool _hrtfReported;
    private float _hrtfRadiusMin = 1;
    private float _hrtfRadiusMax = 1;
    private VoiceMode _graphMode = VoiceMode.Pan;
    private bool _audioBusy;
    private bool _audioIdleScheduled;
    private bool _rebuildPending;
    private Action? _audioIdle;
    private readonly List<SpatialVoice> _retiredVoices = [];
    private string? _audioModeOverride;
    private long _nextLaunchTag;
    private long _pendingLaunchTag;
    private long _seenLaunch;
    private int _nextVoice;
    private bool _voicesDirty;
    private long _lastRebalance;

    private string AudioMode => _audioModeOverride ?? _settings.Audio.Mode;

    private VoiceMode EffectiveVoiceMode => _graphMode;

    private VoiceMode ChooseGraph()
    {
        var wantsHrtf = AudioMode switch
        {
            "hrtf" => true,
            "auto" => _audioOutput?.Kind == AudioOutputKind.Headphones,
            _ => false,
        };
        if (!wantsHrtf)
        {
            return VoiceMode.Pan;
        }

        if (_hrtfUnavailable is { } reason)
        {
            if (!_hrtfReported)
            {
                _hrtfReported = true;
                BasinReport.Line($"AUDIO hrtf unavailable: {reason}");
            }

            return VoiceMode.Pan;
        }

        return VoiceMode.Hrtf;
    }

    private void ProbeHrtf()
    {
        _hrtfReported = false;
        _hrtfUnavailable = SofaFile.TryProbe(_settings.Audio.Hrtf, out var min, out var max, out var reason) ? null : reason;
        (_hrtfRadiusMin, _hrtfRadiusMax) = (MathF.Max(0.01f, min), MathF.Max(min, max));
    }

    private void OnOutputChanged(AudioOutputWatch watch)
    {
        if (watch.Kind == AudioOutputKind.Unknown && watch.SinkName is not null)
        {
            return;
        }

        var chosen = ChooseGraph();
        if (chosen == _graphMode)
        {
            return;
        }

        _graphMode = chosen;
        BasinReport.Line($"AUDIO output={watch.Kind.ToString().ToLowerInvariant()} graph={GraphName(chosen)}");
        RebuildVoices();
    }

    private static string GraphName(VoiceMode mode) => mode == VoiceMode.Hrtf ? "hrtf" : "pan";

    private void RebuildVoices()
    {
        _rebuildPending = true;
        RequestAudio();
    }

    private void RequestAudio()
    {
        _voicesDirty = true;
        if (_audio is not null && !_audioIdleScheduled)
        {
            _audioIdleScheduled = true;
            _host.Loop.AddIdle(_audioIdle ??= RunDeferredAudio);
        }
    }

    private void RebuildDoorways()
    {
        _doorways.Build(_world.Map, _world.Unit);
        _impulses.Clear();
    }

    private void StartAudio()
    {
        if (AudioMode == "off")
        {
            return;
        }

        _audio = AudioSession.TryCreate(_host.Loop, out var reason, clientName: "dungeon-wm");
        if (_audio is null)
        {
            BasinReport.Line($"AUDIO unavailable: {reason}");
            return;
        }

        ProbeHrtf();
        _audioOutput = new AudioOutputWatch(_audio);
        _audioOutput.Changed += OnOutputChanged;
        _graphMode = ChooseGraph();
        _audioOwners = new AudioStreamOwners(_toplevels, LaunchGroup);
        _audioStreams = new AudioStreams(_audio);
        _audioStreams.Added += OnStreamAdded;
        _audioStreams.Changed += OnStreamChanged;
        _audioStreams.Removed += OnStreamRemoved;
        foreach (var stream in _audioStreams.Streams.ToArray())
        {
            OnStreamAdded(stream);
        }
    }

    private void StopAudio()
    {
        _audioBusy = true;
        _audio?.Roundtrip(200);
        foreach (var sound in AllSounds())
        {
            DropVoice(sound);
        }

        DisposeRetiredVoices();
        _audioBusy = false;

        _sounds.Clear();
        _fadingSounds.Clear();
        _streamSounds.Clear();
        _audioStreams?.Dispose();
        _audioStreams = null;
        _audioOwners?.Dispose();
        _audioOwners = null;
        _audioOutput?.Dispose();
        _audioOutput = null;
        _audio?.Dispose();
        _audio = null;
        _unmatched.Clear();
        _released.Clear();
    }

    private void ApplyAudio(AudioSettings before)
    {
        var after = _settings.Audio;
        var rebuild = _audioModeOverride is not null || before.Mode != after.Mode || before.Hrtf != after.Hrtf ||
            before.UpdateHz != after.UpdateHz || before.ReverbMaxSeconds != after.ReverbMaxSeconds ||
            (before.Reverb > 0) != (after.Reverb > 0);
        _audioModeOverride = null;
        if (rebuild)
        {
            RestartAudio();
        }
        else
        {
            RequestAudio();
        }
    }

    private void RestartAudio()
    {
        if (AudioMode == "off")
        {
            StopAudio();
            return;
        }

        if (_audio is null)
        {
            StartAudio();
            return;
        }

        ProbeHrtf();
        _impulses.Clear();
        _graphMode = ChooseGraph();
        RebuildVoices();
    }

    private List<WindowSound> AllSounds()
    {
        var all = new List<WindowSound>(_sounds.Values);
        all.AddRange(_fadingSounds.Values);
        return all;
    }

    private Dictionary<string, string?> LaunchEnvironment()
    {
        var values = ProcessTracker.SessionEnvironment(_host.Socket, _xServer?.DisplayName, _ipc?.Path, _xwaylandModule?.ChildEnvironment());
        ClaimPendingLaunch();
        _pendingLaunchTag = ++_nextLaunchTag;
        foreach (var (name, value) in LaunchTag(_pendingLaunchTag))
        {
            values[name] = value;
        }

        return values;
    }

    private static Dictionary<string, string> LaunchTag(long tag)
    {
        var id = tag.ToString(CultureInfo.InvariantCulture);
        var pipewire = Environment.GetEnvironmentVariable("PIPEWIRE_PROPS") is { Length: > 0 } existing
            ? $"{{ {existing.Trim().TrimStart('{').TrimEnd('}')} {AudioStreams.LaunchKey} = {id} }}"
            : $"{{ {AudioStreams.LaunchKey} = {id} }}";
        var pulse = Environment.GetEnvironmentVariable("PULSE_PROP") is { Length: > 0 } list
            ? $"{list} {AudioStreams.LaunchKey}='{id}'"
            : $"{AudioStreams.LaunchKey}='{id}'";
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["PIPEWIRE_PROPS"] = pipewire,
            ["PULSE_PROP"] = pulse,
        };
    }

    private Dictionary<string, string> InitLaunchTag(out long tag)
    {
        tag = ++_nextLaunchTag;
        return LaunchTag(tag);
    }

    private void ClaimPendingLaunch()
    {
        if (_pendingLaunchTag == 0)
        {
            return;
        }

        foreach (var process in _processes.Processes)
        {
            if (process.LaunchId > _seenLaunch)
            {
                _seenLaunch = process.LaunchId;
                _launchGroups[_pendingLaunchTag] = process.Pid;
            }
        }
    }

    private int LaunchGroup(long tag)
    {
        ClaimPendingLaunch();
        return _launchGroups.GetValueOrDefault(tag);
    }

    private void OnStreamAdded(AudioStream stream) => TryOwn(stream, report: true);

    private void OnStreamChanged(AudioStream stream)
    {
        if (_streamSounds.TryGetValue(stream.NodeId, out var sound))
        {
            var index = sound.Streams.FindIndex(s => s.NodeId == stream.NodeId);
            if (index >= 0)
            {
                sound.Streams[index] = stream;
            }
        }
        else if (_unmatched.Contains(stream.NodeId))
        {
            TryOwn(stream, report: false);
        }
    }

    private void OnStreamRemoved(AudioStream stream)
    {
        _unmatched.Remove(stream.NodeId);
        _released.Remove(stream.NodeId);
        if (_streamSounds.Remove(stream.NodeId, out var sound))
        {
            sound.Streams.RemoveAll(s => s.NodeId == stream.NodeId);
            sound.Voice?.Forget(stream.NodeId);
            if (sound.Streams.Count == 0)
            {
                ForgetSound(sound);
            }
        }
    }

    private bool TryOwn(AudioStream stream, bool report)
    {
        if (_audioOwners is not { } owners || _streamSounds.ContainsKey(stream.NodeId) || _released.Contains(stream.NodeId))
        {
            return false;
        }

        var owner = owners.Match(stream);
        if (owner.Matched && WindowById(owner.ToplevelId) is { } window)
        {
            _unmatched.Remove(stream.NodeId);
            if (!_sounds.TryGetValue(window, out var sound))
            {
                sound = new WindowSound(window, owner.ViaName);
                _sounds[window] = sound;
            }

            sound.Streams.Add(stream);
            _streamSounds[stream.NodeId] = sound;
            sound.Voice?.Attach(stream);
            BasinReport.Line($"AUDIO MATCH stream={stream.NodeId} app={stream.Name} window={window.ToplevelId} via={owner.ViaName}");
            RequestAudio();
            return true;
        }

        if (_unmatched.Add(stream.NodeId) && report)
        {
            BasinReport.Line($"AUDIO UNMATCHED stream={stream.NodeId} app={stream.Name}");
        }

        return false;
    }

    private void AudioWindowMapped(DungeonWmWindow window)
    {
        if (_audioStreams is not { } streams || _unmatched.Count == 0)
        {
            return;
        }

        foreach (var id in _unmatched.ToArray())
        {
            if (streams.Find(id) is { } stream)
            {
                TryOwn(stream, report: false);
            }
        }
    }

    private void AudioWindowUnmapped(DungeonWmWindow window, DungeonWmSnapshot? snapshot)
    {
        if (!_sounds.Remove(window, out var sound))
        {
            return;
        }

        sound.Window = null;
        if (snapshot is not null && sound.Voice is not null)
        {
            sound.Snapshot = snapshot;
            _fadingSounds[snapshot] = sound;
            UpdateAudio();
            return;
        }

        ForgetSound(sound);
    }

    private void AudioSnapshotDropped(DungeonWmSnapshot snapshot)
    {
        if (_fadingSounds.Remove(snapshot, out var sound))
        {
            ForgetSound(sound);
        }
    }

    private void ForgetSound(WindowSound sound)
    {
        DropVoice(sound);
        if (sound.Window is { } window)
        {
            _sounds.Remove(window);
        }

        if (sound.Snapshot is { } snapshot)
        {
            _fadingSounds.Remove(snapshot);
        }

        foreach (var stream in sound.Streams)
        {
            _streamSounds.Remove(stream.NodeId);
            _unmatched.Add(stream.NodeId);
        }

        sound.Streams.Clear();
        _voicesDirty = true;
    }

    private void DropVoice(WindowSound sound)
    {
        if (sound.Voice is not { } voice)
        {
            return;
        }

        sound.Voice = null;
        voice.Released -= OnVoiceReleased;
        foreach (var stream in voice.Streams.ToArray())
        {
            voice.Detach(stream);
        }

        _retiredVoices.Add(voice);
        RaiseAudioEvent("removed", sound, voice);
        RequestAudio();
    }

    private void RaiseAudioEvent(string change, WindowSound sound, SpatialVoice voice)
    {
        if (_ipc is not { } ipc || !ipc.Events.HasSubscribers("dungeonwm/audio"))
        {
            return;
        }

        var json = ipc.Events.BeginEvent("dungeonwm/audio");
        json.WriteStartObject();
        json.WriteString("change"u8, change);
        json.WriteNumber("window"u8, sound.Id);
        json.WriteString("app"u8, sound.App);
        json.WriteString("voice"u8, voice.Name);
        json.WriteString("graph"u8, GraphName(voice.Graph.Mode));
        json.WriteNumber("streams"u8, sound.Streams.Count);
        json.WriteEndObject();
        ipc.Events.EndEvent();
    }

    private void DisposeRetiredVoices()
    {
        foreach (var voice in _retiredVoices)
        {
            voice.Dispose();
        }

        _retiredVoices.Clear();
    }

    private void OnVoiceReleased(SpatialVoice voice, uint streamId)
    {
        _released.Add(streamId);
        BasinReport.Line($"AUDIO RELEASED stream={streamId} reason=moved");
        if (_streamSounds.Remove(streamId, out var sound))
        {
            sound.Streams.RemoveAll(s => s.NodeId == streamId);
            if (sound.Streams.Count == 0)
            {
                DropVoice(sound);
                if (sound.Window is { } window)
                {
                    _sounds.Remove(window);
                }

                if (sound.Snapshot is { } snapshot)
                {
                    _fadingSounds.Remove(snapshot);
                }
            }
        }
    }

    private void UpdateAudio()
    {
        if (_audio is null)
        {
            return;
        }

        if (_audioBusy)
        {
            RequestAudio();
            return;
        }

        _audioBusy = true;
        try
        {
            if (_rebuildPending)
            {
                _rebuildPending = false;
                foreach (var sound in AllSounds())
                {
                    DropVoice(sound);
                }
            }

            if (_voicesDirty || Stopwatch.GetElapsedTime(_lastRebalance) >= RebalanceInterval)
            {
                RebalanceVoices();
            }

            foreach (var sound in _sounds.Values)
            {
                Place(sound);
            }

            foreach (var sound in _fadingSounds.Values)
            {
                Place(sound);
            }
        }
        finally
        {
            _audioBusy = false;
        }
    }

    private void RunDeferredAudio()
    {
        _audioIdleScheduled = false;
        if (_audioBusy)
        {
            RequestAudio();
            return;
        }

        _audioBusy = true;
        try
        {
            DisposeRetiredVoices();
        }
        finally
        {
            _audioBusy = false;
        }

        UpdateAudio();
    }

    private void Place(WindowSound sound)
    {
        if (sound.Window is { } window && ReferenceEquals(window, _takeover))
        {
            sound.Path = new SoundPath(_camera.Position, 0, 0, true);
            sound.Wanted = VoiceParams.Centered;
        }
        else
        {
            var hint = sound.Window?.Room ?? sound.Snapshot?.Room;
            sound.Path = _doorways.Path(_camera.Position, sound.Source, hint);
            var region = _doorways.RegionAt(sound.Source) ?? hint;
            if (!ReferenceEquals(region, sound.Region))
            {
                sound.Region = region;
                RequestAudio();
            }

            sound.Wanted = Acoustics.Place(_camera.Position, _camera.Orientation, sound.Path, _settings.Audio, EffectiveVoiceMode == VoiceMode.Pan);
        }

        sound.Voice?.Update(sound.Wanted);
    }

    private void RebalanceVoices()
    {
        _voicesDirty = false;
        _lastRebalance = Stopwatch.GetTimestamp();
        if (_audio is not { } session)
        {
            return;
        }

        _soundScratch.Clear();
        foreach (var sound in _sounds.Values)
        {
            Place(sound);
            _soundScratch.Add(sound);
        }

        foreach (var sound in _fadingSounds.Values)
        {
            Place(sound);
            _soundScratch.Add(sound);
        }

        _soundScratch.Sort(LouderFirst.Instance);
        var cap = Math.Max(0, _settings.Audio.MaxVoices);
        for (var i = 0; i < _soundScratch.Count; i++)
        {
            var sound = _soundScratch[i];
            if (i >= cap)
            {
                DropVoice(sound);
                _voicesDirty = false;
                continue;
            }

            if (!_rebuildPending && (sound.Voice is null || !GraphMatches(sound.Voice.Graph, sound)))
            {
                CreateVoice(session, sound);
            }
        }

        _soundScratch.Clear();
    }

    private VoiceGraph GraphFor(WindowSound sound)
    {
        var impulse = sound.Region is { } region && _settings.Audio.Reverb > 0 ? ImpulseFor(region) : null;
        return EffectiveVoiceMode == VoiceMode.Hrtf
            ? new VoiceGraph(VoiceMode.Hrtf, _settings.Audio.Hrtf, impulse) { HrtfRadiusMin = _hrtfRadiusMin, HrtfRadiusMax = _hrtfRadiusMax }
            : new VoiceGraph(VoiceMode.Pan, null, impulse);
    }

    private bool GraphMatches(VoiceGraph graph, WindowSound sound)
    {
        var impulse = sound.Region is { } region && _settings.Audio.Reverb > 0 ? ImpulseFor(region) : null;
        return graph.Mode == EffectiveVoiceMode && graph.ImpulseFile == impulse &&
            (graph.Mode == VoiceMode.Pan || graph.HrtfFile == _settings.Audio.Hrtf);
    }

    private string ImpulseFor(MapRegion region)
    {
        if (!_impulses.TryGetValue(region, out var path))
        {
            path = RoomImpulse.PathFor(Acoustics.ReverbSeconds(region, _world.Unit, _settings.Audio.ReverbMaxSeconds));
            _impulses[region] = path;
        }

        return path;
    }

    private void CreateVoice(AudioSession session, WindowSound sound)
    {
        var name = string.Create(CultureInfo.InvariantCulture, $"dungeon-wm.{Environment.ProcessId}.voice{++_nextVoice}");
        var graph = GraphFor(sound);
        var voice = SpatialVoice.Create(session, graph, name, out var error, $"dungeon-wm {sound.App}");
        if (voice is null && graph.Mode == VoiceMode.Hrtf)
        {
            _hrtfUnavailable = error;
            _graphMode = ChooseGraph();
            graph = GraphFor(sound);
            voice = SpatialVoice.Create(session, graph, name, out error, $"dungeon-wm {sound.App}");
        }

        if (voice is null)
        {
            _log.Warn($"no voice for {sound.App}: {error}");
            return;
        }

        var live = (sound.Window is { } window && _sounds.TryGetValue(window, out var owner) && ReferenceEquals(owner, sound)) ||
            (sound.Snapshot is { } snapshot && _fadingSounds.TryGetValue(snapshot, out var fading) && ReferenceEquals(fading, sound));
        if (!live || sound.Streams.Count == 0 || (sound.Voice is { } current && GraphMatches(current.Graph, sound)))
        {
            voice.Dispose();
            return;
        }

        voice.MinHrtfInterval = TimeSpan.FromSeconds(1.0 / Math.Max(1, _settings.Audio.UpdateHz));
        voice.Released += OnVoiceReleased;
        voice.Update(sound.Wanted);
        var previous = sound.Voice;
        foreach (var stream in sound.Streams)
        {
            previous?.Forget(stream.NodeId);
            voice.Attach(stream);
        }

        sound.Voice = voice;
        RaiseAudioEvent(previous is null ? "created" : "swapped", sound, voice);
        if (previous is not null)
        {
            previous.Released -= OnVoiceReleased;
            _retiredVoices.Add(previous);
            RequestAudio();
        }
    }

    private void PrintAudio()
    {
        UpdateAudio();
        var voices = 0;
        foreach (var sound in _sounds.Values)
        {
            voices += sound.Voice is null ? 0 : 1;
        }

        foreach (var sound in _fadingSounds.Values)
        {
            voices += sound.Voice is null ? 0 : 1;
        }

        _report.Line(_audio is null
            ? $"AUDIO mode=off voices={voices} unmatched={_unmatched.Count}"
            : $"AUDIO mode={AudioMode} voices={voices} unmatched={_unmatched.Count} graph={GraphName(_graphMode)} output={(_audioOutput?.Kind ?? AudioOutputKind.Unknown).ToString().ToLowerInvariant()}");
        foreach (var sound in _sounds.Values)
        {
            PrintVoice(sound);
        }

        foreach (var sound in _fadingSounds.Values)
        {
            PrintVoice(sound);
        }
    }

    private void PrintVoice(WindowSound sound)
    {
        var wanted = sound.Voice?.Current ?? sound.Wanted;
        var path = sound.Path.Doorways == 0 ? "direct" : $"doorways:{sound.Path.Doorways}";
        _report.Line(string.Create(CultureInfo.InvariantCulture,
            $"VOICE {sound.Id} app={sound.App} streams={sound.Streams.Count} via={sound.Via} az={Whole(Acoustics.Signed(wanted.Azimuth))} el={Whole(wanted.Elevation)} dist={sound.Path.Length:F2} path={path} gain={Db(wanted.GainDb)} cutoff={wanted.CutoffHz:F0} wet={Db(wanted.WetDb)} region={RegionName(sound.Region)} reverb={ReverbName(sound)}{(sound.Voice is null ? " voice=none" : string.Empty)}"));
    }

    private static string RegionName(MapRegion? region) => region is null
        ? "none"
        : region.Kind == RegionKind.Room ? $"room{region.Workspace + 1}" : $"corridor{region.Id}";

    private static string ReverbName(WindowSound sound) =>
        sound.Voice?.Graph.ImpulseFile is { } file ? System.IO.Path.GetFileNameWithoutExtension(file) : "none";

    private static float Whole(float value) => MathF.Round(value) + 0f;

    private static string Db(float db) => db <= VoiceParams.SilentDb ? "-inf" : (MathF.Round(db, 2) + 0f).ToString("F2", CultureInfo.InvariantCulture);

    private DungeonWmWindow? WindowById(ulong toplevelId)
    {
        foreach (var window in _windows)
        {
            if (window.ToplevelId == toplevelId)
            {
                return window;
            }
        }

        return null;
    }

    private sealed class LouderFirst : IComparer<WindowSound>
    {
        public static readonly LouderFirst Instance = new();

        public int Compare(WindowSound? x, WindowSound? y) => (y?.Wanted.GainDb ?? 0).CompareTo(x?.Wanted.GainDb ?? 0);
    }
}
