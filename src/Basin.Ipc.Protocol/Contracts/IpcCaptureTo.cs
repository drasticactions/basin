using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// The <c>to</c> key of a capture: where the image goes.
/// </summary>
/// <param name="Kind">The target kind.</param>
/// <param name="Path">The absolute file path for <see cref="IpcCaptureKind.Path"/>, otherwise <see langword="null"/>.</param>
[JsonConverter(typeof(IpcCaptureToConverter))]
public readonly record struct IpcCaptureTo(IpcCaptureKind Kind, string? Path = null)
{
    /// <summary>
    /// Gets the target that returns the PNG in the reply.
    /// </summary>
    public static IpcCaptureTo Inline { get; } = new(IpcCaptureKind.Inline);

    /// <summary>
    /// Gets the target that returns a descriptor of raw pixels.
    /// </summary>
    public static IpcCaptureTo Fd { get; } = new(IpcCaptureKind.Fd);

    /// <summary>
    /// Makes the target that writes a PNG file.
    /// </summary>
    /// <param name="path">The file path. The server refuses a relative path.</param>
    /// <returns>A target of kind <see cref="IpcCaptureKind.Path"/>.</returns>
    public static IpcCaptureTo ToPath(string path) => new(IpcCaptureKind.Path, path);
}
