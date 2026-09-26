using System.Globalization;
using Basin.Capabilities;
using Microsoft.Win32.SafeHandles;

namespace Basin.Ipc;

internal static class IpcSessionStateMethods
{
    public static void Register(IpcServer server, IpcDescribe describe)
    {
        var methods = server.Methods;
        if (describe.Find<IIdleSource>() is { } idle)
        {
            var next = 0;
            methods.RegisterLibrary(IpcMethodNames.IdleStatus, (ref IpcParams _, IpcReply reply) =>
                reply.Write(new IpcIdleStatus(idle.IdleMillis, idle.IsInhibited), IpcJsonContext.Default.IpcIdleStatus));

            methods.RegisterLibrary(IpcMethodNames.IdleActivity, (ref IpcParams _, IpcReply reply) =>
            {
                idle.NotifyActivity();
                IpcWrite.Empty(reply);
            });

            methods.RegisterLibrary(IpcMethodNames.IdleInhibit, (ref IpcParams _, IpcReply reply) =>
            {
                var token = "inhibit-" + (++next).ToString(CultureInfo.InvariantCulture);
                reply.State.Set(token, idle.Inhibit());
                reply.Write(new IpcIdleToken(token), IpcJsonContext.Default.IpcIdleToken);
            });

            methods.RegisterLibrary(IpcMethodNames.IdleUninhibit, (ref IpcParams parameters, IpcReply reply) =>
            {
                if (parameters.Read(IpcJsonContext.Default.IpcTokenParams) is not { } request)
                {
                    return;
                }

                var token = request.Token;
                if (!token.StartsWith("inhibit-", StringComparison.Ordinal) || !reply.State.Remove(token))
                {
                    reply.Error(IpcErrorCodes.NotFound, $"this connection holds no inhibitor '{token}'");
                    return;
                }

                IpcWrite.Empty(reply);
            });
        }

        if (describe.Find<ILockState>() is { } locked)
        {
            methods.RegisterLibrary(IpcMethodNames.LockStatus, (ref IpcParams _, IpcReply reply) =>
                reply.Write(new IpcLockStatus(locked.IsLocked), IpcJsonContext.Default.IpcLockStatus));
        }

        if (describe.Find<IActiveKeymap>() is { } keymap)
        {
            methods.RegisterLibrary(IpcMethodNames.KeyboardKeymap, (ref IpcParams parameters, IpcReply reply) =>
            {
                if (parameters.Read(IpcJsonContext.Default.IpcKeymapParams) is not { } request)
                {
                    return;
                }

                var buffer = keymap.KeymapBuffer;
                string? text = null;
                if (buffer is { } present && request.Text == true)
                {
                    var bytes = new byte[present.Size];
                    using var handle = new SafeFileHandle(present.Fd, ownsHandle: false);
                    var read = RandomAccess.Read(handle, bytes, 0);
                    var length = Array.IndexOf(bytes, (byte)0, 0, read);
                    text = System.Text.Encoding.UTF8.GetString(bytes, 0, length < 0 ? read : length);
                }

                int? fd = null;
                if (buffer is { } shared && request.Fd == true)
                {
                    var copy = UnixSocket.Duplicate(shared.Fd);
                    if (copy < 0)
                    {
                        reply.Error(IpcErrorCodes.Failed, $"could not duplicate the keymap fd (errno {UnixSocket.LastError})");
                        return;
                    }

                    fd = reply.AttachFd(copy);
                }

                reply.Write(new IpcKeymapInfo(null, buffer?.Size ?? 0, text, fd), IpcJsonContext.Default.IpcKeymapInfo);
            });
        }
    }
}
