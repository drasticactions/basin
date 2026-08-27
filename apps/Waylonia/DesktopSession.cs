namespace Waylonia;

internal static class DesktopSession
{
    private const string Exported =
        "WAYLAND_DISPLAY XDG_RUNTIME_DIR XDG_SESSION_TYPE XDG_SESSION_CLASS " +
        "XDG_CURRENT_DESKTOP XDG_SESSION_DESKTOP DESKTOP_SESSION";

    public static IReadOnlyList<string> Environment(
        DesktopRecipe recipe,
        IReadOnlyList<string> extra,
        bool gpu)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        var assignments = new List<string>(recipe.Env);
        if (recipe.SoftwareFallback && !gpu)
        {
            assignments.Add("WLR_RENDERER=pixman");
            assignments.Add("WLR_NO_HARDWARE_CURSORS=1");
        }

        if (extra is not null)
        {
            assignments.AddRange(extra);
        }

        return assignments;
    }

    public static string Wrapper(
        DesktopRecipe recipe,
        string display,
        string command,
        IReadOnlyList<string> environment)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(environment);
        var assignments = environment.Count > 0 ? string.Join(' ', environment) + " " : string.Empty;
        var inner =
            $"dbus-update-activation-environment --systemd {Exported} >/dev/null 2>&1 || true; " +
            $"exec env {assignments}{command}";
        var run = recipe.Bus
            ? $"exec dbus-run-session -- sh -c '{Quote(inner)}'"
            : $"exec env {assignments}{command}";
        return
            $"d=\"$XDG_RUNTIME_DIR/{display}\"; i=0; " +
            "while [ ! -S \"$d\" ] && [ $i -lt 50 ]; do sleep 0.2; i=$((i+1)); done; " +
            "unset DISPLAY; " +
            $"WAYLAND_DISPLAY={display}; XDG_SESSION_TYPE=wayland; XDG_SESSION_CLASS=user; " +
            $"XDG_CURRENT_DESKTOP={recipe.CurrentDesktop}; XDG_SESSION_DESKTOP={recipe.Name}; " +
            $"DESKTOP_SESSION={recipe.Name}; export {Exported}; " +
            run;
    }

    private static string Quote(string text) => text.Replace("'", "'\\''", StringComparison.Ordinal);
}
