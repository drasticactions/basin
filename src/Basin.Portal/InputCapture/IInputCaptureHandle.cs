using Basin.Eis;

namespace Basin.Portal;

public interface IInputCaptureHandle : IDisposable
{
    uint ActivationId { get; }

    void ClearBarriers();

    bool TryAddBarrier(in InputCaptureBarrier barrier);

    void Enable();

    void Disable();

    bool Release(uint activationId, double x, double y);

    int TakeEisFd();
}
