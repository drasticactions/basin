namespace Basin.Ipc;

public static class IpcEventNames
{
    public const string WindowAdded = "window/added";
    public const string WindowChanged = "window/changed";
    public const string WindowRemoved = "window/removed";
    public const string WindowFocused = "window/focused";
    public const string StackChanged = "stack/changed";
    public const string OutputChanged = "output/changed";
    public const string OutputPower = "output/power";
    public const string WorkspaceChanged = "workspace/changed";
    public const string LockChanged = "lock/changed";
    public const string IdleInhibitChanged = "idle/inhibit-changed";
    public const string KeyboardKeymapChanged = "keyboard/keymap-changed";
    public const string ShortcutActivated = "shortcut/activated";
    public const string ClipboardChanged = "clipboard/changed";
    public const string ProcessExited = "process/exited";
    public const string ApprovalRequested = "approval/requested";

    public static IReadOnlyList<string> ReservedNamespaces { get; } =
    [
        "window", "stack", "output", "workspace", "lock", "idle", "keyboard", "shortcut", "clipboard", "process", "approval",
    ];
}
