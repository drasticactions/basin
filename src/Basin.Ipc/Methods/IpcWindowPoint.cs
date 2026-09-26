using System.Globalization;
using Basin.Capabilities;

namespace Basin.Ipc;

internal sealed class IpcWindowPoint(IpcDescribe describe)
{
    private ulong[] _stack = new ulong[32];

    public bool TryResolve(ulong window, double x, double y, bool raise, IpcReply reply, out double layoutX, out double layoutY)
    {
        layoutX = 0;
        layoutY = 0;
        if (describe.Toplevels is not { } model)
        {
            reply.Error(IpcErrorCodes.InvalidParams, "this compositor has no window model, so 'window' cannot be used");
            return false;
        }

        if (!model.TryGet(window, out var info))
        {
            reply.Error(IpcErrorCodes.NotFound, $"no window {window}");
            return false;
        }

        if ((info.State & ToplevelState.Minimized) != 0)
        {
            reply.Error(IpcErrorCodes.Refused, $"window {window} is minimized");
            return false;
        }

        var client = info.ClientGeometry.IsEmpty ? info.Geometry : info.ClientGeometry;
        if (client.IsEmpty)
        {
            reply.Error(IpcErrorCodes.Refused, $"window {window} is not mapped");
            return false;
        }

        if (x < 0 || y < 0 || x >= client.Width || y >= client.Height)
        {
            reply.Error(IpcErrorCodes.InvalidParams, string.Create(
                CultureInfo.InvariantCulture,
                $"({x}, {y}) is outside the {client.Width}x{client.Height} client area of window {window}"));
            return false;
        }

        layoutX = client.X + x;
        layoutY = client.Y + y;
        if (!TryTop(model, layoutX, layoutY, out var top))
        {
            reply.Error(IpcErrorCodes.Refused, "this compositor cannot tell which window is on top, so 'window' cannot be used");
            return false;
        }

        if (top != window && raise && model.Request(window, new ToplevelRequest(ToplevelRequestKind.Activate)))
        {
            _ = TryTop(model, layoutX, layoutY, out top);
        }

        if (top == window)
        {
            return true;
        }

        reply.Error(IpcErrorCodes.Refused, top == 0
            ? string.Create(CultureInfo.InvariantCulture, $"window {window} is not at ({x}, {y})")
            : string.Create(CultureInfo.InvariantCulture, $"window {window} is covered at ({x}, {y}) by window {top}"));
        return false;
    }

    private bool TryTop(IToplevelModel model, double x, double y, out ulong top)
    {
        top = 0;
        if (describe.Stack is not { } stack)
        {
            return false;
        }

        if (stack.TryToplevelAt(x, y, out top))
        {
            return true;
        }

        var count = stack.Enumerate(_stack);
        while (count < 0)
        {
            _stack = new ulong[_stack.Length * 2];
            count = stack.Enumerate(_stack);
        }

        for (var i = count - 1; i >= 0; i--)
        {
            if (model.TryGet(_stack[i], out var info) && (info.State & ToplevelState.Minimized) == 0
                && x >= info.Geometry.X && y >= info.Geometry.Y && x < info.Geometry.Right && y < info.Geometry.Bottom)
            {
                top = _stack[i];
                return true;
            }
        }

        return true;
    }
}
