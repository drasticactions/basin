using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// The reply of <c>session/describe</c>. The program fills most fields, and a field it does not fill is <see langword="null"/>.
/// </summary>
/// <param name="Compositor">The name of the compositor program.</param>
/// <param name="Backend">The backend name, for example <c>drm</c> or <c>nested</c>, or <see langword="null"/> when the program does not report it.</param>
/// <param name="Renderer">The renderer name, for example <c>gl</c>, or <see langword="null"/> when the program does not report it.</param>
/// <param name="WaylandSocket">The name of the Wayland socket that clients connect to, or <see langword="null"/> when the program does not report it.</param>
/// <param name="XwaylandDisplay">The X11 display, for example <c>:1</c>, or <see langword="null"/> when no Xwayland server runs.</param>
/// <param name="IpcSocket">The path of the control socket, or <see langword="null"/> when the compositor bound none.</param>
/// <param name="Outputs">The count of outputs. The key is absent when the session has no output set.</param>
/// <param name="Pid">The process id of the compositor. It reads as 0 from a compositor that does not send it.</param>
public readonly record struct IpcSessionDescription(
    string Compositor,
    string? Backend,
    string? Renderer,
    string? WaylandSocket,
    string? XwaylandDisplay,
    string? IpcSocket,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Outputs = null,
    int Pid = 0);
