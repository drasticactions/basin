using Basin.Capabilities;

namespace Basin.Ipc;

internal static class IpcClipboardMethods
{
    private const int DefaultTimeoutMs = 2000;
    private const int MaxTimeoutMs = 120_000;
    private const long DefaultMaxBytes = 1024 * 1024;
    private const long MaxBytesCap = 16 * 1024 * 1024;

    private static readonly string[] TextOrder = ["text/plain;charset=utf-8", "UTF8_STRING", "text/plain", "STRING", "TEXT"];

    public static void Register(IpcServer server, IpcDescribe describe)
    {
        if (describe.Find<ISelectionStore>() is not { } store)
        {
            return;
        }

        RegisterWrite(server, store);
        server.Methods.RegisterLibrary(IpcMethodNames.ClipboardRead, (ref IpcParams parameters, IpcReply reply) =>
        {
            if (parameters.Read(IpcJsonContext.Default.IpcClipboardParams) is not { } request)
            {
                return;
            }

            var kindName = request.Kind ?? "clipboard";
            var mime = request.Mime;
            var timeout = request.TimeoutMs ?? DefaultTimeoutMs;
            var maxBytes = request.MaxBytes ?? DefaultMaxBytes;

            SelectionKind? kind = kindName switch { "clipboard" => SelectionKind.Clipboard, "primary" => SelectionKind.Primary, _ => null };
            if (kind is null)
            {
                reply.Error(IpcErrorCodes.InvalidParams, "'kind' is clipboard or primary");
                return;
            }

            if (timeout <= 0 || timeout > MaxTimeoutMs)
            {
                reply.Error(IpcErrorCodes.InvalidParams, $"'timeout_ms' is between 1 and {MaxTimeoutMs}");
                return;
            }

            if (maxBytes <= 0 || maxBytes > MaxBytesCap)
            {
                reply.Error(IpcErrorCodes.InvalidParams, $"'max_bytes' is between 1 and {MaxBytesCap}");
                return;
            }

            if (mime is not null && !IsText(mime))
            {
                reply.Error(IpcErrorCodes.InvalidParams, $"'{mime}' is not a text type; only text is read");
                return;
            }

            var types = Offer(store, kind.Value);
            if (types.Length == 0)
            {
                IpcClipboardRead.WriteResult(reply, types, null, null);
                return;
            }

            var chosen = mime ?? Pick(types);
            if (chosen is null)
            {
                IpcClipboardRead.WriteResult(reply, types, null, null);
                return;
            }

            if (Array.IndexOf(types, chosen) < 0)
            {
                reply.Error(IpcErrorCodes.NotFound, $"the selection does not offer '{chosen}'");
                return;
            }

            if (!IpcNative.TryPipe(out var readFd, out var writeFd))
            {
                reply.Error(IpcErrorCodes.Failed, $"pipe2 failed (errno {UnixSocket.LastError})");
                return;
            }

            if (!store.Receive(kind.Value, chosen, new ClientFd(writeFd, null)))
            {
                _ = UnixSocket.Close(readFd);
                reply.Error(IpcErrorCodes.Failed, "the selection went away before it could be read");
                return;
            }

            new IpcClipboardRead(server, reply.Defer(), readFd, types, chosen, maxBytes).Start((int)timeout);
        });
    }

    private static void RegisterWrite(IpcServer server, ISelectionStore store)
    {
        server.Methods.RegisterLibrary(IpcMethodNames.ClipboardWrite, (ref IpcParams parameters, IpcReply reply) =>
        {
            if (parameters.Read(IpcJsonContext.Default.IpcClipboardWriteParams) is not { } request)
            {
                return;
            }

            SelectionKind? kind = (request.Kind ?? "clipboard") switch
            {
                "clipboard" => SelectionKind.Clipboard,
                "primary" => SelectionKind.Primary,
                _ => null,
            };
            if (kind is null)
            {
                reply.Error(IpcErrorCodes.InvalidParams, "'kind' is clipboard or primary");
                return;
            }

            if (request.Text.Length > MaxBytesCap)
            {
                reply.Error(IpcErrorCodes.InvalidParams, $"'text' is at most {MaxBytesCap} characters");
                return;
            }

            IpcWindowMethods.Done(reply, store.SetSelection(kind.Value, IpcClipboardSource.Create(server, request.Text), SelectionSerial.Unchecked));
        });
    }

    internal static bool IsText(string mime) =>
        mime.StartsWith("text/", StringComparison.Ordinal) || mime is "UTF8_STRING" or "STRING" or "TEXT";

    private static string? Pick(string[] types)
    {
        foreach (var preferred in TextOrder)
        {
            if (Array.IndexOf(types, preferred) >= 0)
            {
                return preferred;
            }
        }

        return null;
    }

    private static string[] Offer(ISelectionStore store, SelectionKind kind)
    {
        for (var size = 32; ; size *= 2)
        {
            var buffer = new string[size];
            var count = store.GetOffer(kind, buffer);
            if (count >= 0 && count < size)
            {
                return buffer[..count];
            }

            if (size >= 4096)
            {
                return count > 0 ? buffer[..count] : [];
            }
        }
    }
}
