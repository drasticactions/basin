using Basin.Diagnostics;

namespace Basin;

/// <summary>
/// Maps client <c>wl_buffer</c> resource handles to <see cref="IBuffer"/> instances, so repeated attaches see one identity. An entry drops out when its buffer is destroyed.
/// </summary>
public sealed class ClientBufferRegistry
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly Dictionary<nint, IBuffer> _byResource = [];
    private readonly List<nint> _dead = [];
    private Action? _forget;

    /// <summary>Gets the number of buffers that the registry holds.</summary>
    public int Count => _byResource.Count;

    /// <summary>
    /// Records the buffer that backs a <c>wl_buffer</c> resource. A protocol that creates the resource itself calls this. A second call for the same handle replaces the entry.
    /// </summary>
    /// <param name="bufferResourceHandle">The native handle of the <c>wl_buffer</c> resource.</param>
    /// <param name="buffer">The buffer that backs the resource.</param>
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

    /// <summary>
    /// Finds the buffer for a resource. An unknown libwayland shm buffer is wrapped in a <see cref="ShmClientBuffer"/> and recorded.
    /// </summary>
    /// <param name="bufferResourceHandle">The native handle of the <c>wl_buffer</c> resource, or zero.</param>
    /// <returns>The buffer, or <see langword="null"/> when the handle is zero or names a resource that is neither registered nor libwayland shm.</returns>
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
