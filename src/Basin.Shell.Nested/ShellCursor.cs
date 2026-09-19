using Basin.Capabilities;

namespace Basin.Shell.Nested;

public readonly record struct ShellCursor(CursorShape? Shape, Surface? Surface, int HotspotX, int HotspotY, bool Hidden)
{
    public static readonly ShellCursor Default = default;

    public static readonly ShellCursor None = new(null, null, 0, 0, true);

    public static ShellCursor Of(CursorShape shape) => new(shape, null, 0, 0, false);

    public static ShellCursor Image(Surface surface, int hotspotX, int hotspotY) => new(null, surface, hotspotX, hotspotY, false);
}
