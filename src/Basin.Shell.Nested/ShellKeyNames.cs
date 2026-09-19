namespace Basin.Shell.Nested;

public static class ShellKeyNames
{
    private static readonly (string Name, string? Chord)[] Table =
    [
        ("switch-windows", "Alt+Tab"),
        ("switch-windows-backward", "Shift+Alt+Tab"),
        ("switch-windows-all", null),
        ("switch-windows-all-backward", null),
        ("switch-group", "Alt+grave"),
        ("switch-group-backward", null),
        ("cycle-windows", "Alt+Escape"),
        ("cycle-windows-backward", null),
        ("cycle-group", null),
        ("cycle-group-backward", null),
        ("switch-to-workspace-left", "Ctrl+Alt+Left"),
        ("switch-to-workspace-right", "Ctrl+Alt+Right"),
        ("switch-to-workspace-up", "Ctrl+Alt+Up"),
        ("switch-to-workspace-down", "Ctrl+Alt+Down"),
        ("switch-to-workspace-prev", null),
        ("switch-to-workspace-1", null),
        ("switch-to-workspace-2", null),
        ("switch-to-workspace-3", null),
        ("switch-to-workspace-4", null),
        ("switch-to-workspace-5", null),
        ("switch-to-workspace-6", null),
        ("switch-to-workspace-7", null),
        ("switch-to-workspace-8", null),
        ("switch-to-workspace-9", null),
        ("switch-to-workspace-10", null),
        ("switch-to-workspace-11", null),
        ("switch-to-workspace-12", null),
        ("move-to-workspace-left", "Ctrl+Shift+Alt+Left"),
        ("move-to-workspace-right", "Ctrl+Shift+Alt+Right"),
        ("move-to-workspace-up", "Ctrl+Shift+Alt+Up"),
        ("move-to-workspace-down", "Ctrl+Shift+Alt+Down"),
        ("move-to-workspace-1", null),
        ("move-to-workspace-2", null),
        ("move-to-workspace-3", null),
        ("move-to-workspace-4", null),
        ("move-to-workspace-5", null),
        ("move-to-workspace-6", null),
        ("move-to-workspace-7", null),
        ("move-to-workspace-8", null),
        ("move-to-workspace-9", null),
        ("move-to-workspace-10", null),
        ("move-to-workspace-11", null),
        ("move-to-workspace-12", null),
        ("show-desktop", "Ctrl+Alt+d"),
        ("panel-main-menu", "Alt+F1"),
        ("activate-window-menu", "Alt+space"),
        ("close", "Alt+F4"),
        ("minimize", "Alt+F9"),
        ("toggle-fullscreen", null),
        ("toggle-maximized", "Alt+F10"),
        ("toggle-above", null),
        ("maximize", null),
        ("unmaximize", "Alt+F5"),
        ("toggle-shaded", null),
        ("begin-move", "Alt+F7"),
        ("begin-resize", "Alt+F8"),
        ("toggle-on-all-workspaces", null),
        ("raise-or-lower", null),
        ("raise", null),
        ("lower", null),
        ("maximize-vertically", null),
        ("maximize-horizontally", null),
        ("move-to-corner-nw", null),
        ("move-to-corner-ne", null),
        ("move-to-corner-sw", null),
        ("move-to-corner-se", null),
        ("move-to-side-n", null),
        ("move-to-side-s", null),
        ("move-to-side-e", null),
        ("move-to-side-w", null),
        ("move-to-center", null),
        ("tile-to-side-e", null),
        ("tile-to-side-w", null),
        ("tile-to-corner-ne", null),
        ("tile-to-corner-nw", null),
        ("tile-to-corner-se", null),
        ("tile-to-corner-sw", null),
        ("toggle-host-fullscreen", "Ctrl+Alt+Return"),
    ];

    public static IReadOnlyList<string> All { get; } = Table.Select(static entry => entry.Name).ToArray();

    public static string Listed { get; } = string.Join(", ", All);

    public static bool IsKnown(string name) => Table.Any(entry => entry.Name == name);

    public static string? DefaultChord(string name)
    {
        foreach (var (candidate, chord) in Table)
        {
            if (candidate == name)
            {
                return chord;
            }
        }

        return null;
    }
}
