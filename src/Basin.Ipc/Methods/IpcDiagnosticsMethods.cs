using System.Globalization;
using Basin.Capabilities;

namespace Basin.Ipc;

internal static class IpcDiagnosticsMethods
{
    private const int DefaultMargin = 24;
    private const int DefaultSettleMs = 2000;

    public static void Register(IpcServer server, IpcDescribe describe)
    {
        if (describe.Find<IInputDiagnostics>() is not null)
        {
            server.Methods.RegisterLibrary(IpcMethodNames.DiagInputTrace, (ref IpcParams parameters, IpcReply reply) =>
            {
                var mode = parameters.GetString("mode");
                if (parameters.Failed)
                {
                    return;
                }

                if (mode is not ("on" or "motion" or "off"))
                {
                    reply.Error(IpcErrorCodes.InvalidParams, $"'mode' must be on, motion or off, not '{mode}'.");
                    return;
                }

                Trace(server, reply, IpcEventNames.DiagInput, mode != "off", mode == "motion" ? MotionOptions : null);
            });
        }

        if (describe.Find<ISurfaceDiagnostics>() is not null)
        {
            server.Methods.RegisterLibrary(IpcMethodNames.DiagCommitTrace, (ref IpcParams parameters, IpcReply reply) =>
            {
                var on = parameters.GetBool("on");
                var target = parameters.TryGetString("target", out var given) ? given : null;
                if (parameters.Failed)
                {
                    return;
                }

                if (on && string.IsNullOrEmpty(target))
                {
                    reply.Error(IpcErrorCodes.InvalidParams, "'target' is required. Give a window id, an app_id, part of a title, or \"*\" for every surface.");
                    return;
                }

                Trace(server, reply, IpcEventNames.DiagCommit, on, on ? TargetOptions(target!) : null);
            });
        }

        if (describe.Find<IExplicitSyncDiagnostics>() is { } explicitSync)
        {
            server.Methods.RegisterLibrary(IpcMethodNames.DiagSyncobj, (ref IpcParams _, IpcReply reply) =>
            {
                var c = explicitSync.Counters;
                reply.Write(
                    new IpcDiagSyncobj(
                        c.ManagedCommits, c.ExplicitCommits, c.ParkedAcquires, c.UnparkedAcquires,
                        c.LateAcquires, c.UnexportableAcquires, c.DeferredReleases, c.SupersededReleases, c.DiscardedReleases),
                    IpcJsonContext.Default.IpcDiagSyncobj);
            });
        }

        if (describe.Find<IUIElementLocator>() is { } locator)
        {
            server.Methods.RegisterLibrary(IpcMethodNames.DiagElement, (ref IpcParams parameters, IpcReply reply) =>
            {
                var name = parameters.GetString("name");
                if (parameters.Failed)
                {
                    return;
                }

                if (!locator.TryLocate(name, out var box))
                {
                    reply.Error(IpcErrorCodes.NotFound, $"No visible element of the compositor's UI has the name '{name}'.");
                    return;
                }

                reply.Write(new IpcDiagElement(name, Box(box)), IpcJsonContext.Default.IpcDiagElement);
            });
        }

        if (describe.Diagnostics is not { } diagnostics)
        {
            if (describe.Find<IWindowPartsSource>() is { } source)
            {
                RegisterPartsSource(server.Methods, source);
            }

            IpcDiagLines.Add(server.Methods);
            return;
        }

        var methods = server.Methods;
        methods.RegisterLibrary(IpcMethodNames.DiagTree, (ref IpcParams _, IpcReply reply) =>
        {
            var nodes = diagnostics.Tree();
            var wire = new IpcDiagNode[nodes.Count];
            for (var i = 0; i < wire.Length; i++)
            {
                var node = nodes[i];
                wire[i] = new IpcDiagNode(
                    node.Depth, node.Kind, node.Name, node.Path, node.X, node.Y, node.Width, node.Height, node.Enabled, node.Details);
            }

            reply.Write(new IpcDiagTree(wire), IpcJsonContext.Default.IpcDiagTree);
        });

        methods.RegisterLibrary(IpcMethodNames.DiagFrames, (ref IpcParams _, IpcReply reply) =>
        {
            var frames = diagnostics.Frames();
            var wire = new IpcDiagFrame[frames.Count];
            for (var i = 0; i < wire.Length; i++)
            {
                wire[i] = Wire(frames[i], describe);
            }

            reply.Write(new IpcDiagFrames(wire, diagnostics.ClockSinks), IpcJsonContext.Default.IpcDiagFrames);
        });

        methods.RegisterLibrary(IpcMethodNames.DiagDamageTrace, (ref IpcParams parameters, IpcReply reply) =>
        {
            var mode = parameters.GetString("mode");
            if (parameters.Failed)
            {
                return;
            }

            if (mode is not ("on" or "stack" or "off"))
            {
                reply.Error(IpcErrorCodes.InvalidParams, $"'mode' must be on, stack or off, not '{mode}'.");
                return;
            }

            Trace(server, reply, IpcEventNames.DiagDamage, mode != "off", mode == "stack" ? StackOptions : null);
        });

        methods.RegisterLibrary(IpcMethodNames.DiagFrameTrace, (ref IpcParams parameters, IpcReply reply) =>
        {
            var on = parameters.GetBool("on");
            if (parameters.Failed)
            {
                return;
            }

            Trace(server, reply, IpcEventNames.DiagFrame, on, null);
        });

        methods.RegisterLibrary(IpcMethodNames.DiagRecord, (ref IpcParams parameters, IpcReply reply) =>
        {
            var x = (int)parameters.GetInt("x");
            var y = (int)parameters.GetInt("y");
            var width = (int)parameters.GetInt("width");
            var height = (int)parameters.GetInt("height");
            var request = ReadRecording(ref parameters);
            var output = parameters.TryGetString("output", out var named) ? named : null;
            if (parameters.Failed || request is null)
            {
                return;
            }

            Record(diagnostics, request with { Region = new Box(x, y, width, height), Output = output }, reply);
        });

        methods.RegisterLibrary(IpcMethodNames.DiagRecordWindow, (ref IpcParams parameters, IpcReply reply) =>
        {
            var margin = parameters.TryGetInt("margin", out var given) ? (int)given : DefaultMargin;
            if (parameters.Failed || !IpcTargetSelector.TryResolve(ref parameters, describe, diagnostics, margin, reply, out var target))
            {
                return;
            }

            var request = ReadRecording(ref parameters);
            if (parameters.Failed || request is null)
            {
                return;
            }

            Record(diagnostics, request with { Follow = target.Follow }, reply);
        });

        methods.RegisterLibrary(IpcMethodNames.DiagTargets, (ref IpcParams _, IpcReply reply) =>
            reply.Write(new IpcDiagTargets([.. Targets(describe, diagnostics)]), IpcJsonContext.Default.IpcDiagTargets));

        methods.RegisterLibrary(IpcMethodNames.DiagCompare, (ref IpcParams parameters, IpcReply reply) =>
        {
            var margin = parameters.TryGetInt("margin", out var givenMargin) ? (int)givenMargin : DefaultMargin;
            if (parameters.Failed || !IpcTargetSelector.TryResolve(ref parameters, describe, diagnostics, margin, reply, out var target))
            {
                return;
            }

            var reference = parameters.GetString("reference");
            var tolerance = parameters.TryGetInt("channel", out var givenTolerance) ? (int)givenTolerance : 2;
            var pixels = parameters.TryGetInt("pixels", out var givenPixels) ? (int)givenPixels : 0;
            var diffPath = parameters.TryGetString("diff_path", out var givenDiff) ? givenDiff : null;
            var inline = parameters.TryGetBool("inline", out var givenInline) && givenInline;
            var timeout = parameters.TryGetInt("timeout_ms", out var givenTimeout) ? (int)givenTimeout : 2000;
            if (parameters.Failed)
            {
                return;
            }

            if (tolerance is < 0 or > 255 || pixels < 0 || timeout < 1)
            {
                reply.Error(IpcErrorCodes.InvalidParams, "'channel' must be 0 to 255, 'pixels' must not be negative and 'timeout_ms' must be at least 1.");
                return;
            }

            byte[] expected;
            int expectedWidth, expectedHeight;
            try
            {
                (expected, expectedWidth, expectedHeight) = Basin.Diagnostics.PngCodec.Decode(File.ReadAllBytes(reference));
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
                reply.Error(IpcErrorCodes.NotFound, $"No reference image is at '{reference}'.");
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
            {
                reply.Error(IpcErrorCodes.Failed, $"The reference '{reference}' is not a readable PNG. {exception.Message}");
                return;
            }

            var follow = target.Follow;
            var window = target.WindowId;
            IpcPendingReply? pending = null;
            IpcDiagComparison? early = null;
            var refused = diagnostics.Record(
                new RecordRequest(string.Empty, 1) { Follow = follow, Immediate = true, KeepPixels = true, TimeoutMs = timeout },
                result =>
                {
                    var comparison = Compare(window, result, expected, expectedWidth, expectedHeight, tolerance, pixels, diffPath, inline);
                    if (pending is null)
                    {
                        early = comparison;
                    }
                    else if (!pending.IsDone)
                    {
                        pending.Write(comparison, IpcJsonContext.Default.IpcDiagComparison);
                        _ = pending.Complete();
                    }
                });
            if (refused is not null)
            {
                reply.Error(IpcErrorCodes.Refused, refused);
                return;
            }

            if (early is { } done)
            {
                reply.Write(done, IpcJsonContext.Default.IpcDiagComparison);
                return;
            }

            pending = reply.Defer();
        });

        methods.RegisterLibrary(IpcMethodNames.DiagParts, (ref IpcParams parameters, IpcReply reply) =>
        {
            if (!TryFramed(ref parameters, describe, diagnostics, reply, out var info) || !TryParts(diagnostics, info, reply, out var parts))
            {
                return;
            }

            var wire = new IpcDiagPart[parts.Count];
            for (var i = 0; i < wire.Length; i++)
            {
                wire[i] = new IpcDiagPart(IpcWindowSelector.Name(parts[i].Part), Box(parts[i].Box));
            }

            reply.Write(new IpcDiagParts(info.Id, info.AppId, info.Title, wire), IpcJsonContext.Default.IpcDiagParts);
        });

        methods.RegisterLibrary(IpcMethodNames.DiagPlanesState, (ref IpcParams _, IpcReply reply) =>
        {
            var reports = diagnostics.Planes();
            var wire = new IpcDiagPlaneReport[reports.Count];
            for (var i = 0; i < wire.Length; i++)
            {
                wire[i] = Wire(reports[i]);
            }

            reply.Write(new IpcDiagPlaneState(wire), IpcJsonContext.Default.IpcDiagPlaneState);
        });

        methods.RegisterLibrary(IpcMethodNames.DiagPlaneTrace, (ref IpcParams parameters, IpcReply reply) =>
        {
            var on = parameters.GetBool("on");
            if (parameters.Failed)
            {
                return;
            }

            Trace(server, reply, IpcEventNames.DiagPlane, on, null);
        });

        methods.RegisterLibrary(IpcMethodNames.DiagPlanes, (ref IpcParams parameters, IpcReply reply) =>
        {
            var on = parameters.GetBool("on");
            if (parameters.Failed)
            {
                return;
            }

            diagnostics.AllowPlanes(on);
            IpcWrite.Empty(reply);
        });

        if (server.SyntheticInput is not { } input)
        {
            IpcDiagLines.Add(methods);
            return;
        }

        var guard = new IpcInputGuard(describe.Find<ICaptureExclusion>(), describe.PointerPosition);

        methods.RegisterLibrary(IpcMethodNames.DiagClick, (ref IpcParams parameters, IpcReply reply) =>
        {
            if (!TryFramed(ref parameters, describe, diagnostics, reply, out var info))
            {
                return;
            }

            var name = parameters.GetString("part");
            var buttonName = parameters.TryGetString("button", out var named) ? named : "left";
            if (parameters.Failed || !TryPartBox(diagnostics, info, name, reply, out var part, out var box))
            {
                return;
            }

            uint? button = buttonName switch { "left" => 0x110, "right" => 0x111, "middle" => 0x112, _ => null };
            if (button is not { } code)
            {
                reply.Error(IpcErrorCodes.InvalidParams, "'button' must be left, right or middle.");
                return;
            }

            var time = (uint)Environment.TickCount;
            var x = box.X + (box.Width / 2.0);
            var y = box.Y + (box.Height / 2.0);
            if (!guard.AllowsPoint(x, y, reply))
            {
                return;
            }

            var settleMs = parameters.TryGetInt("timeout_ms", out var givenSettle) ? (int)givenSettle : DefaultSettleMs;
            if (parameters.Failed)
            {
                return;
            }

            if (!input.PointerMotionAbsolute(time, x, y) || !input.PointerButton(time, code, true) || !input.PointerButton(time, code, false))
            {
                reply.Error(IpcErrorCodes.Refused, "The compositor refused the click.");
                return;
            }

            var pending = reply.Defer();
            var window = info.Id;
            var partName = IpcWindowSelector.Name(part);
            new IpcInputSettle(server, describe.Find<IFrameClock>(), describe.Toplevels, window, quietMs: 100, settleMs, (settled, waitedMs) =>
            {
                if (!pending.IsDone)
                {
                    pending.Write(new IpcDiagClick(window, partName, x, y, settled), IpcJsonContext.Default.IpcDiagClick);
                    _ = pending.Complete();
                }
            }).Start();
        });

        methods.RegisterLibrary(IpcMethodNames.DiagDrag, (ref IpcParams parameters, IpcReply reply) =>
        {
            if (!TryFramed(ref parameters, describe, diagnostics, reply, out var info))
            {
                return;
            }

            var dx = parameters.GetDouble("dx");
            var dy = parameters.GetDouble("dy");
            var name = parameters.TryGetString("part", out var named) ? named : "title";
            var steps = parameters.TryGetInt("steps", out var givenSteps) ? (int)givenSteps : 20;
            var interval = parameters.TryGetInt("interval_ms", out var givenInterval) ? (int)givenInterval : 50;
            var settle = parameters.TryGetInt("timeout_ms", out var givenSettle) ? (int)givenSettle : DefaultSettleMs;
            if (parameters.Failed || !TryPartBox(diagnostics, info, name, reply, out var part, out var box))
            {
                return;
            }

            if (steps is < 1 or > 600 || interval is < 1 or > 1000)
            {
                reply.Error(IpcErrorCodes.InvalidParams, "'steps' must be 1 to 600 and 'interval_ms' must be 1 to 1000.");
                return;
            }

            var fromX = box.X + (box.Width / 2.0);
            var fromY = box.Y + (box.Height / 2.0);
            if (!guard.AllowsPoint(fromX, fromY, reply) || !guard.AllowsPoint(fromX + dx, fromY + dy, reply))
            {
                return;
            }

            new IpcDragRun(
                server, input, describe.Find<IFrameClock>(), describe.Toplevels, reply.Defer(), info.Id, IpcWindowSelector.Name(part),
                fromX, fromY, dx, dy, steps, interval, Math.Max(1, settle)).Start();
        });
        IpcDiagLines.Add(methods);
    }

    private static readonly System.Text.Json.JsonElement MotionOptions = System.Text.Json.JsonDocument.Parse("""{"motion":true}""").RootElement;

    private static readonly System.Text.Json.JsonElement StackOptions = System.Text.Json.JsonDocument.Parse("""{"stack":true}""").RootElement;

    internal static IpcDiagFrame Wire(in FrameInfo frame, IpcDescribe describe)
    {
        var window = frame.Window is { } surface && describe.Toplevels is { } model && model.TryFind(surface, out var info) ? info.Id : 0;
        return new IpcDiagFrame(
            frame.Name, window, frame.Draws, frame.Animating, frame.Visible, Box(frame.Outer), frame.Active, frame.Maximized,
            frame.Resizing, IpcWindowSelector.Name(frame.Hot), IpcWindowSelector.Name(frame.PreviousHot),
            IpcWindowSelector.Name(frame.Pressed), frame.Inside, frame.NowMs, frame.HoverMs, frame.ActiveMs);
    }

    internal static IpcDiagPlaneReport Wire(PlaneReport report)
    {
        var entries = new IpcDiagPlaneEntry[report.Entries.Count];
        for (var i = 0; i < entries.Length; i++)
        {
            var entry = report.Entries[i];
            entries[i] = new IpcDiagPlaneEntry(entry.Path, Box(entry.Box), entry.OnPlane, entry.Reason, entry.PlaneBox is { } plane ? Box(plane) : null);
        }

        return new IpcDiagPlaneReport(report.Output, report.Skipped, entries);
    }

    private static IpcDiagComparison Compare(
        ulong window, RecordingResult result, byte[] expected, int expectedWidth, int expectedHeight, int tolerance, int pixels, string? diffPath,
        bool inline)
    {
        if (result.Pixels is not { } actual || result.Frames == 0)
        {
            return new IpcDiagComparison(
                window, false, false, 0, 0, expectedWidth, expectedHeight, 0, 0, default, tolerance, pixels, null, TimedOut: true);
        }

        var comparison = Basin.Diagnostics.ImageCompare.Compare(
            actual, result.Width, result.Height, expected, expectedWidth, expectedHeight, tolerance, out var diff);
        string? written = null;
        byte[]? png = null;
        if ((diffPath is not null || inline) && diff is not null)
        {
            png = Basin.Diagnostics.PngCodec.Encode(diff, result.Width, result.Height);
            if (diffPath is not null)
            {
                File.WriteAllBytes(diffPath, png);
                written = diffPath;
            }
        }

        return new IpcDiagComparison(
            window,
            comparison.SameSize && comparison.Changed <= pixels,
            comparison.SameSize,
            result.Width,
            result.Height,
            expectedWidth,
            expectedHeight,
            comparison.Changed,
            comparison.MaxDelta,
            Box(comparison.ChangedBox),
            tolerance,
            pixels,
            written,
            result.TimedOut,
            inline ? png : null);
    }

    private static System.Text.Json.JsonElement TargetOptions(string target)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new System.Text.Json.Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("target", target);
            writer.WriteEndObject();
        }

