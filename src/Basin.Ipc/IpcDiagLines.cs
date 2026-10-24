using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Basin.Ipc;

internal static class IpcDiagLines
{
    public static void Add(IpcMethodRegistry methods)
    {
        Add(methods, IpcMethodNames.DiagTree, "scene-tree", Tree);
        Add(methods, IpcMethodNames.DiagFrames, "frames", Frames);
        Add(methods, IpcMethodNames.DiagDamageTrace, "damage-trace {mode:on|stack|off}", Plain);
        Add(methods, IpcMethodNames.DiagFrameTrace, "frame-trace {on:bool}", Plain);
        Add(methods, IpcMethodNames.DiagRecord, "record {x:int} {y:int} {width:int} {height:int} {frames:int} {path} [{output}]", Recording);
        Add(methods, IpcMethodNames.DiagRecordWindow, "record-window {window} {frames:int} {path} [{margin:int}]", Recording);
        Add(methods, IpcMethodNames.DiagParts, "parts {window}", Parts);
        Add(methods, IpcMethodNames.DiagElement, "element {name}", Element);
        Add(methods, IpcMethodNames.DiagTargets, "targets", Targets);
        Add(methods, IpcMethodNames.DiagClick, "click {window} {part} [{button}]", Click);
        Add(methods, IpcMethodNames.DiagDrag, "drag {window} {dx:number} {dy:number} [{steps:int}] [{part}]", Drag);
        Add(methods, IpcMethodNames.DiagPlanes, "planes {on:bool}", Plain);
        Add(methods, IpcMethodNames.DiagInputTrace, "input-trace {mode:on|motion|off}", Plain);
        Add(methods, IpcMethodNames.DiagPlanesState, "planes-state", PlanesState);
        Add(methods, IpcMethodNames.DiagPlaneTrace, "plane-trace {on:bool}", Plain);
        Add(methods, IpcMethodNames.DiagCompare, "compare {window} {reference} [{diff_path}]", Comparison);
        Add(methods, IpcMethodNames.DiagCommitTrace, "commit-trace {on:bool} {target}", Plain);
        Add(methods, IpcMethodNames.DiagSyncobj, "syncobj", Syncobj);
    }

    public static string? DamageEvent(ReadOnlySpan<byte> data)
    {
        var damage = JsonSerializer.Deserialize(data, IpcJsonContext.Default.IpcDiagDamage);
        var box = damage.Box;
        var line = string.Create(CultureInfo.InvariantCulture, $"DAMAGE {damage.Path} {box.X},{box.Y},{box.Width}x{box.Height}");
        return damage.Stack is { } stack ? $"{line}\nDAMAGE-STACK {damage.Path}\n{stack}" : line;
    }

    public static string? FrameEvent(ReadOnlySpan<byte> data)
    {
        var frame = JsonSerializer.Deserialize(data, IpcJsonContext.Default.IpcDiagFrame);
        var text = new StringBuilder();
        AppendFrame(text, "FRAMEDRAW", frame);
        return text.ToString(0, text.Length - 1);
    }

    public static string? InputEvent(ReadOnlySpan<byte> data)
    {
        var input = JsonSerializer.Deserialize(data, IpcJsonContext.Default.IpcDiagInput);
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture,
            $"INPUT-TRACE {input.Kind} window={input.Window} app_id={input.AppId ?? "-"} at={input.X:F1},{input.Y:F1} layout={input.LayoutX:F1},{input.LayoutY:F1}");
        if (input.Kind is "button" or "axis")
        {
            text.Append(CultureInfo.InvariantCulture, $" code={input.Code}");
        }

        if (input.Kind == "button")
        {
            text.Append(input.Pressed ? " pressed" : " released");
        }

        if (input.Kind == "axis")
        {
            text.Append(CultureInfo.InvariantCulture, $" value={input.Value:F2}");
        }

        if (input.Detail is { } detail)
        {
            text.Append(' ').Append(detail);
        }

