using Basin.Diagnostics;

namespace Basin.Ipc;

/// <summary>
/// Captures the report lines that a consumer handler prints, so that a socket call receives them as a <c>lines</c> result and the line front prints them.
/// </summary>
public sealed class IpcLineReport
{
    private List<string>? _lines;

    /// <summary>
    /// Gets a value indicating whether a wrapped handler runs now, so that <see cref="Line"/> captures.
    /// </summary>
    public bool IsCapturing => _lines is not null;

    /// <summary>
    /// Reports one line. Inside a wrapped handler it is captured, and outside one it goes straight to the report channel.
    /// </summary>
    /// <param name="text">The line.</param>
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

    /// <summary>
    /// Wraps a handler so that its lines become the reply's <c>lines</c> result. When the handler defers, fails or writes its own result, the lines go to the report channel instead.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <returns>The wrapped handler.</returns>
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

    /// <summary>
    /// Wraps an action that ignores the params, as <see cref="Wrap(IpcHandler)"/> does for a handler.
    /// </summary>
    /// <param name="action">The action.</param>
    /// <returns>The wrapped handler.</returns>
    public IpcHandler Wrap(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return Wrap((ref IpcParams _, IpcReply _) => action());
    }

    /// <summary>
    /// Registers a wrapped handler with a line form that prints the captured lines.
    /// </summary>
    /// <param name="methods">The method table.</param>
    /// <param name="name">The method name.</param>
    /// <param name="line">The line form pattern, or <see langword="null"/> for none.</param>
    /// <param name="handler">The handler.</param>
    public void Register(IpcMethodRegistry methods, string name, string? line, IpcHandler handler)
    {
        ArgumentNullException.ThrowIfNull(methods);
        methods.Register(name, Wrap(handler), line, Format);
    }

    /// <summary>
    /// Registers a wrapped handler with metadata and a line form that prints the captured lines.
    /// </summary>
    /// <param name="methods">The method table.</param>
    /// <param name="name">The method name.</param>
    /// <param name="line">The line form pattern, or <see langword="null"/> for none.</param>
    /// <param name="info">The method's metadata.</param>
    /// <param name="handler">The handler.</param>
    public void Register(IpcMethodRegistry methods, string name, string? line, IpcMethodInfo info, IpcHandler handler)
    {
        ArgumentNullException.ThrowIfNull(methods);
        methods.Register(name, Wrap(handler), line, Format, info);
    }

    /// <summary>
    /// Registers a wrapped action with metadata and a line form that prints the captured lines.
    /// </summary>
    /// <param name="methods">The method table.</param>
    /// <param name="name">The method name.</param>
    /// <param name="line">The line form pattern, or <see langword="null"/> for none.</param>
    /// <param name="info">The method's metadata.</param>
    /// <param name="action">The action, which ignores the params.</param>
    public void Register(IpcMethodRegistry methods, string name, string? line, IpcMethodInfo info, Action action)
    {
        ArgumentNullException.ThrowIfNull(methods);
        methods.Register(name, Wrap(action), line, Format, info);
    }

    /// <summary>
    /// Registers a wrapped action with a line form that prints the captured lines.
    /// </summary>
    /// <param name="methods">The method table.</param>
    /// <param name="name">The method name.</param>
    /// <param name="line">The line form pattern, or <see langword="null"/> for none.</param>
    /// <param name="action">The action, which ignores the params.</param>
    public void Register(IpcMethodRegistry methods, string name, string? line, Action action)
    {
        ArgumentNullException.ThrowIfNull(methods);
        methods.Register(name, Wrap(action), line, Format);
    }

    /// <summary>
    /// Adds a line form whose reply prints the <c>lines</c> result.
    /// </summary>
    /// <param name="methods">The method table.</param>
    /// <param name="name">The method name.</param>
    /// <param name="line">The line form pattern.</param>
    public void AddLine(IpcMethodRegistry methods, string name, string line)
    {
        ArgumentNullException.ThrowIfNull(methods);
        methods.AddLine(name, line, Format);
    }

    /// <summary>
    /// Adds the <c>move</c>, <c>button</c> and <c>key</c> line forms for the <c>input</c> group.
    /// </summary>
    /// <param name="methods">The method table.</param>
    public void AddInputLines(IpcMethodRegistry methods)
    {
        AddLine(methods, IpcMethodNames.InputPointerMove, "move {x:number} {y:number}");
        AddLine(methods, IpcMethodNames.InputPointerButton, "button {button:int} {pressed:bool}");
        AddLine(methods, IpcMethodNames.InputKey, "key {code:int} {pressed:bool}");
    }

    /// <summary>
    /// Adds the <c>frame-limit</c> and <c>stats</c> line forms for the <c>windows</c> group.
    /// </summary>
    /// <param name="methods">The method table.</param>
    public void AddWindowLines(IpcMethodRegistry methods)
    {
        AddLine(methods, IpcMethodNames.WindowsFrameLimit, "frame-limit {id} {fps:int}");
        AddLine(methods, IpcMethodNames.WindowsStats, "stats {id}");
    }

    /// <summary>
    /// Adds the line forms of the <c>diag</c> group.
    /// </summary>
    /// <param name="methods">The method table.</param>
    public void AddDiagnosticsLines(IpcMethodRegistry methods)
    {
        ArgumentNullException.ThrowIfNull(methods);
        IpcDiagLines.Add(methods);
    }

    /// <summary>
    /// Adds the <c>look</c> line form for <c>outputs/look</c>.
    /// </summary>
    /// <param name="methods">The method table.</param>
    public void AddOutputLines(IpcMethodRegistry methods) =>
        AddLine(methods, IpcMethodNames.OutputsLook, "look {output} [{path}]");

    /// <summary>
    /// Adds the <c>quit</c> and <c>settle</c> line forms for the <c>session</c> group.
    /// </summary>
    /// <param name="methods">The method table.</param>
    public void AddSessionLines(IpcMethodRegistry methods)
    {
        AddLine(methods, IpcMethodNames.SessionQuit, "quit");
        methods.AddLine(IpcMethodNames.SessionSettle, "settle", IpcLineFront.Describe);
    }

    /// <summary>
    /// Writes lines as the <c>lines</c> result of a pending reply and completes it.
    /// </summary>
    /// <param name="pending">The pending reply.</param>
    /// <param name="lines">The lines.</param>
    /// <returns>The value of <see cref="IpcPendingReply.Complete"/>.</returns>
    public static bool Complete(IpcPendingReply pending, params string[] lines)
    {
        ArgumentNullException.ThrowIfNull(pending);
        WriteLines(pending, lines);
        return pending.Complete();
    }

    /// <summary>
    /// Formats a reply for the line front: <c>ERR message</c> for an error, or the captured lines.
    /// </summary>
    /// <param name="reply">The finished reply.</param>
    /// <returns>The text, or <see langword="null"/> to print nothing when the reply has no lines.</returns>
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
