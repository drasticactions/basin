using Basin.Capabilities;

namespace Basin.Ipc;

internal static class IpcInputMethods
{
    public static void Register(IpcServer server, IpcDescribe describe)
    {
        var lookup = describe.Find<IKeymapLookup>();
        var commit = describe.Find<ITextInputCommit>();
        var exclusion = describe.Find<ICaptureExclusion>();
        var pointer = describe.PointerPosition;
        var points = new IpcWindowPoint(describe);
        if (server.SyntheticInput is { } synthetic)
        {
            RegisterGroup(
                server, new IpcSyntheticTarget(synthetic), lookup, describe.Find<IKeymapOverride>(), commit, points,
                new IpcInputGuard(exclusion, pointer), seat: false);
        }

        if (describe.Find<IInputSink>() is { } sink)
        {
            RegisterGroup(
                server, new IpcSinkTarget(sink, describe.Layout), lookup, null, commit, points, new IpcInputGuard(exclusion, pointer), seat: true);
        }
    }

    private static uint Now => (uint)Environment.TickCount;

    private static bool TryPoint(
        IpcWindowPoint points, ulong? window, double? x, double? y, bool raise, IpcReply reply, out bool move, out double atX, out double atY)
    {
        move = false;
        atX = x ?? 0;
        atY = y ?? 0;
        if (x is null != y is null)
        {
            reply.Error(IpcErrorCodes.InvalidParams, "Give 'x' and 'y' together, or neither.");
            return false;
        }

        if (window is { } id)
        {
            if (x is null)
            {
                reply.Error(IpcErrorCodes.InvalidParams, "'window' needs 'x' and 'y'. Give a point in the window's client area.");
                return false;
            }

            move = true;
            return points.TryResolve(id, atX, atY, raise, reply, out atX, out atY);
        }

        move = x is not null;
        return true;
    }

