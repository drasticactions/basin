using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// The data of the <c>diag/commit</c> event: one commit or one frame-done of a traced surface.
/// </summary>
/// <param name="Kind"><c>commit</c> for a surface commit, or <c>frame-done</c> when the compositor sent frame callbacks.</param>
/// <param name="Window">The id of the window whose root surface this is, or 0 for another surface.</param>
/// <param name="AppId">The app id of that window, or <see langword="null"/> for another surface.</param>
/// <param name="Width">The width of the committed buffer in buffer pixels. It is 0 without a buffer and for frame-done.</param>
/// <param name="Height">The height of the committed buffer in buffer pixels. It is 0 without a buffer and for frame-done.</param>
/// <param name="Format">The name of the DRM format of the committed buffer. Without a buffer and for frame-done, it names the invalid format.</param>
/// <param name="Damage">The buffer damage boxes, then the surface damage boxes. It is empty for frame-done.</param>
/// <param name="FrameCallbacks">For a commit, the frame callbacks in the committed state. For frame-done, the number of callbacks sent.</param>
/// <param name="Feedback">Whether the commit asked for presentation feedback.</param>
/// <param name="TimestampMs">The frame-done time in milliseconds. It is 0 for a commit.</param>
public readonly record struct IpcDiagCommit(
    string Kind,
    ulong Window,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AppId,
    int Width,
    int Height,
    string Format,
    IpcBox[] Damage,
    int FrameCallbacks,
    bool Feedback,
    uint TimestampMs);
