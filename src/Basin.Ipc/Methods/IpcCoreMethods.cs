namespace Basin.Ipc;

internal static class IpcCoreMethods
{
    public static void Register(IpcServer server)
    {
        var methods = server.Methods;
        methods.RegisterLibrary(IpcMethodNames.Version, (ref IpcParams _, IpcReply reply) =>
            reply.Write(
                new IpcVersion(IpcProtocol.Version, server.Session.Compositor, server.Session.BasinVersion),
                IpcJsonContext.Default.IpcVersion));

        methods.RegisterLibrary(IpcMethodNames.Methods, (ref IpcParams parameters, IpcReply reply) =>
        {
            if (parameters.Read(IpcJsonContext.Default.IpcMethodsParams) is not { } request)
            {
                return;
            }

            if (request.Detail != true)
            {
                reply.Write(new IpcMethodList(methods.SortedNames), IpcJsonContext.Default.IpcMethodList);
                return;
            }

            reply.Write(new IpcMethodDetailList(methods.Details ??= Details(methods)), IpcJsonContext.Default.IpcMethodDetailList);
        });

        methods.RegisterLibrary(IpcMethodNames.Events, (ref IpcParams _, IpcReply reply) =>
            reply.Write(new IpcEventList(server.Events.SortedNames), IpcJsonContext.Default.IpcEventList));

        methods.RegisterLibrary(IpcMethodNames.Subscribe, (ref IpcParams parameters, IpcReply reply) =>
        {
            if (parameters.Read(IpcJsonContext.Default.IpcEventsParams) is not { } request)
            {
                return;
            }

            if (request.Events is not { } events)
            {
                reply.Error(IpcErrorCodes.InvalidParams, "'events' is required");
                return;
            }

            if (reply.Sink is not IIpcEventSubscriber subscriber)
            {
                reply.Error(IpcErrorCodes.Unavailable, "this front delivers no events");
                return;
            }

            foreach (var name in events)
            {
                if (!server.Events.IsDeclared(name))
                {
                    reply.Error(IpcErrorCodes.InvalidParams, $"no event '{name}' in this session");
                    return;
                }
            }

            foreach (var name in events)
            {
                _ = server.Events.Subscribe(name, subscriber);
            }

            reply.Write(new IpcEventList(events.ToArray()), IpcJsonContext.Default.IpcEventList);
        });

        methods.RegisterLibrary(IpcMethodNames.Unsubscribe, (ref IpcParams parameters, IpcReply reply) =>
        {
            if (reply.Sink is not IIpcEventSubscriber subscriber)
            {
                reply.Error(IpcErrorCodes.Unavailable, "this front delivers no events");
                return;
            }

            if (parameters.Read(IpcJsonContext.Default.IpcEventsParams) is not { } request)
            {
                return;
            }

            if (request.Events is not { } events)
            {
                server.Events.UnsubscribeAll(subscriber);
                reply.Write(new IpcEventList(ReadOnlyMemory<string>.Empty), IpcJsonContext.Default.IpcEventList);
                return;
            }

            foreach (var name in events)
            {
                server.Events.Unsubscribe(name, subscriber);
            }

            reply.Write(new IpcEventList(events.ToArray()), IpcJsonContext.Default.IpcEventList);
        });
    }

    private static IpcMethodDetail[] Details(IpcMethodRegistry methods)
    {
        var names = methods.SortedNames;
        var details = new IpcMethodDetail[names.Length];
        for (var i = 0; i < details.Length; i++)
        {
            details[i] = Detail(methods, names[i]);
        }

        return details;
    }

    private static IpcMethodDetail Detail(IpcMethodRegistry methods, string name)
    {
        var known = methods.TryGetInfo(name, out var info);
        var traits = known ? info.Traits : IpcMethodTraits.None;
        var schema = methods.SchemaMemory(name);
        return new IpcMethodDetail(
            name,
            known ? info.Description : null,
            new IpcRawJson(schema),
            (traits & IpcMethodTraits.ReadOnly) != 0,
            (traits & IpcMethodTraits.Destructive) != 0,
            (traits & IpcMethodTraits.Idempotent) != 0,
            methods.LineOf(name));
    }
}
