namespace Basin.Ipc;

public interface IApprovalBroker
{
    IpcApproval Request(IpcHeldCall call, string reason);

    bool IsAllowedForRun(string method);
}
