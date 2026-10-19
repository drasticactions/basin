using Basin.Capabilities;

namespace Basin.Ipc;

internal sealed class IpcDiagFrameEvents(IpcServer server, IpcDescribe describe, ISceneDiagnostics diagnostics) : IpcEventSource
{
    private IDisposable? _watch;

    protected override void Attach() => _watch = diagnostics.WatchFrames(OnFrame);

    protected override void Detach()
    {
        _watch?.Dispose();
        _watch = null;
    }

    private void OnFrame(FrameInfo frame) =>
        server.Events.Emit(IpcEventNames.DiagFrame, IpcDiagnosticsMethods.Wire(frame, describe), IpcJsonContext.Default.IpcDiagFrame);
}
