using System.Runtime.InteropServices;
using Basin.Diagnostics;
using PipeWire;
using PipeWire.Native;
using PipeWire.Spa;
using static PipeWire.Native.Pipewire;

namespace Basin.Audio.PipeWire;

/// <summary>
/// Plays the sound effects of a compositor through one PipeWire output stream, F32 planar stereo at 48 kHz, with <see cref="Slots"/> mixing slots. The mix runs on the compositor thread and allocates nothing.
/// </summary>
public sealed unsafe class SoundPlayer : IDisposable
{
    /// <summary>
    /// The number of sounds that can play at the same time.
    /// </summary>
    public const int Slots = 16;

    private const int Channels = 2;
    private const int DefaultFrames = 256;

    private readonly AudioSession _session;
    private readonly PipeWireCore _core;
    private readonly PipeWireStream _stream;
    private readonly Slot[] _slots = new Slot[Slots];
    private long _started;
    private bool _active;
    private bool _disposed;

    private SoundPlayer(AudioSession session, PipeWireCore core, PipeWireStream stream, string name)
    {
        _session = session;
        _core = core;
        _stream = stream;
        Name = name;
        _stream.Process = _ => Mix();
        BasinCounters.Track();
    }

    /// <summary>
    /// Creates the player stream on a core of its own on the remote of the session, and waits until it has a node. The stream stays inactive until a sound plays. Do not call it inside a PipeWire callback, because it pumps the loop.
    /// </summary>
    /// <param name="session">The session that supplies the context and the remote.</param>
    /// <param name="name">The node and media name. <see cref="AudioStreams"/> never reports a node with this name.</param>
    /// <param name="error">Why creation failed, or <see langword="null"/> on success.</param>
    /// <param name="role">The <c>media.role</c> of the stream, or <see langword="null"/> for none.</param>
    /// <returns>The player, or <see langword="null"/> when PipeWire refuses the stream or creates no node within 2 seconds.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is <see langword="null"/>, empty or white space.</exception>
    public static SoundPlayer? Create(AudioSession session, string name, out string? error, string? role = "Game")
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        session.AddOwnNode(name);
        PipeWireStream? stream = null;
        PipeWireCore? core = null;
        try
        {
            using (var remote = PipeWireProperties.From("remote.name", session.Remote, "application.name", name))
            {
                core = session.Context.Connect(remote);
            }

            using var properties = PipeWireProperties.From(
                "media.type", "Audio",
                "media.class", "Stream/Output/Audio",
                "media.category", "Playback",
                "media.role", role,
                "media.name", name,
                "node.name", name,
                "node.latency", "256/48000",
                AudioStreams.VoiceKey, "true");
            stream = new PipeWireStream(core, name, properties);
            var player = new SoundPlayer(session, core, stream, name);
            ReadOnlySpan<spa_audio_channel> positions = [spa_audio_channel.SPA_AUDIO_CHANNEL_FL, spa_audio_channel.SPA_AUDIO_CHANNEL_FR];
            var format = SpaAudioFormats.BuildRaw(spa_audio_format.SPA_AUDIO_FORMAT_F32P, SoundClip.Rate, Channels, positions);
            stream.Connect(
                spa_direction.SPA_DIRECTION_OUTPUT,
                PW_ID_ANY,
                pw_stream_flags.PW_STREAM_FLAG_AUTOCONNECT | pw_stream_flags.PW_STREAM_FLAG_MAP_BUFFERS | pw_stream_flags.PW_STREAM_FLAG_INACTIVE,
                format);
            if (!session.WaitFor(() => stream.NodeId != PW_ID_ANY && stream.State >= pw_stream_state.PW_STREAM_STATE_PAUSED))
            {
                player.Dispose();
                error = "PipeWire did not create a node for the sound player";
                return null;
            }

            error = null;
            return player;
        }
        catch (PipeWireException failure)
        {
            stream?.Dispose();
            core?.Dispose();
            session.RemoveOwnNode(name);
            error = failure.Message;
            return null;
        }
    }

    /// <summary>
    /// Gets the node name of the player stream.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the PipeWire global id of the player node. Pass it to <see cref="SpatialVoice.Attach(uint)"/> to route the player through a voice.
    /// </summary>
    public uint NodeId => _stream.NodeId;

    /// <summary>
    /// Gets the <c>object.serial</c> of the player node.
    /// </summary>
    public ulong Serial => _stream.ObjectSerial;

    /// <summary>
    /// Gets the number of slots that play a clip now.
    /// </summary>
    public int Playing
    {
        get
        {
            var count = 0;
            foreach (ref readonly var slot in _slots.AsSpan())
            {
                count += slot.Clip is null ? 0 : 1;
            }

            return count;
        }
    }

    /// <summary>
    /// Gets the number of calls to <see cref="Play"/> that started a sound since creation.
    /// </summary>
    public long Started => _started;

    /// <summary>
    /// Starts a clip in a free slot. When all slots play, it replaces the sound that has played the most samples. It allocates nothing and activates the stream when it is inactive.
    /// </summary>
    /// <param name="clip">The clip to play.</param>
    /// <param name="placement">The gain, pan, low-pass and speed of this sound.</param>
    /// <returns><see langword="true"/> when the sound started. <see langword="false"/> when the player is disposed, the clip is empty, or the gain is at or below <see cref="VoiceParams.SilentDb"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="clip"/> is <see langword="null"/>.</exception>
    public bool Play(SoundClip clip, in SoundPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(clip);
        if (_disposed || placement.GainDb <= VoiceParams.SilentDb || clip.Samples.Length == 0)
        {
            return false;
        }

        var index = -1;
        var oldest = -1;
        var progress = -1.0;
        for (var i = 0; i < _slots.Length; i++)
        {
            if (_slots[i].Clip is null)
            {
                index = i;
                break;
            }

            if (_slots[i].Position > progress)
            {
                progress = _slots[i].Position;
                oldest = i;
            }
        }

        index = index >= 0 ? index : oldest;
        var gain = MathF.Pow(10, MathF.Min(20, placement.GainDb) / 20);
        var lateral = MathF.Sin(placement.Azimuth * MathF.PI / 180);
        var theta = (1 - lateral) * 0.25f * MathF.PI;
        var cutoff = Math.Clamp(placement.CutoffHz, 20, VoiceParams.OpenCutoffHz);
        _slots[index] = new Slot
        {
            Clip = clip,
            Position = 0,
            Rate = Math.Clamp(placement.Rate, 0.25f, 4),
            Left = gain * MathF.Cos(theta),
            Right = gain * MathF.Sin(theta),
            Alpha = cutoff >= VoiceParams.OpenCutoffHz ? 1 : 1 - MathF.Exp(-2 * MathF.PI * cutoff / SoundClip.Rate),
            State = 0,
        };
        _started++;
        if (!_active)
        {
            _active = true;
            _ = pw_stream_set_active(_stream.Handle, 1);
        }

        return true;
    }

    /// <summary>
    /// Stops every sound at once. The stream goes inactive at the next mix.
    /// </summary>
    public void StopAll()
    {
        Array.Clear(_slots);
    }

    /// <summary>
    /// Destroys the stream and its core, and removes the node name from the session. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stream.Process = null;
        _stream.Dispose();
        _core.Dispose();
        _session.RemoveOwnNode(Name);
        BasinCounters.Untrack();
    }

    private void Mix()
    {
        var buffer = _stream.DequeueBuffer();
        if (buffer.IsNull)
        {
            return;
        }

        if (buffer.DataCount < Channels)
        {
            _stream.QueueBuffer(buffer);
            return;
        }

        var leftData = buffer[0];
        var rightData = buffer[1];
        var capacity = Math.Min(leftData.Memory.Length, rightData.Memory.Length) / sizeof(float);
        var frames = (int)Math.Min((ulong)capacity, buffer.RequestedFrames > 0 ? buffer.RequestedFrames : DefaultFrames);
        var left = MemoryMarshal.Cast<byte, float>(leftData.Memory)[..frames];
        var right = MemoryMarshal.Cast<byte, float>(rightData.Memory)[..frames];
        left.Clear();
        right.Clear();
        var any = false;
        for (var s = 0; s < _slots.Length; s++)
        {
            ref var slot = ref _slots[s];
            if (slot.Clip is not { } clip)
            {
                continue;
            }

            var samples = clip.Samples;
            var last = samples.Length - 1;
            var position = slot.Position;
            var state = slot.State;
            var i = 0;
            for (; i < frames && position < last; i++)
            {
                var index = (int)position;
                var t = (float)(position - index);
                var sample = samples[index] + ((samples[index + 1] - samples[index]) * t);
                state += slot.Alpha * (sample - state);
                left[i] += state * slot.Left;
                right[i] += state * slot.Right;
                position += slot.Rate;
            }

            slot.State = state;
            slot.Position = position;
            if (position >= last)
            {
                slot = default;
            }
            else
            {
                any = true;
            }
        }

        leftData.SetChunk(0, (uint)(frames * sizeof(float)), sizeof(float));
        rightData.SetChunk(0, (uint)(frames * sizeof(float)), sizeof(float));
        _stream.QueueBuffer(buffer);
        if (!any && _active)
        {
            _active = false;
            _ = pw_stream_set_active(_stream.Handle, 0);
        }
    }

    private struct Slot
    {
        public SoundClip? Clip;
        public double Position;
        public float Rate;
        public float Left;
        public float Right;
        public float Alpha;
        public float State;
    }
}
