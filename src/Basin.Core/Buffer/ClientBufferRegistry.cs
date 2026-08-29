using Basin.Diagnostics;

namespace Basin;

public sealed class ClientBufferRegistry
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly Dictionary<nint, IBuffer> _byResource = [];
    private readonly List<nint> _dead = [];
    private Action? _forget;

    public int Count => _byResource.Count;

    public void Register(nint bufferResourceHandle, IBuffer buffer)
    {
        _thread.Assert();
        _byResource[bufferResourceHandle] = buffer;
        Watch(buffer);
    }

    private void Watch(IBuffer buffer)
    {
        _forget ??= ForgetDestroyed;
        buffer.Destroyed += _forget;
    }

    private void ForgetDestroyed()
    {
        foreach (var pair in _byResource)
        {
            if (pair.Value.IsDestroyed)
            {
                _dead.Add(pair.Key);
            }
        }

        foreach (var handle in _dead)
        {
            if (_byResource.Remove(handle, out var buffer) && _forget is { } forget)
            {
                buffer.Destroyed -= forget;
            }
        }

        _dead.Clear();
    }

    public IBuffer? GetOrImport(nint bufferResourceHandle)
    {
        _thread.Assert();
        if (bufferResourceHandle == 0)
        {
            return null;
        }

        if (_byResource.TryGetValue(bufferResourceHandle, out var existing))
        {
            return existing;
        }

        var imported = ShmClientBuffer.FromResource(bufferResourceHandle);
        if (imported is null)
        {
            return null;
        }

        _byResource[bufferResourceHandle] = imported;
        Watch(imported);
        return imported;
    }
}
