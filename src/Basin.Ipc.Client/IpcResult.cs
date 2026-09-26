using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Win32.SafeHandles;

namespace Basin.Ipc;

public sealed class IpcResult : IDisposable
{
    public IpcResult(string method, byte[] json, SafeFileHandle[] fds)
    {
        Method = method;
        Json = json;
        Fds = fds;
    }

    public string Method { get; }

    public byte[] Json { get; }

    public IReadOnlyList<SafeFileHandle> Fds { get; }

    public Utf8JsonReader Reader() => new(Json);

    public T Read<T>(JsonTypeInfo<T> info) => JsonSerializer.Deserialize(Json, info)!;

    public SafeFileHandle? Fd(int index) => index >= 0 && index < Fds.Count ? Fds[index] : null;

    public override string ToString() => System.Text.Encoding.UTF8.GetString(Json);

    public void Dispose()
    {
        foreach (var fd in Fds)
        {
            fd.Dispose();
        }
    }
}
