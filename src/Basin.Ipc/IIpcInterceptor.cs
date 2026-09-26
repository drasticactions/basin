namespace Basin.Ipc;

public interface IIpcInterceptor
{
    IpcDecision Before(string method, ReadOnlySpan<byte> parameters, IpcCallContext context);

    void After(string method, IpcCallOutcome outcome, TimeSpan elapsed, IpcCallContext context);
}
