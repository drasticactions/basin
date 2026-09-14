using Basin.Eis;

namespace Basin.Portal;

public sealed class EngineInputCaptureProvider : IInputCaptureProvider
{
    private readonly InputCaptureEngine _engine;

    public EngineInputCaptureProvider(InputCaptureEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        _engine = engine;
    }

    public uint ZoneSet => _engine.ZoneSet;

    public IInputCaptureHandle? Open(IInputCaptureFront front, string handle) =>
        new Handle(_engine.CreateSession(front, handle));

    private sealed class Handle : IInputCaptureHandle
    {
        private readonly Basin.Eis.InputCaptureSession _session;

        public Handle(Basin.Eis.InputCaptureSession session) => _session = session;

        public uint ActivationId => _session.ActivationId;

        public void ClearBarriers() => _session.ClearBarriers();

        public bool TryAddBarrier(in InputCaptureBarrier barrier) => _session.TryAddBarrier(in barrier);

        public void Enable() => _session.Enable();

        public void Disable() => _session.Disable();

        public bool Release(uint activationId, double x, double y) => _session.Release(activationId, x, y);

        public int TakeEisFd() => _session.Eis.AddClientFd();

        public void Dispose() => _session.Dispose();
    }
}
