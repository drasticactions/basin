using Basin.Capabilities;

namespace Basin.Ipc;

internal sealed class IpcInputGuard(ICaptureExclusion? exclusion, Func<(double X, double Y)?>? pointer)
{
    public bool AllowsPoint(double x, double y, IpcReply reply)
    {
        if (exclusion is null || !exclusion.IsExcludedAt(x, y))
        {
            return true;
        }

        reply.Error(IpcErrorCodes.Refused, "the point is on a surface that is excluded from capture and from synthetic input");
        return false;
    }

    public bool AllowsPointer(bool move, double x, double y, IpcReply reply)
    {
        if (move)
        {
            return AllowsPoint(x, y, reply);
        }

        return exclusion is null || pointer?.Invoke() is not { } at || AllowsPoint(at.X, at.Y, reply);
    }

    public bool AllowsKeyboard(IpcReply reply)
    {
        if (exclusion is null || !exclusion.IsKeyboardFocusExcluded)
        {
            return true;
        }

        reply.Error(IpcErrorCodes.Refused, "keyboard focus is on a surface that is excluded from capture and from synthetic input");
        return false;
    }
}
