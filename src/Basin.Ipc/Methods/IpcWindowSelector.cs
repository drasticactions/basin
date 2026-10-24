using System.Buffers;
using System.Globalization;
using System.Text.Json;
using Basin.Capabilities;

namespace Basin.Ipc;

internal static class IpcWindowSelector
{
    private static readonly (string Method, string Field)[] Named =
    [
        (IpcMethodNames.WindowsGet, "id"),
        (IpcMethodNames.WindowsStats, "id"),
        (IpcMethodNames.WindowsFrameLimit, "id"),
        (IpcMethodNames.WindowsActivate, "id"),
        (IpcMethodNames.WindowsClose, "id"),
        (IpcMethodNames.WindowsSetState, "id"),
        (IpcMethodNames.WindowsMove, "id"),
        (IpcMethodNames.WindowsResize, "id"),
        (IpcMethodNames.WindowsSendToOutput, "id"),
        (IpcMethodNames.WindowsSendToWorkspace, "id"),
        (IpcMethodNames.WindowsWaitIdle, "id"),
        (IpcMethodNames.WindowsSetX11Density, "id"),
        (IpcMethodNames.CaptureWindow, "id"),
        (IpcMethodNames.InputPointerMove, "window"),
        (IpcMethodNames.InputPointerButton, "window"),
        (IpcMethodNames.InputAxis, "window"),
        (IpcMethodNames.SeatPointerMove, "window"),
        (IpcMethodNames.SeatPointerButton, "window"),
        (IpcMethodNames.SeatAxis, "window"),
    ];

    public static void WrapNamed(IpcMethodRegistry methods, IpcDescribe describe)
    {
        foreach (var (method, field) in Named)
        {
            methods.Wrap(method, handler => (ref IpcParams parameters, IpcReply reply) =>
            {
                if (!parameters.TryGetRaw(field, out var raw) || !IsName(raw))
                {
                    handler(ref parameters, reply);
                    return;
                }

                if (!TryResolve(ref parameters, describe, reply, out var info, field))
                {
                    return;
                }

                var rewritten = new IpcParams(Rewrite(parameters.Raw, field, info.Id), reply);
                handler(ref rewritten, reply);
            });
        }
    }

    public static bool TryResolve(ref IpcParams parameters, IpcDescribe describe, IpcReply reply, out ToplevelInfo info, string field = "window")
    {
        info = default;
        if (describe.Toplevels is not { } model)
        {
            reply.Error(IpcErrorCodes.Unavailable, "This compositor has no window model.");
            return false;
        }

        if (!parameters.TryGetRaw(field, out var raw))
        {
            reply.Error(IpcErrorCodes.InvalidParams, $"'{field}' is required. Give a window id, an app_id or part of a title.");
            return false;
        }

        string? name = null;
        if (raw.Length > 0 && raw[0] == (byte)'"')
        {
            if (!parameters.TryGetString(field, out var text))
            {
                return false;
            }

            if (text.Length == 0)
            {
                reply.Error(IpcErrorCodes.InvalidParams, $"'{field}' is empty. Give a window id, an app_id or part of a title.");
                return false;
            }

            if (text.AsSpan().IndexOfAnyExceptInRange('0', '9') >= 0
                || !ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            {
                name = text;
            }
        }

        if (name is null)
        {
            var id = parameters.GetUlong(field);
            if (parameters.Failed)
            {
                return false;
            }

            if (model.TryGet(id, out info))
            {
                return true;
            }

            reply.Error(IpcErrorCodes.NotFound, $"No window has the id {id}. Call windows/list for the windows.");
            return false;
        }

        return TryFind(model, name, reply, out info);
    }

    public static ToplevelInfo[] All(IToplevelModel model)
    {
        var all = new ToplevelInfo[64];
        var count = model.Enumerate(all);
        while (count < 0)
        {
            all = new ToplevelInfo[all.Length * 2];
            count = model.Enumerate(all);
        }

        return all[..count];
    }

    public static bool TryFind(IToplevelModel model, string name, IpcReply reply, out ToplevelInfo info, Func<ToplevelInfo, bool>? only = null)
    {
        info = default;
        if (name.Length > 0 && name.AsSpan().IndexOfAnyExceptInRange('0', '9') < 0
            && ulong.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
        {
            if (model.TryGet(id, out info) && (only is null || only(info)))
            {
                return true;
            }

            reply.Error(IpcErrorCodes.NotFound, $"No window has the id {id}. Call windows/list for the windows.");
            return false;
        }

        var all = All(model);
        foreach (var exact in new[] { true, false })
        {
            var matches = new List<ToplevelInfo>();
            foreach (var candidate in all)
            {
                if ((only is null || only(candidate))
                    && (exact ? candidate.AppId == name : candidate.Title.Contains(name, StringComparison.Ordinal)))
                {
                    matches.Add(candidate);
                }
            }

            if (matches.Count == 1)
            {
                info = matches[0];
                return true;
            }

            if (matches.Count > 1)
            {
                var active = matches.FindAll(static m => (m.State & ToplevelState.Activated) != 0);
                if (active.Count == 1)
                {
                    info = active[0];
                    return true;
                }

                reply.Error(IpcErrorCodes.InvalidParams, $"'{name}' matches {matches.Count} windows. Give one of their ids: " + string.Join(
                    ", ", matches.Select(static m => string.Create(CultureInfo.InvariantCulture, $"{m.Id} (app_id={m.AppId}, title={m.Title})"))) + ".");
                return false;
            }
        }

        reply.Error(IpcErrorCodes.NotFound, $"No window has the app_id '{name}' or a title that contains it.");
        return false;
    }

    private static bool IsName(ReadOnlySpan<byte> raw)
    {
        if (raw.Length < 2 || raw[0] != (byte)'"')
        {
            return false;
        }

        var text = raw[1..^1];
        return text.Length > 0 && text.IndexOfAnyExceptInRange((byte)'0', (byte)'9') >= 0;
    }

    private static byte[] Rewrite(ReadOnlySpan<byte> json, string field, ulong id)
    {
        using var document = JsonDocument.Parse(json.ToArray());
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.NameEquals(field))
                {
                    writer.WriteNumber(field, id);
                }
                else
                {
                    property.WriteTo(writer);
                }
            }

            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    public static bool Matches(string selector, in ToplevelInfo info)
    {
        if (selector == "*")
        {
            return true;
        }

        if (selector.Length > 0 && selector.AsSpan().IndexOfAnyExceptInRange('0', '9') < 0
            && ulong.TryParse(selector, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
        {
            return info.Id == id;
        }

        return info.AppId == selector || info.Title.Contains(selector, StringComparison.Ordinal);
    }

    public static bool TryPart(string text, out FramePart part) =>
        Enum.TryParse(text.Replace("-", string.Empty, StringComparison.Ordinal), ignoreCase: true, out part)
        && part != FramePart.None && Enum.IsDefined(part);

    public static string Name(FramePart part) => part switch
    {
        FramePart.TopLeft => "top-left",
        FramePart.TopRight => "top-right",
        FramePart.BottomRight => "bottom-right",
        FramePart.BottomLeft => "bottom-left",
        _ => part.ToString().ToLowerInvariant(),
    };
}
