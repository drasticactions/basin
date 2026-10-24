namespace Basin.Ipc;

/// <summary>
/// The names of the methods that the library registers. A consumer cannot register a method in one of the <see cref="ReservedNamespaces"/>.
/// </summary>
public static class IpcMethodNames
{
    /// <summary>
    /// Names the method that answers the protocol version, the compositor name and the basin version. It takes no params.
    /// </summary>
    public const string Version = "ipc/version";
    /// <summary>
    /// Names the method that lists the registered methods. With <c>detail</c> set, it answers the description, schema and traits of each method.
    /// </summary>
    public const string Methods = "ipc/methods";
    /// <summary>
    /// Names the method that lists every declared event name, sorted.
    /// </summary>
    public const string Events = "ipc/events";
    /// <summary>
    /// Names the method that subscribes the connection to the events in <c>events</c>, with optional <c>options</c> for each event.
    /// </summary>
    public const string Subscribe = "ipc/subscribe";
    /// <summary>
    /// Names the method that ends the subscriptions in <c>events</c>. Without <c>events</c>, it ends every subscription of the connection.
    /// </summary>
    public const string Unsubscribe = "ipc/unsubscribe";

    /// <summary>
    /// Names the method that describes the session: the compositor, backend, renderer, sockets and process id.
    /// </summary>
    public const string SessionDescribe = "session/describe";
    /// <summary>
    /// Names the method that calls the program's stop action. It is absent when the program has no stop action.
    /// </summary>
    public const string SessionQuit = "session/quit";
    /// <summary>
    /// Names the method that answers when every output presented what it was asked to draw. It is absent when the program passes no settle source.
    /// </summary>
    public const string SessionSettle = "session/settle";

    /// <summary>
    /// Names the method that lists the outputs with their modes, geometry and state.
    /// </summary>
    public const string OutputsList = "outputs/list";
    /// <summary>
    /// Names the method that tests an output configuration without applying it.
    /// </summary>
    public const string OutputsTest = "outputs/test";
    /// <summary>
    /// Names the method that applies an output configuration. Each entry names one output and only what changes on it.
    /// </summary>
    public const string OutputsApply = "outputs/apply";
    /// <summary>
    /// Names the method that turns the power of an output on or off.
    /// </summary>
    public const string OutputsPower = "outputs/power";
    /// <summary>
    /// Names the method that applies a <c>.cube</c> look to an output. An empty or missing path removes the look.
    /// </summary>
    public const string OutputsLook = "outputs/look";

    /// <summary>
    /// Names the method that lists the windows as window descriptors.
    /// </summary>
    public const string WindowsList = "windows/list";
    /// <summary>
    /// Names the method that answers the descriptor of one window.
    /// </summary>
    public const string WindowsGet = "windows/get";

    /// <summary>
    /// Names the method that limits the frame rate of a window, from 0 for no limit to 1000 frames a second.
    /// </summary>
    public const string WindowsFrameLimit = "windows/frame-limit";

    /// <summary>
    /// Names the method that answers the commit and present statistics of a window for the last second.
    /// </summary>
    public const string WindowsStats = "windows/stats";
    /// <summary>
    /// Names the method that answers the window ids from bottom to top.
    /// </summary>
    public const string WindowsStack = "windows/stack";
    /// <summary>
    /// Names the method that asks the compositor to activate a window. Compositor policy can refuse it.
    /// </summary>
    public const string WindowsActivate = "windows/activate";
    /// <summary>
    /// Names the method that asks a window to close.
    /// </summary>
    public const string WindowsClose = "windows/close";
    /// <summary>
    /// Names the method that asks the compositor to change the maximized, minimized or fullscreen state of a window.
    /// </summary>
    public const string WindowsSetState = "windows/set-state";
    /// <summary>
    /// Names the method that asks the compositor to move a window to a point in layout coordinates.
    /// </summary>
    public const string WindowsMove = "windows/move";
    /// <summary>
    /// Names the method that asks the compositor to resize a window.
    /// </summary>
    public const string WindowsResize = "windows/resize";
    /// <summary>
    /// Names the method that moves a window to a named output.
    /// </summary>
    public const string WindowsSendToOutput = "windows/send-to-output";
    /// <summary>
    /// Names the method that moves a window to a workspace.
    /// </summary>
    public const string WindowsSendToWorkspace = "windows/send-to-workspace";
    /// <summary>
    /// Names the method that answers when a window matches the given app id or title, or fails at its timeout. Its reply can arrive after later replies.
    /// </summary>
    public const string WindowsWait = "windows/wait";
    /// <summary>
    /// Names the method that answers when a window, or every window, made no counting commit for the quiet period.
    /// </summary>
    public const string WindowsWaitIdle = "windows/wait-idle";
    /// <summary>
    /// Names the method that moves an X11 window to the native or the low-density X server.
    /// </summary>
    public const string WindowsSetX11Density = "windows/set-x11-density";

