using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Win32.SafeHandles;

namespace Basin.Ipc;

/// <summary>
/// The result of a successful call: the result JSON and the descriptors that came with it. Disposal closes the descriptors.
/// </summary>
public sealed class IpcResult : IDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IpcResult"/> class.
    /// </summary>
    /// <param name="method">The called method.</param>
    /// <param name="json">The UTF-8 JSON of the result.</param>
    /// <param name="fds">The received descriptors in index order. The result owns them.</param>
    public IpcResult(string method, byte[] json, SafeFileHandle[] fds)
    {
        Method = method;
        Json = json;
        Fds = fds;
    }

    /// <summary>
    /// Gets the name of the called method.
    /// </summary>
    public string Method { get; }

    /// <summary>
    /// Gets the UTF-8 JSON of the result.
    /// </summary>
    public byte[] Json { get; }

    /// <summary>
    /// Gets the received descriptors in index order. The result owns them, so a caller that keeps one must stop <see cref="Dispose"/> from closing it.
    /// </summary>
    public IReadOnlyList<SafeFileHandle> Fds { get; }

    /// <summary>
    /// Makes a reader over the result JSON.
    /// </summary>
    /// <returns>A new reader at the start of the JSON.</returns>
    public Utf8JsonReader Reader() => new(Json);

    /// <summary>
    /// Deserializes the result into a contract.
    /// </summary>
    /// <typeparam name="T">The result contract type.</typeparam>
    /// <param name="info">The source-generated metadata of <typeparamref name="T"/>.</param>
    /// <returns>The result. It is <see langword="null"/> when the JSON is <c>null</c>.</returns>
    public T Read<T>(JsonTypeInfo<T> info) => JsonSerializer.Deserialize(Json, info)!;

    /// <summary>
    /// Finds a descriptor by the index that the result JSON names.
    /// </summary>
    /// <param name="index">The index.</param>
    /// <returns>The descriptor, or <see langword="null"/> when the index is out of range.</returns>
    public SafeFileHandle? Fd(int index) => index >= 0 && index < Fds.Count ? Fds[index] : null;

    /// <summary>
    /// Decodes the result JSON as text.
    /// </summary>
    /// <returns>The JSON text.</returns>
    public override string ToString() => System.Text.Encoding.UTF8.GetString(Json);

    /// <summary>
    /// Closes every received descriptor.
    /// </summary>
    public void Dispose()
    {
        foreach (var fd in Fds)
        {
            fd.Dispose();
        }
    }
}