        using var document = System.Text.Json.JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    private static void Trace(IpcServer server, IpcReply reply, string name, bool on, System.Text.Json.JsonElement? options)
    {
        if (reply.Sink is not IIpcEventSubscriber subscriber)
        {
            reply.Error(IpcErrorCodes.Unavailable, "This front delivers no events. Subscribe through the control socket.");
            return;
        }

        if (on)
        {
            _ = server.Events.TrySubscribe(name, subscriber, options, out _);
        }
        else
        {
            server.Events.Unsubscribe(name, subscriber);
        }

        IpcWrite.Empty(reply);
    }

    private static IpcBox Box(in Box box) => new(box.X, box.Y, box.Width, box.Height);

    private static void RegisterPartsSource(IpcMethodRegistry methods, IWindowPartsSource source)
    {
        methods.RegisterLibrary(IpcMethodNames.DiagParts, (ref IpcParams parameters, IpcReply reply) =>
        {
            var name = parameters.GetString("window");
            if (parameters.Failed)
            {
                return;
            }

            var windows = new List<WindowParts>();
            source.CollectWindows(windows);
            var matches = windows.FindAll(window => window.AppId == name);
            if (matches.Count == 0)
            {
                matches = windows.FindAll(window => window.Title.Contains(name, StringComparison.Ordinal));
            }

            if (matches.Count != 1)
            {
                reply.Error(matches.Count == 0 ? IpcErrorCodes.NotFound : IpcErrorCodes.InvalidParams, matches.Count == 0
                    ? $"No decorated window has the app_id '{name}' or a title that contains it."
                    : $"'{name}' matches {matches.Count} windows. Give a more exact name: " + string.Join(", ", matches.Select(static m => $"app_id={m.AppId} title={m.Title}")) + ".");
                return;
            }

            var found = matches[0];
            var parts = new IpcDiagPart[found.Parts.Count];
            for (var i = 0; i < parts.Length; i++)
            {
                parts[i] = new IpcDiagPart(IpcWindowSelector.Name(found.Parts[i].Part), Box(found.Parts[i].Box));
            }

            reply.Write(new IpcDiagParts(0, found.AppId, found.Title, parts), IpcJsonContext.Default.IpcDiagParts);
        });

        methods.RegisterLibrary(IpcMethodNames.DiagTargets, (ref IpcParams _, IpcReply reply) =>
        {
            var windows = new List<WindowParts>();
            source.CollectWindows(windows);
            var targets = new IpcDiagTarget[windows.Count];
            for (var i = 0; i < targets.Length; i++)
            {
                var window = windows[i];
                targets[i] = new IpcDiagTarget("decorated", window.AppId, 0, window.AppId, window.Title, null, Box(window.Frame));
            }

            reply.Write(new IpcDiagTargets(targets), IpcJsonContext.Default.IpcDiagTargets);
        });
    }

