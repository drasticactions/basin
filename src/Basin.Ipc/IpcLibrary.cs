namespace Basin.Ipc;

internal static class IpcLibrary
{
    public static void Register(IpcServer server)
    {
        IpcCoreMethods.Register(server);
        IpcSessionMethods.Register(server);
        var describe = server.Describe;
        IpcOutputMethods.Register(server, describe);
        IpcWindowMethods.Register(server, describe);
        IpcWorkspaceMethods.Register(server, describe);
        IpcSessionStateMethods.Register(server, describe);
        IpcCaptureMethods.Register(server, describe);
        IpcProcessMethods.Register(server);
        IpcClipboardMethods.Register(server, describe);
        IpcInputMethods.Register(server, describe);
        IpcApprovalMethods.Register(server);
        IpcEvents.Declare(server, describe);
    }
}
