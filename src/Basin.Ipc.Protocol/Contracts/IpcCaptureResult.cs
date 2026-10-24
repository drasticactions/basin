using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// The reply of a <c>capture/*</c> method. Only the keys of the requested target are present.
/// </summary>
/// <param name="Width">The image width in pixels. For a file or inline PNG it is the size after scaling. For a descriptor it is the size of the raw pixels.</param>
/// <param name="Height">The image height in pixels, measured as <paramref name="Width"/> is.</param>
/// <param name="Path">The path of the written PNG file, or <see langword="null"/> for another target.</param>
/// <param name="Png">The PNG bytes, written as base64, or <see langword="null"/> for another target.</param>
/// <param name="Fd">The index of the descriptor among those that the reply carries, or <see langword="null"/> for another target.</param>
/// <param name="Format">The lowercase DRM format of the raw pixels, for example <c>xrgb8888</c>, or <see langword="null"/> for another target.</param>
/// <param name="Stride">The bytes in one row of the raw pixels, or <see langword="null"/> for another target.</param>
public readonly record struct IpcCaptureResult(
    int Width,
    int Height,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Path = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] byte[]? Png = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Fd = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Format = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Stride = null);