    private static bool TryFramed(ref IpcParams parameters, IpcDescribe describe, ISceneDiagnostics diagnostics, IpcReply reply, out ToplevelInfo info)
    {
        info = default;
        if (!IpcTargetSelector.TryResolve(ref parameters, describe, diagnostics, 0, reply, out var target))
        {
            return false;
        }

        if (!target.HasFrame || target.Window is not { } window)
        {
            reply.Error(IpcErrorCodes.InvalidParams, $"'{target.Selector}' is a {target.Kind}. Only a window or an x11 window has frame parts.");
            return false;
        }

        info = window;
        return true;
    }

    private static List<IpcDiagTarget> Targets(IpcDescribe describe, ISceneDiagnostics diagnostics)
    {
        var targets = new List<IpcDiagTarget>();
        void Add(string kind, string selector, ToplevelInfo? info, Func<Box?>? follow)
        {
            var box = follow?.Invoke();
            string? output = null;
            if (box is { } found && describe.Layout is { } layout)
            {
                output = layout.OutputAt(found.X + (found.Width / 2.0), found.Y + (found.Height / 2.0))?.Name;
            }

            targets.Add(new IpcDiagTarget(kind, selector, info?.Id ?? 0, info?.AppId, info?.Title, output, box is { } b ? Box(b) : null));
        }

        if (describe.Toplevels is { } model)
        {
            var popups = describe.Find<IPopupDirectory>();
            foreach (var info in IpcWindowSelector.All(model))
            {
                var x11 = info.X11Display is not null;
                var selector = x11 ? $"x11:{info.AppId}" : info.Id.ToString(CultureInfo.InvariantCulture);
                Add(x11 ? "x11" : "window", selector, info, info.Surface is { } surface ? diagnostics.LocateWindow(surface, 0) : null);
                if (popups is not null && info.Surface is { } owner)
                {
                    var found = new List<Surface>();
                    popups.CollectPopups(owner, found);
                    for (var i = 0; i < found.Count; i++)
                    {
                        Add("popup", string.Create(CultureInfo.InvariantCulture, $"{info.Id}/popup:{i}"), info, diagnostics.LocateSurface(found[i], 0));
                    }
                }
            }
        }

        var layers = new List<LayerInfo>();
        describe.Find<ILayerDirectory>()?.CollectLayers(layers);
        foreach (var layer in layers)
        {
            Add("layer", $"layer:{layer.Namespace}", null, diagnostics.LocateSurface(layer.Surface, 0));
        }

        return targets;
    }

