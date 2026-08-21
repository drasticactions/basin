namespace Basin.Capabilities.Defaults;

public sealed class FrameClock : IFrameClock
{
    private IFrameSink[] _sinks = [];

    public void Add(IFrameSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        if (Array.IndexOf(_sinks, sink) >= 0)
        {
            return;
        }

        var grown = new IFrameSink[_sinks.Length + 1];
        Array.Copy(_sinks, grown, _sinks.Length);
        grown[^1] = sink;
        _sinks = grown;
    }

    public void Remove(IFrameSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        var index = Array.IndexOf(_sinks, sink);
        if (index < 0)
        {
            return;
        }

        var shrunk = new IFrameSink[_sinks.Length - 1];
        Array.Copy(_sinks, shrunk, index);
        Array.Copy(_sinks, index + 1, shrunk, index, shrunk.Length - index);
        _sinks = shrunk;
    }

    public void BeginFrame(IOutput output, long predictedVblankNanos)
    {
        var sinks = _sinks;
        for (var i = 0; i < sinks.Length; i++)
        {
            sinks[i].BeginFrame(output, predictedVblankNanos);
        }
    }

    public void EndFrame(IOutput output, long presentedNanos)
    {
        var sinks = _sinks;
        for (var i = 0; i < sinks.Length; i++)
        {
            sinks[i].EndFrame(output, presentedNanos);
        }
    }
}
