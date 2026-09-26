using System.Text.Json.Serialization;

namespace Basin.Ipc;

[JsonConverter(typeof(IpcCaptureToConverter))]
public readonly record struct IpcCaptureTo(IpcCaptureKind Kind, string? Path = null)
{
    public static IpcCaptureTo Inline { get; } = new(IpcCaptureKind.Inline);

    public static IpcCaptureTo Fd { get; } = new(IpcCaptureKind.Fd);

    public static IpcCaptureTo ToPath(string path) => new(IpcCaptureKind.Path, path);
}