    private static RecordRequest? ReadRecording(ref IpcParams parameters)
    {
        var frames = (int)parameters.GetInt("frames");
        var path = parameters.TryGetString("path", out var givenPath) ? givenPath : string.Empty;
        var maxDimension = parameters.TryGetInt("max_dimension", out var max) ? (int)max : 0;
        var timeout = parameters.TryGetInt("timeout_ms", out var given) ? (int)given : 10_000;
        if (parameters.Failed)
        {
            return null;
        }

        if (frames is < 1 or > 600 || maxDimension < 0 || timeout < 1)
        {
            parameters.Invalid("'frames' must be 1 to 600, 'max_dimension' must not be negative and 'timeout_ms' must be at least 1.");
            return null;
        }

        return new RecordRequest(path, frames) { MaxDimension = maxDimension, TimeoutMs = timeout, InlinePng = path.Length == 0 };
    }

    private static void Record(ISceneDiagnostics diagnostics, RecordRequest request, IpcReply reply)
    {
        IpcPendingReply? pending = null;
        RecordingResult? early = null;
        var refused = diagnostics.Record(request, result =>
        {
            if (pending is null)
            {
                early = result;
                return;
            }

            Answer(pending, result);
        });
        if (refused is not null)
        {
            reply.Error(IpcErrorCodes.Refused, refused);
            return;
        }

        if (early is { } result)
        {
            reply.Write(Wire(result), IpcJsonContext.Default.IpcDiagRecording);
            return;
        }

        pending = reply.Defer();
    }

