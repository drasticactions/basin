using Basin.Capabilities;

namespace Basin.Ipc;

internal static class IpcWindowMethods
{
    private const int DefaultWaitMs = 5000;
    private const int MaxWaitMs = 3_600_000;
    private const int DefaultQuietMs = 300;
    private const int DefaultIgnoreBelow = 64;

    public static void Register(IpcServer server, IpcDescribe describe)
    {
        var methods = server.Methods;
        if (describe.Toplevels is { } model)
        {
            methods.RegisterLibrary(IpcMethodNames.WindowsList, (ref IpcParams _, IpcReply reply) =>
                reply.Write(describe.WindowList(), IpcJsonContext.Default.IpcWindowList));

            methods.RegisterLibrary(IpcMethodNames.WindowsGet, (ref IpcParams parameters, IpcReply reply) =>
            {
                if (parameters.Read(IpcJsonContext.Default.IpcIdParams) is not { } request || !Found(model, request.Id, reply, out var info))
                {
                    return;
                }

                reply.Write(describe.Window(info, describe.WorkspaceOf()), IpcJsonContext.Default.IpcWindow);
            });

            methods.RegisterLibrary(IpcMethodNames.WindowsActivate, (ref IpcParams parameters, IpcReply reply) =>
                Simple(ref parameters, reply, model, ToplevelRequestKind.Activate));

            methods.RegisterLibrary(IpcMethodNames.WindowsClose, (ref IpcParams parameters, IpcReply reply) =>
                Simple(ref parameters, reply, model, ToplevelRequestKind.Close));

            methods.RegisterLibrary(IpcMethodNames.WindowsSetState, (ref IpcParams parameters, IpcReply reply) =>
            {
                if (parameters.Read(IpcJsonContext.Default.IpcSetStateParams) is not { } request || !Found(model, request.Id, reply, out _))
                {
                    return;
                }

                Span<ToplevelRequestKind> kinds = stackalloc ToplevelRequestKind[4];
                var count = 0;
                if (request.Maximized is { } maximized)
                {
                    kinds[count++] = maximized ? ToplevelRequestKind.Maximize : ToplevelRequestKind.Unmaximize;
                }

                if (request.Minimized is { } minimized)
                {
                    kinds[count++] = minimized ? ToplevelRequestKind.Minimize : ToplevelRequestKind.Unminimize;
                }

                if (request.Fullscreen is { } fullscreen)
                {
                    kinds[count++] = fullscreen ? ToplevelRequestKind.Fullscreen : ToplevelRequestKind.Unfullscreen;
                }

                if (request.NoBorder is { } noBorder)
                {
                    kinds[count++] = noBorder ? ToplevelRequestKind.SetNoBorder : ToplevelRequestKind.UnsetNoBorder;
                }

                if (count == 0)
                {
                    reply.Error(IpcErrorCodes.InvalidParams, "name at least one of maximized, minimized, fullscreen, no_border");
                    return;
                }

                foreach (var kind in kinds[..count])
                {
                    if (!model.Request(request.Id, new ToplevelRequest(kind)))
                    {
                        Refused(reply, kind);
                        return;
                    }
                }

                IpcWrite.Empty(reply);
            });

            methods.RegisterLibrary(IpcMethodNames.WindowsMove, (ref IpcParams parameters, IpcReply reply) =>
            {
                if (parameters.Read(IpcJsonContext.Default.IpcMoveParams) is not { } request || !Found(model, request.Id, reply, out var info))
                {
                    return;
                }

                var box = new Box(request.X, request.Y, info.Geometry.Width, info.Geometry.Height);
                Send(reply, model, request.Id, new ToplevelRequest(ToplevelRequestKind.Move, Geometry: box), info.State);
            });

            methods.RegisterLibrary(IpcMethodNames.WindowsResize, (ref IpcParams parameters, IpcReply reply) =>
            {
                if (parameters.Read(IpcJsonContext.Default.IpcResizeParams) is not { } request || !Found(model, request.Id, reply, out var info))
                {
                    return;
                }

                if (request.Width <= 0 || request.Height <= 0 || request.Width > 65535 || request.Height > 65535)
                {
                    reply.Error(IpcErrorCodes.InvalidParams, "width and height are between 1 and 65535");
                    return;
                }

                var box = new Box(info.Geometry.X, info.Geometry.Y, request.Width, request.Height);
                Send(reply, model, request.Id, new ToplevelRequest(ToplevelRequestKind.Resize, Geometry: box), info.State);
            });

            methods.RegisterLibrary(IpcMethodNames.WindowsSendToOutput, (ref IpcParams parameters, IpcReply reply) =>
            {
                if (parameters.Read(IpcJsonContext.Default.IpcSendToOutputParams) is not { } request || !Found(model, request.Id, reply, out _))
                {
                    return;
                }

                if (describe.OutputNamed(request.Output) is not { } output)
                {
                    reply.Error(IpcErrorCodes.NotFound, $"no output '{request.Output}'");
                    return;
                }

                Send(reply, model, request.Id, new ToplevelRequest(ToplevelRequestKind.SendToOutput, output));
            });

            if (describe.Workspaces is { } workspaces)
            {
                methods.RegisterLibrary(IpcMethodNames.WindowsSendToWorkspace, (ref IpcParams parameters, IpcReply reply) =>
                {
                    if (parameters.Read(IpcJsonContext.Default.IpcSendToWorkspaceParams) is not { } request || !Found(model, request.Id, reply, out _))
                    {
                        return;
                    }

                    Done(reply, workspaces.Request(request.Workspace, new WorkspaceRequest(WorkspaceRequestKind.Assign, ToplevelId: request.Id)));
                });
            }

            methods.RegisterLibrary(IpcMethodNames.WindowsWait, (ref IpcParams parameters, IpcReply reply) =>
            {
                if (parameters.Read(IpcJsonContext.Default.IpcWaitParams) is not { } request)
                {
                    return;
                }

                var timeout = request.TimeoutMs ?? DefaultWaitMs;
                if (request.AppId is null && request.Title is null)
                {
                    reply.Error(IpcErrorCodes.InvalidParams, "name an app_id, a title, or both");
                    return;
                }

                if (timeout <= 0 || timeout > MaxWaitMs)
                {
                    reply.Error(IpcErrorCodes.InvalidParams, $"timeout_ms is between 1 and {MaxWaitMs}");
                    return;
                }

                foreach (var info in describe.Windows())
                {
                    if (IpcWindowWait.Matches(info, request.AppId, request.Title))
                    {
                        reply.Write(describe.Window(info, describe.WorkspaceOf()), IpcJsonContext.Default.IpcWindow);
                        return;
                    }
                }

                new IpcWindowWait(server, describe, model, request.AppId, request.Title, reply.Defer()).Start((int)timeout);
            });

            methods.RegisterLibrary(IpcMethodNames.WindowsWaitIdle, (ref IpcParams parameters, IpcReply reply) =>
            {
                if (parameters.Read(IpcJsonContext.Default.IpcWaitIdleParams) is not { } request)
                {
                    return;
                }

                var quiet = request.QuietMs ?? DefaultQuietMs;
                var ignore = request.IgnoreBelow ?? DefaultIgnoreBelow;
                var timeout = request.TimeoutMs ?? DefaultWaitMs;
                if (quiet <= 0 || quiet > MaxWaitMs || timeout <= 0 || timeout > MaxWaitMs)
                {
                    reply.Error(IpcErrorCodes.InvalidParams, $"quiet_ms and timeout_ms are between 1 and {MaxWaitMs}");
                    return;
                }

                if (ignore < 0 || ignore > 65535)
                {
                    reply.Error(IpcErrorCodes.InvalidParams, "ignore_below is between 0 and 65535");
                    return;
                }

                var target = request.Id ?? 0;
                if (target != 0)
                {
                    if (!Found(model, target, reply, out _))
                    {
                        return;
                    }

                    if (!model.ReportsCommits(target))
                    {
                        reply.Error(IpcErrorCodes.Refused, $"the compositor does not report the commits of window {target}");
                        return;
                    }
                }

                new IpcWindowIdle(server, model, target, (int)quiet, (int)ignore, (int)timeout, reply.Defer()).Start();
            });
        }

        if (describe.Stack is not null)
        {
            methods.RegisterLibrary(IpcMethodNames.WindowsStack, (ref IpcParams _, IpcReply reply) =>
                reply.Write(describe.StackIds(), IpcJsonContext.Default.IpcStack));
        }
    }

