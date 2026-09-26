using Basin.Capabilities;

namespace Basin.Ipc;

internal static class IpcWrite
{
    public static IpcBox Box(in Box box) => new(box.X, box.Y, box.Width, box.Height);

    public static void Empty(IpcReply reply) => reply.Write(default, IpcJsonContext.Default.IpcEmpty);

    public static string Transform(OutputTransform transform) => transform switch
    {
        OutputTransform.Normal => "normal",
        OutputTransform.Rotate90 => "90",
        OutputTransform.Rotate180 => "180",
        OutputTransform.Rotate270 => "270",
        OutputTransform.Flipped => "flipped",
        OutputTransform.Flipped90 => "flipped-90",
        OutputTransform.Flipped180 => "flipped-180",
        OutputTransform.Flipped270 => "flipped-270",
        _ => "normal",
    };

    public static bool TryParseTransform(string text, out OutputTransform transform)
    {
        for (var value = OutputTransform.Normal; value <= OutputTransform.Flipped270; value++)
        {
            if (Transform(value) == text)
            {
                transform = value;
                return true;
            }
        }

        transform = OutputTransform.Normal;
        return false;
    }

    public static IpcWindowState States(ToplevelState state) => new(
        (state & ToplevelState.Activated) != 0,
        (state & ToplevelState.Maximized) != 0,
        (state & ToplevelState.Minimized) != 0,
        (state & ToplevelState.Fullscreen) != 0,
        (state & ToplevelState.NoBorder) != 0,
        (state & ToplevelState.SkipTaskbar) != 0);
}