        return text.ToString();
    }

    public static string? Syncobj(IpcReply reply) => Render(reply, IpcJsonContext.Default.IpcDiagSyncobj, static (c, text) =>
        text.Append(CultureInfo.InvariantCulture,
            $"SYNCOBJ managed={c.ManagedCommits} explicit={c.ExplicitCommits} parked={c.ParkedAcquires} unparked={c.UnparkedAcquires} late={c.LateAcquires} unexportable={c.UnexportableAcquires} deferred-releases={c.DeferredReleases} superseded-releases={c.SupersededReleases} discarded-releases={c.DiscardedReleases}\n"));

    public static string? PlanesState(IpcReply reply) => Render(reply, IpcJsonContext.Default.IpcDiagPlaneState, static (state, text) =>
    {
        foreach (var report in state.Outputs)
        {
            AppendPlanes(text, "PLANES", report);
        }
    });

    public static string? PlaneEvent(ReadOnlySpan<byte> data)
    {
        var text = new StringBuilder();
        AppendPlanes(text, "PLANES-CHANGED", JsonSerializer.Deserialize(data, IpcJsonContext.Default.IpcDiagPlaneReport));
        return text.ToString(0, text.Length - 1);
    }

    public static string? CommitEvent(ReadOnlySpan<byte> data)
    {
        var commit = JsonSerializer.Deserialize(data, IpcJsonContext.Default.IpcDiagCommit);
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"COMMIT-TRACE {commit.Kind} window={commit.Window} app_id={commit.AppId ?? "-"}");
        if (commit.Kind == "frame-done")
        {
            text.Append(CultureInfo.InvariantCulture, $" callbacks={commit.FrameCallbacks} at-ms={commit.TimestampMs}");
            return text.ToString();
        }

        text.Append(CultureInfo.InvariantCulture, $" size={commit.Width}x{commit.Height} format={commit.Format} callbacks={commit.FrameCallbacks}");
        if (commit.Feedback)
        {
            text.Append(" feedback");
        }

        text.Append(CultureInfo.InvariantCulture, $" damage={commit.Damage.Length}");
        foreach (var box in commit.Damage.Take(4))
        {
            text.Append(CultureInfo.InvariantCulture, $" {box.X},{box.Y},{box.Width}x{box.Height}");
        }

        return text.ToString();
    }

    public static string? Comparison(IpcReply reply) => Render(reply, IpcJsonContext.Default.IpcDiagComparison, static (compare, text) =>
    {
        var box = compare.ChangedBox;
        text.Append(CultureInfo.InvariantCulture,
            $"COMPARE {(compare.Passed ? "pass" : "fail")} window={compare.Window} size={compare.Width}x{compare.Height}"
            + $" reference={compare.ReferenceWidth}x{compare.ReferenceHeight} changed={compare.Changed} max-delta={compare.MaxDelta}"
            + $" box={box.X},{box.Y},{box.Width}x{box.Height}");
        if (compare.DiffPath is { } diff)
        {
            text.Append(" diff=").Append(diff);
        }

        text.Append(compare.TimedOut ? " timed-out\n" : "\n");
    });

    public static string? Targets(IpcReply reply) => Render(reply, IpcJsonContext.Default.IpcDiagTargets, static (targets, text) =>
    {
        foreach (var target in targets.Targets)
        {
            text.Append(CultureInfo.InvariantCulture, $"TARGET {target.Kind} {target.Selector} window={target.Window}");
            if (target.AppId is { } appId)
            {
                text.Append(" app_id=").Append(appId);
            }

            if (target.Output is { } output)
            {
                text.Append(" output=").Append(output);
            }

            if (target.Box is { } box)
            {
                text.Append(CultureInfo.InvariantCulture, $" box={box.X},{box.Y},{box.Width}x{box.Height}");
            }

            text.Append('\n');
        }
    });

    public static string? Plain(IpcReply reply) => reply.IsError ? $"ERR {reply.ErrorMessage}" : null;

    private static void Add(IpcMethodRegistry methods, string method, string pattern, IpcLineFormatter formatter)
    {
        if (methods.Contains(method) && !methods.HasLine(method))
        {
            methods.AddLine(method, pattern, formatter);
        }
    }

    public static string? Tree(IpcReply reply) => Render(reply, IpcJsonContext.Default.IpcDiagTree, static (tree, text) =>
    {
        foreach (var node in tree.Nodes)
        {
            text.Append("TREE ").Append(' ', node.Depth * 2).Append(node.Kind);
            if (node.Name is { } name)
            {
                text.Append(" \"").Append(name).Append('"');
            }

            text.Append(CultureInfo.InvariantCulture, $" at={node.X},{node.Y}");
            if (!node.Enabled)
            {
                text.Append(" disabled");
            }

            text.Append(node.Details).Append('\n');
        }
    });

    public static string? Frames(IpcReply reply) => Render(reply, IpcJsonContext.Default.IpcDiagFrames, static (frames, text) =>
    {
        foreach (var frame in frames.Frames)
        {
            AppendFrame(text, "FRAME", frame);
        }

        if (frames.ClockSinks >= 0)
        {
            text.Append(CultureInfo.InvariantCulture, $"CLOCK sinks={frames.ClockSinks}\n");
        }
    });

    public static string? Parts(IpcReply reply) => Render(reply, IpcJsonContext.Default.IpcDiagParts, static (parts, text) =>
    {
        text.Append(CultureInfo.InvariantCulture, $"WINDOW {parts.Window} app_id={parts.AppId} title={parts.Title}\n");
        foreach (var part in parts.Parts)
        {
            var box = part.Box;
            text.Append(CultureInfo.InvariantCulture, $"PART {part.Part} {box.X},{box.Y},{box.Width}x{box.Height}\n");
        }
    });

    public static string? Element(IpcReply reply) => Render(reply, IpcJsonContext.Default.IpcDiagElement, static (element, text) =>
    {
        var box = element.Box;
        text.Append(CultureInfo.InvariantCulture, $"ELEMENT {element.Name} {box.X},{box.Y},{box.Width}x{box.Height}\n");
    });

    public static string? Recording(IpcReply reply) => Render(reply, IpcJsonContext.Default.IpcDiagRecording, static (recording, text) =>
    {
        foreach (var frame in recording.PerFrame)
        {
            text.Append(CultureInfo.InvariantCulture,
                $"RECORD-FRAME {frame.Index} at={frame.At.X},{frame.At.Y} luma={frame.Luma:F1} peak={frame.Peak:F1} delta={frame.Delta} planes={frame.Planes.Length}");
            foreach (var plane in frame.Planes)
            {
                text.Append(CultureInfo.InvariantCulture, $" plane={plane.X},{plane.Y},{plane.Width}x{plane.Height}");
            }

            text.Append(frame.Unmapped ? " unmapped\n" : "\n");
        }

        text.Append(CultureInfo.InvariantCulture,
            $"RECORD {recording.Path} frames={recording.Frames} size={recording.Width}x{recording.Height}"
            + $" luma={recording.LumaLow:F1}..{recording.LumaHigh:F1} peak={recording.PeakLow:F1}..{recording.PeakHigh:F1}"
            + $" delta-max={recording.DeltaMax}{(recording.TimedOut ? " timed-out" : string.Empty)}\n");
    });

    public static string? Click(IpcReply reply) => Render(reply, IpcJsonContext.Default.IpcDiagClick, static (click, text) =>
        text.Append(CultureInfo.InvariantCulture, $"CLICK {click.Part} at={click.X:F0},{click.Y:F0}{(click.Settled ? string.Empty : " unsettled")}\n"));

    public static string? Drag(IpcReply reply) => Render(reply, IpcJsonContext.Default.IpcDiagDrag, static (drag, text) =>
        text.Append(CultureInfo.InvariantCulture,
            $"DRAG {drag.Part} from={drag.FromX:F0},{drag.FromY:F0} to={drag.ToX:F0},{drag.ToY:F0} steps={drag.Steps} frames={drag.Frames}"
            + $"{(drag.Settled ? string.Empty : " unsettled")}\n"));

    private static void AppendFrame(StringBuilder text, string prefix, in IpcDiagFrame frame)
    {
        var outer = frame.Outer;
        text.Append(CultureInfo.InvariantCulture,
            $"{prefix} {frame.Name} draws={frame.Draws} animating={frame.Animating} visible={frame.Visible}"
            + $" outer={outer.X},{outer.Y},{outer.Width}x{outer.Height} active={frame.Active} maximized={frame.Maximized}"
            + $" resizing={frame.Resizing} hot={frame.Hot} previous-hot={frame.PreviousHot} pressed={frame.Pressed}"
            + $" inside={frame.Inside} now-ms={frame.NowMs} hover-ms={frame.HoverMs} active-ms={frame.ActiveMs}\n");
    }

    private static void AppendPlanes(StringBuilder text, string prefix, in IpcDiagPlaneReport report)
    {
        text.Append(prefix).Append(' ').Append(report.Output);
        text.Append(CultureInfo.InvariantCulture, $" planes={report.Entries.Count(static e => e.OnPlane)}");
        if (report.Skipped is { } skipped)
        {
            text.Append(" skipped=").Append(skipped.Replace(' ', '-'));
        }

        text.Append('\n');
        foreach (var entry in report.Entries)
        {
            var box = entry.Box;
            text.Append(CultureInfo.InvariantCulture, $"  {(entry.OnPlane ? "PLANE" : "COMPOSITED")} {entry.Path} {box.X},{box.Y},{box.Width}x{box.Height}");
            if (entry.PlaneBox is { } plane)
            {
                text.Append(CultureInfo.InvariantCulture, $" plane={plane.X},{plane.Y},{plane.Width}x{plane.Height}");
            }

            if (entry.Reason is { } reason)
            {
                text.Append(' ').Append(reason);
            }

            text.Append('\n');
        }
    }

    private static string? Render<T>(IpcReply reply, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info, Action<T, StringBuilder> write)
    {
        ArgumentNullException.ThrowIfNull(reply);
        if (reply.IsError)
        {
            return $"ERR {reply.ErrorMessage}";
        }

        var value = JsonSerializer.Deserialize(reply.ResultJson, info);
        if (value is null)
        {
            return null;
        }

        var text = new StringBuilder();
        write(value, text);
        return text.Length == 0 ? null : text.ToString(0, text.Length - 1);
    }
}
