namespace Basin.Ipc;

/// <summary>
/// The names of the events that the library declares. A consumer cannot declare an event in one of the <see cref="ReservedNamespaces"/>.
/// </summary>
public static class IpcEventNames
{
    /// <summary>
    /// A window was mapped. The data is the window descriptor.
    /// </summary>
    public const string WindowAdded = "window/added";
    /// <summary>
    /// A window changed. The data is the window descriptor. The event coalesces for each window in one loop iteration.
    /// </summary>
    public const string WindowChanged = "window/changed";
    /// <summary>
    /// A window went away. The data is its <c>id</c>.
    /// </summary>
    public const string WindowRemoved = "window/removed";
    /// <summary>
    /// A window became activated. The data is its <c>id</c>. A source that does not report activation never raises it.
    /// </summary>
    public const string WindowFocused = "window/focused";
    /// <summary>
    /// The stacking order changed. The data is <c>ids</c>, bottom to top.
    /// </summary>
    public const string StackChanged = "stack/changed";
    /// <summary>
    /// The output set, the layout or the output configuration changed. The data is the output list.
    /// </summary>
    public const string OutputChanged = "output/changed";
    /// <summary>
    /// The power of an output changed. The data is the output <c>name</c> and <c>on</c>.
    /// </summary>
    public const string OutputPower = "output/power";
    /// <summary>
    /// A workspace changed. The data is the workspace list.
    /// </summary>
    public const string WorkspaceChanged = "workspace/changed";
    /// <summary>
    /// The session lock changed. The data is <c>locked</c>.
    /// </summary>
    public const string LockChanged = "lock/changed";
    /// <summary>
    /// Idle inhibition started or stopped. The data is <c>inhibited</c>.
    /// </summary>
    public const string IdleInhibitChanged = "idle/inhibit-changed";
    /// <summary>
    /// The active keymap changed. The data is the keymap <c>names</c> and <c>size</c>.
    /// </summary>
    public const string KeyboardKeymapChanged = "keyboard/keymap-changed";
    /// <summary>
    /// A global shortcut was pressed or released. The data names the app, the shortcut and its flags.
    /// </summary>
    public const string ShortcutActivated = "shortcut/activated";
    /// <summary>
    /// The clipboard or the primary selection changed. The data is <c>kind</c>.
    /// </summary>
    public const string ClipboardChanged = "clipboard/changed";
    /// <summary>
    /// A tracked launch exited. The data is the launch id, the pid, and the exit code or the signal.
    /// </summary>
    public const string ProcessExited = "process/exited";
    /// <summary>
    /// An interceptor held a call for approval. The data names the request id, the method, its arguments and a reason.
    /// </summary>
    public const string ApprovalRequested = "approval/requested";
    /// <summary>
    /// An input device was added. The data is the device descriptor.
    /// </summary>
    public const string DeviceAdded = "devices/added";
    /// <summary>
    /// An input device was removed. The data is its <c>id</c>.
    /// </summary>
    public const string DeviceRemoved = "devices/removed";
    /// <summary>
    /// An input setting changed. The data is the device, the key and the value word at the flush.
    /// </summary>
    public const string DeviceChanged = "devices/changed";
    /// <summary>
    /// Raised once a second for each window that presented in the last second. The data is the window statistics reply.
    /// </summary>
    public const string FrameStats = "frame/stats";
    /// <summary>
    /// The scene took damage. The data is the path of the node and the damage box.
    /// </summary>
    public const string DiagDamage = "diag/damage";
    /// <summary>
    /// A window frame was drawn. The data is the frame entry of that frame.
    /// </summary>
    public const string DiagFrame = "diag/frame";
    /// <summary>
    /// The seat delivered an input event. Motion comes only with the <c>motion</c> option.
    /// </summary>
    public const string DiagInput = "diag/input";
    /// <summary>
    /// The plane decisions of an output changed. The data is the plane report of that output.
    /// </summary>
    public const string DiagPlane = "diag/plane";
    /// <summary>
    /// A surface of a traced target committed, or its frame callbacks fired.
    /// </summary>
    public const string DiagCommit = "diag/commit";

    /// <summary>
    /// Gets the event namespaces that the library owns. A consumer declaration in one of them throws.
    /// </summary>
    public static IReadOnlyList<string> ReservedNamespaces { get; } =
    [
        "window", "stack", "output", "workspace", "lock", "idle", "keyboard", "shortcut", "clipboard", "process", "approval", "devices", "frame",
        "diag",
    ];
}