    private static void Answer(IpcPendingReply pending, RecordingResult result)
    {
        if (pending.IsDone)
        {
            return;
        }

        pending.Write(Wire(result), IpcJsonContext.Default.IpcDiagRecording);
        _ = pending.Complete();
    }

    private static IpcDiagRecording Wire(RecordingResult result)
    {
        var frames = new IpcDiagRecordFrame[result.PerFrame.Count];
        for (var i = 0; i < frames.Length; i++)
        {
            var frame = result.PerFrame[i];
            var planes = new IpcBox[frame.Planes.Count];
            for (var p = 0; p < planes.Length; p++)
            {
                planes[p] = Box(frame.Planes[p]);
            }

            frames[i] = new IpcDiagRecordFrame(frame.Index, Box(frame.At), frame.Luma, frame.Peak, frame.Delta, planes, frame.Unmapped);
        }

        return new IpcDiagRecording(
            result.Path, result.Frames, result.Width, result.Height, result.ImageWidth, result.ImageHeight,
            result.LumaLow, result.LumaHigh, result.PeakLow, result.PeakHigh, result.DeltaMax, result.TimedOut, frames, result.Png);
    }

    private static bool TryParts(ISceneDiagnostics diagnostics, in ToplevelInfo info, IpcReply reply, out List<(FramePart Part, Box Box)> parts)
    {
        var found = new List<(FramePart Part, Box Box)>();
        parts = found;
        if (info.Surface is not { } surface || !diagnostics.WriteParts(surface, (part, box) => found.Add((part, box))))
        {
            reply.Error(IpcErrorCodes.Refused, $"Window {info.Id} has no tracked frame.");
            return false;
        }

        return true;
    }

    private static bool TryPartBox(
        ISceneDiagnostics diagnostics, in ToplevelInfo info, string name, IpcReply reply, out FramePart wanted, out Box box)
    {
        box = default;
        if (!IpcWindowSelector.TryPart(name, out wanted))
        {
            reply.Error(IpcErrorCodes.InvalidParams, $"'{name}' is not a frame part.");
            return false;
        }

        if (!TryParts(diagnostics, info, reply, out var parts))
        {
            return false;
        }

        foreach (var (part, found) in parts)
        {
            if (part == wanted)
            {
                box = found;
                return true;
            }
        }

        reply.Error(IpcErrorCodes.NotFound, $"Window {info.Id} has no {IpcWindowSelector.Name(wanted)} part.");
        return false;
    }
}
