using Basin.Audio.PipeWire;
using Basin.Capabilities;

namespace TabWm;

internal sealed class TabAudio : IDisposable
{
    private readonly AudioSession _session;
    private readonly AudioStreams _streams;
    private readonly AudioStreamOwners _owners;
    private readonly HashSet<uint> _muted = [];

    public TabAudio(AudioSession session, IToplevelModel toplevels, Func<long, int> launchGroup)
    {
        _session = session;
        _streams = new AudioStreams(session);
        _owners = new AudioStreamOwners(toplevels, launchGroup);
        _streams.Added += _ => Changed?.Invoke();
        _streams.Changed += _ => Changed?.Invoke();
        _streams.Removed += stream =>
        {
            _muted.Remove(stream.NodeId);
            Changed?.Invoke();
        };
    }

    public event Action? Changed;

    public IEnumerable<(AudioStream Stream, ulong ToplevelId)> Owned()
    {
        foreach (var stream in _streams.Streams.ToArray())
        {
            var owner = _owners.Match(stream);
            if (owner.Matched)
            {
                yield return (stream, owner.ToplevelId);
            }
        }
    }

    public void SetMuted(AudioStream stream, bool muted)
    {
        if (_muted.Contains(stream.NodeId) == muted)
        {
            return;
        }

        if (_streams.SetMuted(stream.NodeId, muted))
        {
            if (muted)
            {
                _muted.Add(stream.NodeId);
            }
            else
            {
                _muted.Remove(stream.NodeId);
            }
        }
    }

    public void Dispose()
    {
        _owners.Dispose();
        _streams.Dispose();
        _session.Dispose();
    }
}