    internal static bool Found(IToplevelModel model, ulong id, IpcReply reply, out ToplevelInfo info)
    {
        if (model.TryGet(id, out info))
        {
            return true;
        }

        reply.Error(IpcErrorCodes.NotFound, $"no window {id}");
        return false;
    }

    internal static void Done(IpcReply reply, bool accepted)
    {
        if (!accepted)
        {
            reply.Error(IpcErrorCodes.Refused, "the compositor declined the request");
            return;
        }

        IpcWrite.Empty(reply);
    }

    private static void Refused(IpcReply reply, ToplevelRequestKind kind, ulong id = 0, ToplevelState state = ToplevelState.None)
    {
        var reason = (state & ToplevelState.Fullscreen) != 0 ? "fullscreen"
            : (state & ToplevelState.Maximized) != 0 ? "maximized"
            : null;
        reply.Error(IpcErrorCodes.Refused, reason is null
            ? $"the compositor declined {kind}"
            : $"the compositor declined {kind}: window {id} is {reason}, and only a floating window moves or resizes");
    }

    private static void Send(
        IpcReply reply, IToplevelModel model, ulong id, in ToplevelRequest request, ToplevelState state = ToplevelState.None)
    {
        if (!model.Request(id, request))
        {
            Refused(reply, request.Kind, id, state);
            return;
        }

        IpcWrite.Empty(reply);
    }

    private static void Simple(ref IpcParams parameters, IpcReply reply, IToplevelModel model, ToplevelRequestKind kind)
    {
        if (parameters.Read(IpcJsonContext.Default.IpcIdParams) is not { } request || !Found(model, request.Id, reply, out _))
        {
            return;
        }

        Send(reply, model, request.Id, new ToplevelRequest(kind));
    }
}
