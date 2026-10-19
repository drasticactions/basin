using Basin.Capabilities;

namespace Basin.Ipc;

internal sealed class IpcDiagPlaneEvents(IpcServer server, ISceneDiagnostics diagnostics) : IpcEventSource
{
    private IDisposable? _watch;

    protected override void Attach() => _watch = diagnostics.WatchPlanes(OnPlanes);

    protected override void Detach()
    {
        _watch?.Dispose();
        _watch = null;
    }

    private void OnPlanes(PlaneReport report) =>
        server.Events.Emit(IpcEventNames.DiagPlane, IpcDiagnosticsMethods.Wire(report), IpcJsonContext.Default.IpcDiagPlaneReport);
}
