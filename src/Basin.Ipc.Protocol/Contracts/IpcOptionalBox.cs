using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// A box that can be absent, written as a box object or as JSON <see langword="null"/>.
/// It replaces a nullable <see cref="IpcBox"/>, which leaves the fast serializer path.
/// </summary>
/// <param name="Value">The box, or <see langword="null"/> for no box.</param>
[JsonConverter(typeof(IpcOptionalBoxConverter))]
public readonly record struct IpcOptionalBox(IpcBox? Value)
{
    /// <summary>
    /// Wraps a box that is present.
    /// </summary>
    /// <param name="box">The box to wrap.</param>
    /// <returns>A value that holds the box.</returns>
    public static implicit operator IpcOptionalBox(IpcBox box) => new(box);
}
