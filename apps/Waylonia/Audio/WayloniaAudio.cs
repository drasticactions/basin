using static Waylonia.WayloniaLog;

namespace Waylonia.Audio;

internal sealed class WayloniaAudio : IDisposable
{
    public const int Rate = 48000;

    public const int Channels = 2;

    private readonly AudioRing _ring;
    private readonly AudioSink _sink;
    private readonly RemoteAudioSource _source;

    private WayloniaAudio(AudioRing ring, AudioSink sink, RemoteAudioSource source)
    {
        _ring = ring;
        _sink = sink;
        _source = source;
    }

    public AudioRing Ring => _ring;

    public static bool Wanted(bool audio, string? sshHost, string? waypipeListen)
    {
        if (!audio)
        {
            return false;
        }

        if (waypipeListen is not null)
        {
            Log.Warn($"--audio carries sound over a connection of its own and --waypipe-listen opens none, so this session stays silent");
            return false;
        }

        if (sshHost is null)
        {
            Log.Warn($"--audio carries a remote session's sound and this session is local, where the application already plays to this host's sound server");
            return false;
        }

        return true;
    }

    public static WayloniaAudio? TryStart(string sshHost, string? controlPath, string sink, string format)
    {
        var ring = AudioRing.ForSession(Rate, Channels);
        var device = AudioSink.TryCreate(ring, Rate, Channels, out var whyNot);
        if (device is null)
        {
            Log.Warn($"this host has no playback device, so the session has no sound: {whyNot}");
            return null;
        }

        var source = new RemoteAudioSource(ring, sshHost, controlPath, sink, Rate, Channels, format == "s16");
        source.Start();
        Log.Info($"playing {sshHost}'s sound on this host from {sink}.monitor as {format}");
        return new WayloniaAudio(ring, device, source);
    }

    public void Dispose()
    {
        _sink.Dispose();
        _source.Dispose();
        Log.Debug($"audio ended with {_ring.Underruns} underrun(s) and {_ring.Dropped} sample(s) dropped");
    }
}