    /// <summary>
    /// Names the method that lists the workspace groups and their workspaces.
    /// </summary>
    public const string WorkspacesList = "workspaces/list";
    /// <summary>
    /// Names the method that activates a workspace.
    /// </summary>
    public const string WorkspacesActivate = "workspaces/activate";
    /// <summary>
    /// Names the method that deactivates a workspace.
    /// </summary>
    public const string WorkspacesDeactivate = "workspaces/deactivate";
    /// <summary>
    /// Names the method that creates a workspace in a group.
    /// </summary>
    public const string WorkspacesCreate = "workspaces/create";
    /// <summary>
    /// Names the method that removes a workspace.
    /// </summary>
    public const string WorkspacesRemove = "workspaces/remove";
    /// <summary>
    /// Names the method that moves a workspace to another group.
    /// </summary>
    public const string WorkspacesMove = "workspaces/move";

    /// <summary>
    /// Names the method that answers the idle time and whether idle is inhibited.
    /// </summary>
    public const string IdleStatus = "idle/status";
    /// <summary>
    /// Names the method that reports user activity and resets the idle timers.
    /// </summary>
    public const string IdleActivity = "idle/activity";
    /// <summary>
    /// Names the method that takes an idle inhibitor. The inhibitor belongs to the connection and goes when the connection closes.
    /// </summary>
    public const string IdleInhibit = "idle/inhibit";
    /// <summary>
    /// Names the method that releases an idle inhibitor by its token.
    /// </summary>
    public const string IdleUninhibit = "idle/uninhibit";

    /// <summary>
    /// Names the method that answers whether the session is locked.
    /// </summary>
    public const string LockStatus = "lock/status";

    /// <summary>
    /// Names the method that answers the active keymap, optionally with its text or a duplicate of its memfd.
    /// </summary>
    public const string KeyboardKeymap = "keyboard/keymap";

    /// <summary>
    /// Names the method that moves the pointer through the synthetic input path, before bindings and focus policy.
    /// </summary>
    public const string InputPointerMove = "input/pointer-move";
    /// <summary>
    /// Names the method that presses or releases a pointer button through the synthetic input path.
    /// </summary>
    public const string InputPointerButton = "input/pointer-button";
    /// <summary>
    /// Names the method that sends a scroll axis event through the synthetic input path.
    /// </summary>
    public const string InputAxis = "input/axis";
    /// <summary>
    /// Names the method that presses or releases an evdev key through the synthetic input path.
    /// </summary>
    public const string InputKey = "input/key";
    /// <summary>
    /// Names the method that presses a <c>Mod+Key</c> chord in order and releases it in reverse, through the synthetic input path.
    /// </summary>
    public const string InputChord = "input/chord";
    /// <summary>
    /// Names the method that types text through the focused text input, or through the keymap.
    /// </summary>
    public const string InputText = "input/text";
    /// <summary>
    /// Names the method that sends a touch event through the synthetic input path.
    /// </summary>
    public const string InputTouch = "input/touch";

    /// <summary>
    /// Names the method that moves the pointer straight on the seat, after bindings and focus policy.
    /// </summary>
    public const string SeatPointerMove = "seat/pointer-move";
    /// <summary>
    /// Names the method that presses or releases a pointer button straight on the seat.
    /// </summary>
    public const string SeatPointerButton = "seat/pointer-button";
    /// <summary>
    /// Names the method that sends a scroll axis event straight to the seat.
    /// </summary>
    public const string SeatAxis = "seat/axis";
    /// <summary>
    /// Names the method that presses or releases a key on the injected keyboard of the connection.
    /// </summary>
    public const string SeatKey = "seat/key";
    /// <summary>
    /// Names the method that presses a <c>Mod+Key</c> chord on the injected keyboard of the connection.
    /// </summary>
    public const string SeatChord = "seat/chord";
    /// <summary>
    /// Names the method that types text straight to the seat. It takes no keymap override.
    /// </summary>
    public const string SeatText = "seat/text";
    /// <summary>
    /// Names the method that sends a touch event straight to the seat.
    /// </summary>
    public const string SeatTouch = "seat/touch";

    /// <summary>
    /// Names the method that captures an output to a PNG file, an inline PNG or a descriptor of raw pixels.
    /// </summary>
    public const string CaptureOutput = "capture/output";
    /// <summary>
    /// Names the method that captures one window to a PNG file, an inline PNG or a descriptor of raw pixels.
    /// </summary>
    public const string CaptureWindow = "capture/window";
    /// <summary>
    /// Names the method that captures a box in layout coordinates to a PNG file, an inline PNG or a descriptor of raw pixels.
    /// </summary>
    public const string CaptureRegion = "capture/region";

