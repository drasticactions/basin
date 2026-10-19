namespace Basin.Ipc;

public static class IpcMethodNames
{
    public const string Version = "ipc/version";
    public const string Methods = "ipc/methods";
    public const string Events = "ipc/events";
    public const string Subscribe = "ipc/subscribe";
    public const string Unsubscribe = "ipc/unsubscribe";

    public const string SessionDescribe = "session/describe";
    public const string SessionQuit = "session/quit";
    public const string SessionSettle = "session/settle";

    public const string OutputsList = "outputs/list";
    public const string OutputsTest = "outputs/test";
    public const string OutputsApply = "outputs/apply";
    public const string OutputsPower = "outputs/power";
    public const string OutputsLook = "outputs/look";

    public const string WindowsList = "windows/list";
    public const string WindowsGet = "windows/get";

    public const string WindowsFrameLimit = "windows/frame-limit";

    public const string WindowsStats = "windows/stats";
    public const string WindowsStack = "windows/stack";
    public const string WindowsActivate = "windows/activate";
    public const string WindowsClose = "windows/close";
    public const string WindowsSetState = "windows/set-state";
    public const string WindowsMove = "windows/move";
    public const string WindowsResize = "windows/resize";
    public const string WindowsSendToOutput = "windows/send-to-output";
    public const string WindowsSendToWorkspace = "windows/send-to-workspace";
    public const string WindowsWait = "windows/wait";
    public const string WindowsWaitIdle = "windows/wait-idle";
    public const string WindowsSetX11Density = "windows/set-x11-density";

    public const string WorkspacesList = "workspaces/list";
    public const string WorkspacesActivate = "workspaces/activate";
    public const string WorkspacesDeactivate = "workspaces/deactivate";
    public const string WorkspacesCreate = "workspaces/create";
    public const string WorkspacesRemove = "workspaces/remove";
    public const string WorkspacesMove = "workspaces/move";

    public const string IdleStatus = "idle/status";
    public const string IdleActivity = "idle/activity";
    public const string IdleInhibit = "idle/inhibit";
    public const string IdleUninhibit = "idle/uninhibit";

    public const string LockStatus = "lock/status";

    public const string KeyboardKeymap = "keyboard/keymap";

    public const string InputPointerMove = "input/pointer-move";
    public const string InputPointerButton = "input/pointer-button";
    public const string InputAxis = "input/axis";
    public const string InputKey = "input/key";
    public const string InputChord = "input/chord";
    public const string InputText = "input/text";
    public const string InputTouch = "input/touch";

    public const string SeatPointerMove = "seat/pointer-move";
    public const string SeatPointerButton = "seat/pointer-button";
    public const string SeatAxis = "seat/axis";
    public const string SeatKey = "seat/key";
    public const string SeatChord = "seat/chord";
    public const string SeatText = "seat/text";
    public const string SeatTouch = "seat/touch";

    public const string CaptureOutput = "capture/output";
    public const string CaptureWindow = "capture/window";
    public const string CaptureRegion = "capture/region";

    public const string ProcessSpawn = "process/spawn";
    public const string ProcessList = "process/list";
    public const string ProcessKill = "process/kill";
    public const string ProcessLog = "process/log";

    public const string ClipboardRead = "clipboard/read";
    public const string ClipboardWrite = "clipboard/write";

    public const string ApprovalAnswer = "approval/answer";

    public const string DevicesList = "devices/list";
    public const string DevicesGet = "devices/get";
    public const string DevicesSet = "devices/set";
    public const string DevicesReset = "devices/reset";

    public const string DiagTree = "diag/tree";
    public const string DiagFrames = "diag/frames";
    public const string DiagDamageTrace = "diag/damage-trace";
    public const string DiagFrameTrace = "diag/frame-trace";
    public const string DiagRecord = "diag/record";
    public const string DiagRecordWindow = "diag/record-window";
    public const string DiagParts = "diag/parts";
    public const string DiagClick = "diag/click";
    public const string DiagDrag = "diag/drag";
    public const string DiagPlanes = "diag/planes";
    public const string DiagInputTrace = "diag/input-trace";
    public const string DiagPlanesState = "diag/planes-state";
    public const string DiagPlaneTrace = "diag/plane-trace";
    public const string DiagCommitTrace = "diag/commit-trace";
    public const string DiagCompare = "diag/compare";
    public const string DiagTargets = "diag/targets";

    public static IReadOnlyList<string> ReservedNamespaces { get; } =
    [
        "ipc", "session", "outputs", "windows", "workspaces", "idle", "lock", "keyboard", "input", "seat",
        "capture", "process", "clipboard", "approval", "devices", "diag",
    ];
}
