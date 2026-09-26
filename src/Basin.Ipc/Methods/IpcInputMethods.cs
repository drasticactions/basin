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
            RegisterGroup(server, new IpcSyntheticTarget(synthetic), lookup, commit, points, new Guard(exclusion, pointer), seat: false);
        }

        if (describe.Find<IInputSink>() is { } sink)
        {
            RegisterGroup(server, new IpcSinkTarget(sink, describe.Layout), lookup, commit, points, new Guard(exclusion, pointer), seat: true);
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
            reply.Error(IpcErrorCodes.InvalidParams, "'x' and 'y' go together");
            return false;
        }

        if (window is { } id)
        {
            if (x is null)
            {
                reply.Error(IpcErrorCodes.InvalidParams, "'window' needs 'x' and 'y' in the window's client area");
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
        ITextInputCommit? commit,
        IpcWindowPoint points,
        Guard guard,
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
                reply.Error(IpcErrorCodes.InvalidParams, "'axis' is vertical or horizontal; 'source' is wheel, finger, continuous or wheel-tilt");
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
                reply.Error(IpcErrorCodes.InvalidParams, "'code' is an evdev keycode");
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
                reply.Error(IpcErrorCodes.InvalidParams, "'kind' is down, motion, up, frame or cancel");
                return;
            }

            if (kind is "down" or "motion" && (request.X is null || request.Y is null))
            {
                reply.Error(IpcErrorCodes.InvalidParams, $"a touch {kind} names x and y");
                return;
            }

            if (kind is "down" or "motion" && !guard.AllowsPoint(request.X!.Value, request.Y!.Value, reply))
            {
                return;
            }

            Result(reply, target.Touch(Now, kind, request.Id ?? 0, request.X ?? 0, request.Y ?? 0));
        });

        var keys = new List<(uint Code, uint Mask)>();
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
                    reply.Error(IpcErrorCodes.InvalidParams, "'via' is auto, keymap or text-input");
                    return;
                }

                if (via != "keymap" && commit is { HasActiveTextInput: true })
                {
                    Result(reply, commit.TryCommitString(text), TextInputVia);
                    return;
                }

                if (via == "text-input")
                {
                    reply.Error(IpcErrorCodes.Refused, "the focused client has no enabled text input");
                    return;
                }

                if (lookup is null)
                {
                    reply.Error(IpcErrorCodes.Refused, "the compositor has no keymap lookup, and the focused client has no enabled text input");
                    return;
                }

                if (!IpcKeys.TryResolveText(text, lookup, keys, out var error))
                {
                    reply.Error(IpcErrorCodes.InvalidParams, error!);
                    return;
                }

                var time = Now;
                var ok = true;
                foreach (var (code, mask) in keys)
                {
                    var modifier = mask == 0 ? null : IpcKeys.ModifierCode(lookup, mask);
                    if (modifier is { } held)
                    {
                        ok &= target.Key(reply, time, held, true);
                    }

                    ok &= target.Key(reply, time, code, true);
                    ok &= target.Key(reply, time, code, false);
                    if (modifier is { } released)
                    {
                        ok &= target.Key(reply, time, released, false);
                    }
                }

                Result(reply, ok, KeymapVia);
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

    private sealed class Guard(ICaptureExclusion? exclusion, Func<(double X, double Y)?>? pointer)
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
}