    /// <summary>
    /// Names the method that starts a program from an argv array with no shell, and tracks it.
    /// </summary>
    public const string ProcessSpawn = "process/spawn";
    /// <summary>
    /// Names the method that lists every tracked launch, running or finished.
    /// </summary>
    public const string ProcessList = "process/list";
    /// <summary>
    /// Names the method that sends SIGTERM to the process group of a launch, then SIGKILL after the grace period.
    /// </summary>
    public const string ProcessKill = "process/kill";
    /// <summary>
    /// Names the method that answers the last lines of the log file of a launch.
    /// </summary>
    public const string ProcessLog = "process/log";

    /// <summary>
    /// Names the method that reads text from the clipboard or the primary selection.
    /// </summary>
    public const string ClipboardRead = "clipboard/read";
    /// <summary>
    /// Names the method that puts text on the clipboard or the primary selection.
    /// </summary>
    public const string ClipboardWrite = "clipboard/write";

    /// <summary>
    /// Names the method that answers a pending approval request. Only the first subscriber to approval requests can answer.
    /// </summary>
    public const string ApprovalAnswer = "approval/answer";

    /// <summary>
    /// Names the method that lists the input devices.
    /// </summary>
    public const string DevicesList = "devices/list";
    /// <summary>
    /// Names the method that answers each setting that one input device supports.
    /// </summary>
    public const string DevicesGet = "devices/get";
    /// <summary>
    /// Names the method that writes input settings to the runtime layer of a device or of every device of a type.
    /// </summary>
    public const string DevicesSet = "devices/set";
    /// <summary>
    /// Names the method that removes runtime values from an input device.
    /// </summary>
    public const string DevicesReset = "devices/reset";

    /// <summary>
    /// Names the method that answers the scene graph as a flat list of nodes.
    /// </summary>
    public const string DiagTree = "diag/tree";
    /// <summary>
    /// Names the method that answers the state of each window frame and the sink count of the frame clock.
    /// </summary>
    public const string DiagFrames = "diag/frames";
    /// <summary>
    /// Names the method that subscribes the caller to scene damage events, with or without stack traces.
    /// </summary>
    public const string DiagDamageTrace = "diag/damage-trace";
    /// <summary>
    /// Names the method that subscribes the caller to frame draw events.
    /// </summary>
    public const string DiagFrameTrace = "diag/frame-trace";
    /// <summary>
    /// Names the method that records a region of the next presented frames into one PNG strip with statistics for each frame.
    /// </summary>
    public const string DiagRecord = "diag/record";
    /// <summary>
    /// Names the method that records a target as it moves into one PNG strip with statistics for each frame.
    /// </summary>
    public const string DiagRecordWindow = "diag/record-window";
    /// <summary>
    /// Names the method that answers the parts of the frame of a window as boxes.
    /// </summary>
    public const string DiagParts = "diag/parts";
    /// <summary>
    /// Names the method that answers the box of a named element of the compositor's own UI.
    /// </summary>
    public const string DiagElement = "diag/element";
    /// <summary>
    /// Names the method that clicks the center of a frame part through synthetic input.
    /// </summary>
    public const string DiagClick = "diag/click";
    /// <summary>
    /// Names the method that presses a frame part, moves the pointer one step for each repaint, and releases it.
    /// </summary>
    public const string DiagDrag = "diag/drag";
    /// <summary>
    /// Names the method that allows or stops overlay planes and direct scanout.
    /// </summary>
    public const string DiagPlanes = "diag/planes";
    /// <summary>
    /// Names the method that subscribes the caller to seat input events, with or without motion.
    /// </summary>
    public const string DiagInputTrace = "diag/input-trace";
    /// <summary>
    /// Names the method that answers, for each output, the nodes on planes and the composited candidates with their reasons.
    /// </summary>
    public const string DiagPlanesState = "diag/planes-state";
    /// <summary>
    /// Names the method that subscribes the caller to changes of the plane decisions of an output.
    /// </summary>
    public const string DiagPlaneTrace = "diag/plane-trace";
    /// <summary>
    /// Names the method that subscribes the caller to the surface commits of a target.
    /// </summary>
    public const string DiagCommitTrace = "diag/commit-trace";
    /// <summary>
    /// Names the method that compares a target as shown now with a reference PNG.
    /// </summary>
    public const string DiagCompare = "diag/compare";
    /// <summary>
    /// Names the method that lists every target that a diagnostics selector can reach.
    /// </summary>
    public const string DiagTargets = "diag/targets";
    /// <summary>
    /// Names the method that returns the counters of the explicit sync protocol.
    /// </summary>
    public const string DiagSyncobj = "diag/syncobj";

    /// <summary>
    /// Gets the method namespaces that the library owns. A consumer registration in one of them throws.
    /// </summary>
    public static IReadOnlyList<string> ReservedNamespaces { get; } =
    [
        "ipc", "session", "outputs", "windows", "workspaces", "idle", "lock", "keyboard", "input", "seat",
        "capture", "process", "clipboard", "approval", "devices", "diag",
    ];
}
