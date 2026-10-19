using Basin.Diagnostics;

namespace Basin.Ipc;

public sealed class IpcLineReport
{
    private List<string>? _lines;

    public bool IsCapturing => _lines is not null;

    public void Line(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (_lines is { } lines)
        {
            lines.Add(text);
        }
        else
        {
            BasinReport.Line(text);
        }
    }

    public IpcHandler Wrap(IpcHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return (ref IpcParams parameters, IpcReply reply) =>
        {
            var outer = _lines;
            var lines = new List<string>();
            _lines = lines;
            try
            {
                handler(ref parameters, reply);
            }
            finally
            {
                _lines = outer;
            }

            Settle(reply, lines);
        };
    }

    public IpcHandler Wrap(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return Wrap((ref IpcParams _, IpcReply _) => action());
    }

    public void Register(IpcMethodRegistry methods, string name, string? line, IpcHandler handler)
    {
        ArgumentNullException.ThrowIfNull(methods);
        methods.Register(name, Wrap(handler), line, Format);
    }

    public void Register(IpcMethodRegistry methods, string name, string? line, IpcMethodInfo info, IpcHandler handler)
    {
        ArgumentNullException.ThrowIfNull(methods);
        methods.Register(name, Wrap(handler), line, Format, info);
    }

    public void Register(IpcMethodRegistry methods, string name, string? line, IpcMethodInfo info, Action action)
    {
        ArgumentNullException.ThrowIfNull(methods);
        methods.Register(name, Wrap(action), line, Format, info);
    }

    public void Register(IpcMethodRegistry methods, string name, string? line, Action action)
    {
        ArgumentNullException.ThrowIfNull(methods);
        methods.Register(name, Wrap(action), line, Format);
    }

    public void AddLine(IpcMethodRegistry methods, string name, string line)
    {
        ArgumentNullException.ThrowIfNull(methods);
        methods.AddLine(name, line, Format);
    }

    public void AddInputLines(IpcMethodRegistry methods)
    {
        AddLine(methods, IpcMethodNames.InputPointerMove, "move {x:number} {y:number}");
        AddLine(methods, IpcMethodNames.InputPointerButton, "button {button:int} {pressed:bool}");
        AddLine(methods, IpcMethodNames.InputKey, "key {code:int} {pressed:bool}");
    }

    public void AddWindowLines(IpcMethodRegistry methods)
    {
        AddLine(methods, IpcMethodNames.WindowsFrameLimit, "frame-limit {id} {fps:int}");
        AddLine(methods, IpcMethodNames.WindowsStats, "stats {id}");
    }

    public void AddDiagnosticsLines(IpcMethodRegistry methods)
    {
        ArgumentNullException.ThrowIfNull(methods);
        IpcDiagLines.Add(methods);
    }

    public void AddOutputLines(IpcMethodRegistry methods) =>
        AddLine(methods, IpcMethodNames.OutputsLook, "look {output} [{path}]");

    public void AddSessionLines(IpcMethodRegistry methods)
    {
        AddLine(methods, IpcMethodNames.SessionQuit, "quit");
        methods.AddLine(IpcMethodNames.SessionSettle, "settle", IpcLineFront.Describe);
    }

    public static bool Complete(IpcPendingReply pending, params string[] lines)
    {
        ArgumentNullException.ThrowIfNull(pending);
        WriteLines(pending, lines);
        return pending.Complete();
    }

    public string? Format(IpcReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);
        if (reply.IsError)
        {
            return $"ERR {reply.ErrorMessage}";
        }

        var lines = IpcLineFront.Lines(reply);
        return lines.Count == 0 ? null : string.Join('\n', lines);
    }

    private static void Settle(IpcReply reply, List<string> lines)
    {
        if (lines.Count == 0)
        {
            return;
        }

        if (reply.IsDeferred || reply.IsError || reply.HasResult)
        {
            foreach (var line in lines)
            {
                BasinReport.Line(line);
            }

            return;
        }

        WriteLines(reply, lines);
    }

    private static void WriteLines(IpcReply reply, IReadOnlyList<string> lines) =>
        reply.Write(new IpcLineReply(lines.ToArray()), IpcJsonContext.Default.IpcLineReply);
}
