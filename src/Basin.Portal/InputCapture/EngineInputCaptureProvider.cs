using Basin.Eis;

namespace Basin.Portal;

/// <summary>
/// Adapts a compositor-side <see cref="InputCaptureEngine"/> to <see cref="IInputCaptureProvider"/>, so that the portal runs in the compositor process.
/// </summary>
public sealed class EngineInputCaptureProvider : IInputCaptureProvider
{
    private readonly InputCaptureEngine _engine;

    /// <summary>
    /// Initializes a new instance of the <see cref="EngineInputCaptureProvider"/> class.
    /// </summary>
    /// <param name="engine">The engine that owns the barriers and the EIS senders.</param>
    /// <exception cref="ArgumentNullException"><paramref name="engine"/> is <see langword="null"/>.</exception>
    public EngineInputCaptureProvider(InputCaptureEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        _engine = engine;
    }

    /// <inheritdoc/>
    public uint ZoneSet => _engine.ZoneSet;

    /// <summary>
    /// Creates an engine session for a portal session. It never returns <see langword="null"/>.
    /// </summary>
    /// <param name="front">The portal session that receives the state changes.</param>
    /// <param name="handle">The session id for log lines.</param>
    /// <returns>The handle to the new engine session. The caller disposes it.</returns>
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
