using Basin.Capabilities;

namespace Basin.Ipc;

internal static class IpcInputMethods
{
    public static void Register(IpcServer server, IpcDescribe describe)
    {
        var lookup = describe.Find<IKeymapLookup>();
        if (server.SyntheticInput is { } synthetic)
        {
            RegisterGroup(server, new IpcSyntheticTarget(synthetic), lookup, seat: false);
        }

        if (describe.Find<IInputSink>() is { } sink)
        {
            RegisterGroup(server, new IpcSinkTarget(sink, describe.Layout), lookup, seat: true);
        }
    }

    private static uint Now => (uint)Environment.TickCount;

    private static void RegisterGroup(IpcServer server, IIpcInputTarget target, IKeymapLookup? lookup, bool seat)
    {
        var methods = server.Methods;
        methods.RegisterLibrary(seat ? IpcMethodNames.SeatPointerMove : IpcMethodNames.InputPointerMove, (ref IpcParams parameters, IpcReply reply) =>
        {
            if (parameters.Read(IpcJsonContext.Default.IpcPointerMoveParams) is { } request)
            {
                Result(reply, target.Move(Now, request.X, request.Y));
            }
        });

        methods.RegisterLibrary(seat ? IpcMethodNames.SeatPointerButton : IpcMethodNames.InputPointerButton, (ref IpcParams parameters, IpcReply reply) =>
        {
            if (parameters.Read(IpcJsonContext.Default.IpcPointerButtonParams) is not { } request)
            {
                return;
            }

            var time = Now;
            var button = request.Button.Code;
            Result(reply, request.Pressed is { } pressed
                ? target.Button(time, button, pressed)
                : target.Button(time, button, true) && target.Button(time, button, false));
        });

        methods.RegisterLibrary(seat ? IpcMethodNames.SeatAxis : IpcMethodNames.InputAxis, (ref IpcParams parameters, IpcReply reply) =>
        {
            if (parameters.Read(IpcJsonContext.Default.IpcAxisParams) is not { } request)
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

            Result(reply, target.Axis(Now, axis.Value, request.Value, source.Value));
        });

        methods.RegisterLibrary(seat ? IpcMethodNames.SeatKey : IpcMethodNames.InputKey, (ref IpcParams parameters, IpcReply reply) =>
        {
            if (parameters.Read(IpcJsonContext.Default.IpcKeyParams) is not { } request)
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

            Result(reply, target.Touch(Now, kind, request.Id ?? 0, request.X ?? 0, request.Y ?? 0));
        });

        if (lookup is null)
        {
            return;
        }

        var codes = new List<uint>();
        var codeScratch = Array.Empty<uint>();
        var keys = new List<(uint Code, uint Mask)>();
        methods.RegisterLibrary(seat ? IpcMethodNames.SeatChord : IpcMethodNames.InputChord, (ref IpcParams parameters, IpcReply reply) =>
        {
            if (parameters.Read(IpcJsonContext.Default.IpcChordParams) is not { Chord: var chord })
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

        methods.RegisterLibrary(seat ? IpcMethodNames.SeatText : IpcMethodNames.InputText, (ref IpcParams parameters, IpcReply reply) =>
        {
            if (parameters.Read(IpcJsonContext.Default.IpcTextParams) is not { Text: var text })
            {
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

            Result(reply, ok);
        });
    }

    private static void Result(IpcReply reply, bool accepted) => IpcWindowMethods.Done(reply, accepted);
}