    private static void RegisterGroup(
        IpcServer server,
        IIpcInputTarget target,
        IKeymapLookup? lookup,
        IKeymapOverride? keymapOverride,
        ITextInputCommit? commit,
        IpcWindowPoint points,
        IpcInputGuard guard,
        bool seat)
    {
        var methods = server.Methods;
        methods.RegisterLibrary(seat ? IpcMethodNames.SeatPointerMove : IpcMethodNames.InputPointerMove, (ref IpcParams parameters, IpcReply reply) =>
        {
            if (parameters.Read(IpcJsonContext.Default.IpcPointerMoveParams) is { } request
                && TryPoint(points, request.Window, request.X, request.Y, request.Raise == true, reply, out _, out var x, out var y)
                && guard.AllowsPoint(x, y, reply))
            {
                Result(reply, target.Move(Now, x, y));
            }
        });

        methods.RegisterLibrary(seat ? IpcMethodNames.SeatPointerButton : IpcMethodNames.InputPointerButton, (ref IpcParams parameters, IpcReply reply) =>
        {
            if (parameters.Read(IpcJsonContext.Default.IpcPointerButtonParams) is not { } request
                || !TryPoint(points, request.Window, request.X, request.Y, request.Raise == true, reply, out var move, out var x, out var y)
                || !guard.AllowsPointer(move, x, y, reply))
            {
                return;
            }

            var time = Now;
            var button = request.Button.Code;
            if (move && !target.Move(time, x, y))
            {
                Result(reply, false);
                return;
            }

            Result(reply, request.Pressed is { } pressed
                ? target.Button(time, button, pressed)
                : target.Button(time, button, true) && target.Button(time, button, false));
        });

        methods.RegisterLibrary(seat ? IpcMethodNames.SeatAxis : IpcMethodNames.InputAxis, (ref IpcParams parameters, IpcReply reply) =>
        {
            if (parameters.Read(IpcJsonContext.Default.IpcAxisParams) is not { } request
                || !TryPoint(points, request.Window, request.X, request.Y, request.Raise == true, reply, out var move, out var x, out var y)
                || !guard.AllowsPointer(move, x, y, reply))
            {
                return;
            }

            uint? axis = (request.Axis ?? "vertical") switch { "vertical" => 0, "horizontal" => 1, _ => null };
            uint? source = (request.Source ?? "wheel") switch { "wheel" => 0, "finger" => 1, "continuous" => 2, "wheel-tilt" => 3, _ => null };
            if (axis is null || source is null)
            {
                reply.Error(IpcErrorCodes.InvalidParams, "'axis' must be vertical or horizontal. 'source' must be wheel, finger, continuous or wheel-tilt.");
                return;
            }

            var time = Now;
            if (move && !target.Move(time, x, y))
            {
                Result(reply, false);
                return;
            }

            Result(reply, target.Axis(time, axis.Value, request.Value, source.Value));
        });

        methods.RegisterLibrary(seat ? IpcMethodNames.SeatKey : IpcMethodNames.InputKey, (ref IpcParams parameters, IpcReply reply) =>
        {
            if (parameters.Read(IpcJsonContext.Default.IpcKeyParams) is not { } request || !guard.AllowsKeyboard(reply))
            {
                return;
            }

            if (request.Code is < 0 or > 767)
            {
                reply.Error(IpcErrorCodes.InvalidParams, "'code' must be an evdev keycode.");
                return;
            }

            var time = Now;
            var code = (uint)request.Code;
            Result(reply, request.Pressed is { } pressed
                ? target.Key(reply, time, code, pressed)
                : target.Key(reply, time, code, true) && target.Key(reply, time, code, false));
        });

        methods.RegisterLibrary(seat ? IpcMethodNames.SeatTouch : IpcMethodNames.InputTouch, (ref IpcParams parameters, IpcReply reply) =>
        {
            if (parameters.Read(IpcJsonContext.Default.IpcTouchParams) is not { } request)
            {
                return;
            }

            var kind = request.Kind;
            if (kind is not ("down" or "motion" or "up" or "frame" or "cancel"))
            {
                reply.Error(IpcErrorCodes.InvalidParams, "'kind' must be down, motion, up, frame or cancel.");
                return;
            }

            if (kind is "down" or "motion" && (request.X is null || request.Y is null))
            {
                reply.Error(IpcErrorCodes.InvalidParams, $"A touch {kind} needs 'x' and 'y'.");
                return;
            }

            if (kind is "down" or "motion" && !guard.AllowsPoint(request.X!.Value, request.Y!.Value, reply))
            {
                return;
            }

            var time = Now;
            if (parameters.TryGetInt("time_ms", out var given))
            {
                if (given is < 0 or > uint.MaxValue)
                {
                    reply.Error(IpcErrorCodes.InvalidParams, "'time_ms' must be a millisecond timestamp from 0 to 2^32-1.");
                    return;
                }

                time = (uint)given;
            }
            else if (parameters.Failed)
            {
                return;
            }

            Result(reply, target.Touch(time, kind, request.Id ?? 0, request.X ?? 0, request.Y ?? 0));
        });

        var typer = lookup is null ? null : new TextTyper(lookup, keymapOverride);
        var keyTarget = new ReplyKeyTarget(target);
        if (lookup is not null || commit is not null)
        {
            methods.RegisterLibrary(seat ? IpcMethodNames.SeatText : IpcMethodNames.InputText, (ref IpcParams parameters, IpcReply reply) =>
            {
                if (parameters.Read(IpcJsonContext.Default.IpcTextParams) is not { Text: var text } request || !guard.AllowsKeyboard(reply))
                {
                    return;
                }

                var via = request.Via ?? "auto";
                if (via is not ("auto" or "keymap" or "text-input"))
                {
                    reply.Error(IpcErrorCodes.InvalidParams, "'via' must be auto, keymap or text-input.");
                    return;
                }

                if (via != "keymap" && commit is { HasActiveTextInput: true })
                {
                    Result(reply, commit.TryCommitString(text), TextInputVia);
                    return;
                }

                if (via == "text-input")
                {
                    reply.Error(IpcErrorCodes.Refused, "The focused client has no enabled text input. Use 'via': \"keymap\" instead.");
                    return;
                }

                if (lookup is null)
                {
                    reply.Error(IpcErrorCodes.Refused, "The compositor has no keymap lookup, and the focused client has no enabled text input.");
                    return;
                }

                keyTarget.Reply = reply;
                keyTarget.Failed = false;
                var typed = typer!.Type(text, Now, keyTarget, out var error);
                keyTarget.Reply = null;
                if (error is not null && typed == 0)
                {
                    reply.Error(IpcErrorCodes.InvalidParams, error);
                    return;
                }

                if (error is not null)
                {
                    reply.Error(IpcErrorCodes.Failed, $"Typing stopped after {typed} characters. {error}");
                    return;
                }

                Result(reply, !keyTarget.Failed, KeymapVia);
            });
        }

        if (lookup is null)
        {
            return;
        }

        var codes = new List<uint>();
        var codeScratch = Array.Empty<uint>();
        methods.RegisterLibrary(seat ? IpcMethodNames.SeatChord : IpcMethodNames.InputChord, (ref IpcParams parameters, IpcReply reply) =>
        {
            if (parameters.Read(IpcJsonContext.Default.IpcChordParams) is not { Chord: var chord } || !guard.AllowsKeyboard(reply))
            {
                return;
            }

            if (!IpcKeys.TryResolveChord(chord, lookup, codes, out var error))
            {
                reply.Error(IpcErrorCodes.InvalidParams, error!);
                return;
            }

            var time = Now;
            var ok = true;
            foreach (var code in codes)
            {
                ok &= target.Key(reply, time, code, true);
            }

            for (var i = codes.Count - 1; i >= 0; i--)
            {
                ok &= target.Key(reply, time, codes[i], false);
            }

            if (!ok)
            {
                Result(reply, false);
                return;
            }

            if (codeScratch.Length < codes.Count)
            {
                codeScratch = new uint[Math.Max(codes.Count, 8)];
            }

            codes.CopyTo(codeScratch);
            reply.Write(new IpcChordResult(new ReadOnlyMemory<uint>(codeScratch, 0, codes.Count)), IpcJsonContext.Default.IpcChordResult);
        });
    }

    private const string TextInputVia = "text-input";
    private const string KeymapVia = "keymap";

    private static void Result(IpcReply reply, bool accepted) => IpcWindowMethods.Done(reply, accepted);

    private static void Result(IpcReply reply, bool accepted, string via)
    {
        if (!accepted)
        {
            IpcWindowMethods.Done(reply, false);
            return;
        }

        reply.Write(new IpcTextResult(via), IpcJsonContext.Default.IpcTextResult);
    }

    private sealed class ReplyKeyTarget(IIpcInputTarget target) : ITextKeyTarget
    {
        public IpcReply? Reply { get; set; }

        public bool Failed { get; set; }

        public bool Key(uint timeMs, uint keycode, bool pressed)
        {
            var ok = Reply is { } reply && target.Key(reply, timeMs, keycode, pressed);
            Failed |= !ok;
            return ok;
        }
    }
}
