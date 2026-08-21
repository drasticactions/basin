using Basin.Diagnostics;

namespace Basin;

public sealed class ClientBufferRegistry
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly Dictionary<nint, IBuffer> _byResource = [];

    public int Count => _byResource.Count;

    public void Register(nint bufferResourceHandle, IBuffer buffer)
    {
        _thread.Assert();
        _byResource[bufferResourceHandle] = buffer;
        buffer.Destroyed += () => _byResource.Remove(bufferResourceHandle);
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
        imported.Destroyed += () => _byResource.Remove(bufferResourceHandle);
        return imported;
    }
}
