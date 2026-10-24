namespace Basin.Ipc;

/// <summary>
/// The state of one window frame at its last draw, in the reply of <c>diag/frames</c> and in the <c>diag/frame</c> event.
/// </summary>
/// <param name="Name">The app id or title of the framed window, or the debug name of the parent node of the frame.</param>
/// <param name="Window">The id of the framed window, or 0 when the frame has no known window.</param>
/// <param name="Draws">The number of times the frame was drawn.</param>
/// <param name="Animating">Whether the frame runs an animation.</param>
/// <param name="Visible">Whether the frame is visible.</param>
/// <param name="Outer">The outer bounds of the frame, relative to its parent node.</param>
/// <param name="Active">Whether the frame was drawn as active.</param>
/// <param name="Maximized">Whether the frame was drawn as maximized.</param>
/// <param name="Resizing">Whether the frame was drawn during a resize.</param>
/// <param name="Hot">The name of the part under the pointer.</param>
/// <param name="PreviousHot">The name of the part that was under the pointer before <paramref name="Hot"/>.</param>
/// <param name="Pressed">The name of the part that the pointer presses.</param>
/// <param name="Inside">Whether the pointer is in the frame.</param>
/// <param name="NowMs">The clock time of the last draw, in milliseconds.</param>
/// <param name="HoverMs">The clock time at which the hot part last changed, in milliseconds.</param>
/// <param name="ActiveMs">The clock time at which the active state last changed, in milliseconds.</param>
public readonly record struct IpcDiagFrame(
    string Name,
    ulong Window,
    long Draws,
    bool Animating,
    bool Visible,
    IpcBox Outer,
    bool Active,
    bool Maximized,
    bool Resizing,
    string Hot,
    string PreviousHot,
    string Pressed,
    bool Inside,
    long NowMs,
    long HoverMs,
    long ActiveMs);
