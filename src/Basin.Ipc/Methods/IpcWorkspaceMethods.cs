using Basin.Capabilities;

namespace Basin.Ipc;

internal static class IpcWorkspaceMethods
{
    public static void Register(IpcServer server, IpcDescribe describe)
    {
        if (describe.Workspaces is not { } model)
        {
            return;
        }

        var methods = server.Methods;
        methods.RegisterLibrary(IpcMethodNames.WorkspacesList, (ref IpcParams _, IpcReply reply) =>
            reply.Write(describe.WorkspaceList(), IpcJsonContext.Default.IpcWorkspaceList));

        methods.RegisterLibrary(IpcMethodNames.WorkspacesActivate, (ref IpcParams parameters, IpcReply reply) =>
            OnWorkspace(ref parameters, reply, model, WorkspaceRequestKind.Activate));
        methods.RegisterLibrary(IpcMethodNames.WorkspacesDeactivate, (ref IpcParams parameters, IpcReply reply) =>
            OnWorkspace(ref parameters, reply, model, WorkspaceRequestKind.Deactivate));
        methods.RegisterLibrary(IpcMethodNames.WorkspacesRemove, (ref IpcParams parameters, IpcReply reply) =>
            OnWorkspace(ref parameters, reply, model, WorkspaceRequestKind.Remove));

        methods.RegisterLibrary(IpcMethodNames.WorkspacesCreate, (ref IpcParams parameters, IpcReply reply) =>
        {
            if (parameters.Read(IpcJsonContext.Default.IpcWorkspaceCreateParams) is not { } request)
            {
                return;
            }

            IpcWindowMethods.Done(reply, model.Request(request.Group, new WorkspaceRequest(WorkspaceRequestKind.Create, Name: request.Name)));
        });

        methods.RegisterLibrary(IpcMethodNames.WorkspacesMove, (ref IpcParams parameters, IpcReply reply) =>
        {
            if (parameters.Read(IpcJsonContext.Default.IpcWorkspaceMoveParams) is not { } request)
            {
                return;
            }

            IpcWindowMethods.Done(reply, model.Request(request.Id, new WorkspaceRequest(WorkspaceRequestKind.Move, GroupId: request.Group)));
        });
    }

    private static void OnWorkspace(ref IpcParams parameters, IpcReply reply, IWorkspaceModel model, WorkspaceRequestKind kind)
    {
        if (parameters.Read(IpcJsonContext.Default.IpcIdParams) is not { } request)
        {
            return;
        }

        IpcWindowMethods.Done(reply, model.Request(request.Id, new WorkspaceRequest(kind)));
    }
}
