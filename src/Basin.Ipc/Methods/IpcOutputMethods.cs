using Basin.Capabilities;

namespace Basin.Ipc;

internal static class IpcOutputMethods
{
    public static void Register(IpcServer server, IpcDescribe describe)
    {
        if (describe.Outputs is null && describe.Layout is null)
        {
            return;
        }

        var methods = server.Methods;
        methods.RegisterLibrary(IpcMethodNames.OutputsList, (ref IpcParams _, IpcReply reply) =>
            reply.Write(describe.OutputList(), IpcJsonContext.Default.IpcOutputList));

        if (describe.Configuration is { } configuration)
        {
            methods.RegisterLibrary(IpcMethodNames.OutputsTest, (ref IpcParams parameters, IpcReply reply) =>
            {
                if (ReadEntries(ref parameters, reply, describe) is not { } entries)
                {
                    return;
                }

                reply.Write(new IpcOutputTestResult(configuration.Test(entries)), IpcJsonContext.Default.IpcOutputTestResult);
            });

            methods.RegisterLibrary(IpcMethodNames.OutputsApply, (ref IpcParams parameters, IpcReply reply) =>
            {
                if (ReadEntries(ref parameters, reply, describe) is not { } entries)
                {
                    return;
                }

                if (!configuration.Apply(entries))
                {
                    reply.Error(IpcErrorCodes.Failed, configuration.LastFailureReason ?? "the configuration did not apply");
                    return;
                }

                IpcWrite.Empty(reply);
            });
        }

        if (describe.Power is { } power)
        {
            methods.RegisterLibrary(IpcMethodNames.OutputsPower, (ref IpcParams parameters, IpcReply reply) =>
            {
                if (parameters.Read(IpcJsonContext.Default.IpcPowerParams) is not { } request)
                {
                    return;
                }

                if (describe.OutputNamed(request.Output) is not { } output)
                {
                    reply.Error(IpcErrorCodes.NotFound, $"no output '{request.Output}'");
                    return;
                }

                IpcWindowMethods.Done(reply, power.SetOn(output, request.On));
            });
        }
    }

    private static List<OutputConfigurationEntry>? ReadEntries(ref IpcParams parameters, IpcReply reply, IpcDescribe describe)
    {
        if (parameters.Read(IpcJsonContext.Default.IpcOutputsParams) is not { } request)
        {
            return null;
        }

        var entries = new List<OutputConfigurationEntry>(request.Entries.Count);
        foreach (var change in request.Entries)
        {
            if (change is null)
            {
                reply.Error(IpcErrorCodes.InvalidParams, "'entries' must be an array of objects");
                return null;
            }

            if (Entry(change, reply, describe) is not { } parsed)
            {
                return null;
            }

            entries.Add(parsed);
        }

        return entries;
    }

    private static OutputConfigurationEntry? Entry(IpcOutputChange change, IpcReply reply, IpcDescribe describe)
    {
        if ((change.Missing ?? change.Mode?.Missing) is { } missing)
        {
            reply.Error(IpcErrorCodes.InvalidParams, missing);
            return null;
        }

        if (describe.OutputNamed(change.Name) is not { } output)
        {
            reply.Error(IpcErrorCodes.NotFound, $"no output '{change.Name}'");
            return null;
        }

        var result = new OutputConfigurationEntry
        {
            Output = output,
            Enabled = change.Enabled ?? output.Enabled,
        };

        if (change.Mode is { } mode)
        {
            result = result with { Mode = new OutputMode(mode.Width, mode.Height, mode.RefreshMhz ?? output.CurrentMode.RefreshMilliHz) };
        }

        if (change.Position is { } position)
        {
            result = result with { Position = new Point(position.X, position.Y) };
        }

        if (change.Scale is { } scale)
        {
            if (scale <= 0 || scale > 16)
            {
                reply.Error(IpcErrorCodes.InvalidParams, "'scale' is above 0 and at most 16");
                return null;
            }

            result = result with { Scale = scale };
        }

        if (change.Transform is { } transform)
        {
            if (!IpcWrite.TryParseTransform(transform, out var parsed))
            {
                reply.Error(IpcErrorCodes.InvalidParams, $"unknown transform '{transform}'");
                return null;
            }

            result = result with { Transform = parsed };
        }

        if (change.AdaptiveSync is { } adaptive)
        {
            result = result with { AdaptiveSync = adaptive };
        }

        return result;
    }
}
